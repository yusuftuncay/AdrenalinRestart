namespace AdrenalinRestart.Utilities;

internal static class SafeFs
{
    #region Methods
    internal static IEnumerable<string> EnumerateFiles(string directoryPath, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }

    internal static IEnumerable<string> EnumerateDirectories(string directoryPath)
    {
        try
        {
            return Directory.EnumerateDirectories(
                directoryPath,
                "*",
                SearchOption.TopDirectoryOnly
            );
        }
        catch
        {
            return [];
        }
    }
    #endregion
}
