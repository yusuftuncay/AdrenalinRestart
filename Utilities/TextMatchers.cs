namespace AdrenalinRestart.Utilities;

internal static class TextMatchers
{
    internal static bool ContainsAnyMarker(string value, string[] markers)
    {
        // Check Each Marker Against the Value
        foreach (var marker in markers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
