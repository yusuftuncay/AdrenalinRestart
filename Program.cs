using System.Reflection;
using System.Runtime.Versioning;
using AdrenalinRestart.Amd;
using AdrenalinRestart.Configuration;
using AdrenalinRestart.Game;
using AdrenalinRestart.Native;
using AdrenalinRestart.Startup;
using AdrenalinRestart.Tray;
using static AdrenalinRestart.Utilities.Logger;

namespace AdrenalinRestart;

[SupportedOSPlatform("windows")]
internal static partial class Program
{
    // Pending Reset Flag
    private static int s_pendingResetFlag;

    // Active User Settings
    private static UserSettings s_userSettings = new();

    // Tray Manager Instance
    private static TrayManager? s_trayManager;

    // Monitoring Cancellation Source
    private static CancellationTokenSource s_monitoringCancellationTokenSource = new();

    // Game Process Names for Monitoring
    private static HashSet<string> s_gameProcessNames = new(StringComparer.OrdinalIgnoreCase);

    // Game Process Name to Display Name Map
    private static Dictionary<string, string> s_gameProcessNameToDisplayName = [];

    // Console Close Handler Reference (Kept Alive)
    private static NativeMethods.ConsoleCtrlHandlerDelegate? s_consoleCtrlHandler;

    #region Entry Point
    private static async Task Main()
    {
        // Exit if not Running on Windows
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows Only Tool");
            Console.WriteLine("Press Any Key");
            Console.ReadKey(true);
            return;
        }

        // Require Admin Rights at Startup
        if (!AmdReset.IsAdministrator())
        {
            if (AmdReset.TryRelaunchElevated())
            {
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine("Admin Rights Required");
                Console.WriteLine("Press Any Key");
                Console.ReadKey(true);
            }

            return;
        }

        // Load Persistent Settings
        s_userSettings = UserSettings.Load();

        // Apply Startup Registration
        ApplyStartupRegistration();

        // Intercept Console Close Button to Minimize to Tray Instead
        s_consoleCtrlHandler = HandleConsoleControl;
        NativeMethods.SetConsoleCtrlHandler(s_consoleCtrlHandler, add: true);

        // Launch Windows Forms Message Pump for Tray Icon
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        _ = Task.Run(RunApplicationMessagePump);

        PrintConsoleHeader();
        PrintSettingsStatus();
        PrintCommandHelp();

        // Scan Installed Games
        s_gameProcessNameToDisplayName = GameScanner.ScanInstalledGameProcessNames();
        s_gameProcessNames = new HashSet<string>(
            s_gameProcessNameToDisplayName.Keys,
            StringComparer.OrdinalIgnoreCase
        );

        // Print Discovered Games (Deduplicated by Display Name)
        var uniqueDisplayNames = s_gameProcessNameToDisplayName
            .Values.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        LogList($"Games Found: {uniqueDisplayNames.Count}", uniqueDisplayNames, ConsoleColor.Cyan);

        // Exit if no Games are Found
        if (s_gameProcessNames.Count == 0)
        {
            Log("No Games Found", ConsoleColor.Red);
            Console.WriteLine("Press Any Key");
            Console.ReadKey(true);
            return;
        }

        // Hide Console if StartMinimized is Enabled
        if (s_userSettings.StartMinimized)
            HideConsoleWindow();

        // Set up Ctrl+C Cancellation
        Console.CancelKeyPress += (_, cancelEventArgs) =>
        {
            cancelEventArgs.Cancel = true;
        };

        // Start Background Key Listener
        _ = WatchForManualTriggerAsync(s_monitoringCancellationTokenSource.Token);

