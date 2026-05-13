namespace AdrenalinRestart.Configuration;

internal sealed class UserSettings
{
    // Settings File Location
    private static readonly string s_settingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AdrenalinRestart"
    );

    private static readonly string s_settingsFilePath = Path.Combine(
        s_settingsDirectory,
        "settings.ini"
    );

    internal bool StartupEnabled { get; set; } = false;
    internal bool MinimizeToTray { get; set; } = true;
    internal bool StartMinimized { get; set; } = false;

    #region Methods
    internal static UserSettings Load()
    {
        var settings = new UserSettings();

        if (!File.Exists(s_settingsFilePath))
            return settings;

        foreach (var line in File.ReadAllLines(s_settingsFilePath))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || !trimmed.Contains('='))
                continue;

            var equalIndex = trimmed.IndexOf('=');
            var key = trimmed[..equalIndex].Trim();
            var value = trimmed[(equalIndex + 1)..].Trim();
            var isTrue = value.Equals("true", StringComparison.OrdinalIgnoreCase);

            if (key.Equals("StartupEnabled", StringComparison.OrdinalIgnoreCase))
                settings.StartupEnabled = isTrue;
            else if (key.Equals("MinimizeToTray", StringComparison.OrdinalIgnoreCase))
                settings.MinimizeToTray = isTrue;
            else if (key.Equals("StartMinimized", StringComparison.OrdinalIgnoreCase))
                settings.StartMinimized = isTrue;
        }

        return settings;
    }

    internal void Save()
    {
        Directory.CreateDirectory(s_settingsDirectory);

        File.WriteAllLines(
            s_settingsFilePath,
            [
                $"StartupEnabled={(StartupEnabled ? "true" : "false")}",
                $"MinimizeToTray={(MinimizeToTray ? "true" : "false")}",
                $"StartMinimized={(StartMinimized ? "true" : "false")}",
            ]
        );
    }
    #endregion
}
