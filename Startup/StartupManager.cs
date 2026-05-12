using System.Runtime.Versioning;
using Microsoft.Win32;

namespace AdrenalinRestart.Startup;

[SupportedOSPlatform("windows")]
internal static class StartupManager
{
    // Registry Key for Current User Startup
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    // Application Name in Registry
    private const string RegistryValueName = "AdrenalinRestart";

    #region Methods
    internal static void Enable()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
            return;

        using var registryKey = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true);
        registryKey?.SetValue(RegistryValueName, $"\"{executablePath}\"");
    }

    internal static void Disable()
    {
        using var registryKey = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true);
        registryKey?.DeleteValue(RegistryValueName, throwOnMissingValue: false);
    }

    internal static bool IsEnabled()
    {
        using var registryKey = Registry.CurrentUser.OpenSubKey(
            StartupRegistryKey,
            writable: false
        );
        return registryKey?.GetValue(RegistryValueName) is not null;
    }
    #endregion
}