        // Start Monitoring Loop
        await RunMonitoringLoopAsync(s_monitoringCancellationTokenSource.Token)
            .ConfigureAwait(false);
    }
    #endregion

    #region Methods
    private static void RunApplicationMessagePump()
    {
        // Initialize Tray Manager on the Message Pump Thread
        s_trayManager = new TrayManager(
            openConsoleCallback: ShowConsoleWindow,
            resetCallback: () =>
                _ = TryTriggerResetAsync(s_gameProcessNames, "Manual Reset", isManual: true),
            restartMonitoringCallback: RestartMonitoring,
            exitCallback: ExitApplication
        );

        Application.Run();
    }

    private static bool HandleConsoleControl(uint controlType)
    {
        // Intercept Close Button and Minimize to Tray Instead of Exiting
        if (controlType == NativeMethods.CtrlCloseEvent && s_userSettings.MinimizeToTray)
        {
            HideConsoleWindow();
            s_trayManager?.ShowBalloonTip("Adrenalin Restart", "Still running in the background");
            return true;
        }

        return false;
    }

    private static void HideConsoleWindow()
    {
        var consoleWindowHandle = NativeMethods.GetConsoleWindow();
        if (consoleWindowHandle != IntPtr.Zero)
            NativeMethods.ShowWindow(consoleWindowHandle, NativeMethods.SwHide);
    }

    private static void ShowConsoleWindow()
    {
        var consoleWindowHandle = NativeMethods.GetConsoleWindow();
        if (consoleWindowHandle != IntPtr.Zero)
            NativeMethods.ShowWindow(consoleWindowHandle, NativeMethods.SwRestore);
    }

    private static void RestartMonitoring()
    {
        // Cancel Existing Monitoring Loop
        s_monitoringCancellationTokenSource.Cancel();
        s_monitoringCancellationTokenSource.Dispose();
        s_monitoringCancellationTokenSource = new CancellationTokenSource();

        Log("Monitoring Restarted", ConsoleColor.Cyan);

        _ = WatchForManualTriggerAsync(s_monitoringCancellationTokenSource.Token);
        _ = RunMonitoringLoopAsync(s_monitoringCancellationTokenSource.Token);
    }

    private static void ExitApplication()
    {
        s_trayManager?.Dispose();
        Application.Exit();
        Environment.Exit(0);
    }

    private static void ApplyStartupRegistration()
    {
        if (s_userSettings.StartupEnabled)
            StartupManager.Enable();
        else
            StartupManager.Disable();
    }

    private static async Task RunMonitoringLoopAsync(CancellationToken cancellationToken)
    {
        Log("Watching for Games", ConsoleColor.Gray);

        var previouslyRunning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!cancellationToken.IsCancellationRequested)
        {
            // Detect Newly Started Games
            var currentlyRunning = GetRunningGameProcesses(s_gameProcessNames);

            var newGameStarted = false;
            string? startedProcessName = null;

            foreach (var runningProcessName in currentlyRunning)
            {
                if (previouslyRunning.Contains(runningProcessName))
                    continue;

                newGameStarted = true;
                startedProcessName = runningProcessName;
                break;
            }

            // Fire and Forget Reset Sequence on New Game Start
            if (newGameStarted)
            {
                var startedDisplayName =
                    startedProcessName is not null
                    && s_gameProcessNameToDisplayName.TryGetValue(
                        startedProcessName,
                        out var niceName
                    )
                        ? niceName
                        : (startedProcessName ?? "Game");

                _ = TryTriggerResetAsync(s_gameProcessNames, startedDisplayName, isManual: false);
            }

            // Advance the Tracking Snapshot
            previouslyRunning = currentlyRunning;

            // Wait for Next Poll Interval
            try
            {
                await Task.Delay(AppConfig.s_pollInterval, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                break;
            }
        }
    }

    private static void PrintConsoleHeader()
    {
        Console.Title = "Adrenalin Restart";

        // Print ASCII Title
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(
            @"              _                      _ _         _____           _             _   "
        );
        Console.WriteLine(
            @"     /\      | |                    | (_)       |  __ \         | |           | |  "
        );
        Console.WriteLine(
            @"    /  \   __| |_ __ ___ _ __   __ _| |_ _ __   | |__) |___  ___| |_ __ _ _ __| |_ "
        );
        Console.WriteLine(
            @"   / /\ \ / _` | '__/ _ \ '_ \ / _` | | | '_ \  |  _  // _ \/ __| __/ _` | '__| __|"
        );
        Console.WriteLine(
            @"  / ____ \ (_| | | |  __/ | | | (_| | | | | | | | | \ \  __/\__ \ || (_| | |  | |_ "
        );
        Console.WriteLine(
            @" /_/    \_\__,_|_|  \___|_| |_|\__,_|_|_|_| |_| |_|  \_\___||___/\__\__,_|_|   \__|"
        );
        Console.ResetColor();
        Console.WriteLine();

        // Print Description
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(
            "Adrenalin Restart Detects Running Games And Restarts AMD Adrenalin Automatically. Supports Steam, Epic, Riot, Rockstar, Roblox And Common Games Folders"
        );
        Console.WriteLine();
        var version = typeof(Program)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        Console.WriteLine($"Version: v{version}");
        Console.WriteLine();

        // Print GitHub Link
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Issues And Features");
        Console.WriteLine("https://github.com/yusuftuncay/AdrenalinRestart");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintSettingsStatus()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Status");
        Console.WriteLine(
            $"  Startup:        {(s_userSettings.StartupEnabled ? "TRUE" : "FALSE")}"
        );
        Console.WriteLine(
            $"  MinimizeToTray: {(s_userSettings.MinimizeToTray ? "TRUE" : "FALSE")}"
        );
        Console.WriteLine(
            $"  StartMinimized: {(s_userSettings.StartMinimized ? "TRUE" : "FALSE")}"
        );
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintCommandHelp()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("Commands");
        PrintCommandLine("  * reset", "Force Restart Adrenalin Now");
        PrintCommandLine("  * status", "Show Current Settings");
        PrintCommandLine("  * save", "Save Settings to Disk");
        PrintCommandLine("  * set startup <true|false>", "Run on Windows Startup");
        PrintCommandLine("  * set tray <true|false>", "Minimize to Tray on Close");
        PrintCommandLine("  * set startminimized <true|false>", "Hide Console on Launch");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void PrintCommandLine(string command, string description)
    {
        // Command in Gray, Description in DarkGray
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write(command);
        Console.Write(" ");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"({description})");
    }

    private static async Task WatchForManualTriggerAsync(CancellationToken cancellationToken)
    {
        var inputBuffer = new System.Text.StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Accumulate Typed Characters and Trigger on Enter
                while (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(intercept: true);

                    if (keyInfo.Key == ConsoleKey.Enter)
                    {
                        var typed = inputBuffer.ToString().Trim();
                        inputBuffer.Clear();

                        // Clear the Typed Input Line
                        Console.Write('\r');
                        Console.Write(new string(' ', typed.Length + 2));
                        Console.Write('\r');

                        HandleConsoleCommand(typed);
                    }
                    else if (keyInfo.Key == ConsoleKey.Backspace)
                    {
                        if (inputBuffer.Length > 0)
                        {
                            inputBuffer.Remove(inputBuffer.Length - 1, 1);
                            // Erase Last Character on Screen
                            Console.Write("\b \b");
                        }
                    }
                    else if (!char.IsControl(keyInfo.KeyChar))
                    {
                        inputBuffer.Append(keyInfo.KeyChar);
                        // Echo the Typed Character
                        Console.Write(keyInfo.KeyChar);
                    }
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                break;
            }
        }
    }

    private static void HandleConsoleCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return;

        // Echo Typed Command with Timestamp
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(input);
        Console.ResetColor();

        var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0];

        if (command.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            _ = TryTriggerResetAsync(s_gameProcessNames, "Manual Reset", isManual: true);
            return;
        }

        if (command.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            PrintSettingsStatus();
            return;
        }

        if (command.Equals("save", StringComparison.OrdinalIgnoreCase))
        {
            s_userSettings.Save();
            Log("Settings Saved", ConsoleColor.Green);
            return;
        }

        if (command.Equals("set", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3)
        {
            var settingName = parts[1];
            var settingValue = parts[2].Equals("true", StringComparison.OrdinalIgnoreCase);

            if (settingName.Equals("startup", StringComparison.OrdinalIgnoreCase))
            {
                s_userSettings.StartupEnabled = settingValue;
                ApplyStartupRegistration();
                Log($"Startup Set To {settingValue}", ConsoleColor.Cyan);
            }
            else if (settingName.Equals("tray", StringComparison.OrdinalIgnoreCase))
            {
                s_userSettings.MinimizeToTray = settingValue;
                Log($"MinimizeToTray Set To {settingValue}", ConsoleColor.Cyan);
            }
            else if (settingName.Equals("startminimized", StringComparison.OrdinalIgnoreCase))
            {
                s_userSettings.StartMinimized = settingValue;
                Log($"StartMinimized Set To {settingValue}", ConsoleColor.Cyan);
            }
            else
            {
                Log($"Unknown Setting: {settingName}", ConsoleColor.Red);
            }

            s_userSettings.Save();
            return;
        }

        Log($"Unknown Command: {input}", ConsoleColor.Red);
        PrintCommandHelp();
    }

    private static HashSet<string> GetRunningGameProcesses(HashSet<string> gameProcessNames)
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var processInstance in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    var processName = processInstance.ProcessName;
                    if (gameProcessNames.Contains(processName))
                    {
                        running.Add(processName);
                    }
                }
                catch { }
                finally
                {
                    try
                    {
                        processInstance.Dispose();
                    }
                    catch { }
                }
            }
        }
        catch { }

        return running;
    }

    private static bool IsAnyTrackedGameRunning(HashSet<string> gameProcessNames)
    {
        try
        {
            foreach (var processInstance in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    var processName = processInstance.ProcessName;
                    if (gameProcessNames.Contains(processName))
                        return true;
                }
                catch { }
                finally
                {
                    try
                    {
                        processInstance.Dispose();
                    }
                    catch { }
                }
            }
        }
        catch { }

        return false;
    }

    private static async Task TryTriggerResetAsync(
        HashSet<string> gameProcessNames,
        string startedDisplayName,
        bool isManual
    )
    {
        // Skip if a Reset is Already Pending
        if (Interlocked.Exchange(ref s_pendingResetFlag, 1) == 1)
            return;

        try
        {
            Log($"Game Detected: {startedDisplayName}", ConsoleColor.Yellow);

            if (!isManual)
            {
                // Wait for Configured Delay
                await Task.Delay(AppConfig.s_gameStartDelay, CancellationToken.None)
                    .ConfigureAwait(false);

                // Abort if Game Closed During Delay
                if (!IsAnyTrackedGameRunning(gameProcessNames))
                {
                    Log("Game Closed Before Reset", ConsoleColor.DarkYellow);
                    Log("Watching for Games", ConsoleColor.Gray);
                    return;
                }
            }

            // Execute Reset
            AmdReset.ExecuteReset();
            Log("Reset Done", ConsoleColor.Green);
            Log("Watching for Games", ConsoleColor.Gray);

            s_trayManager?.ShowBalloonTip(
                "Adrenalin Restart",
                $"Reset Done for {startedDisplayName}"
            );
        }
        finally
        {
            Interlocked.Exchange(ref s_pendingResetFlag, 0);
        }
    }
    #endregion
}
