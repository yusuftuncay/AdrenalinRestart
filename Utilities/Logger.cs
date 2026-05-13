namespace AdrenalinRestart.Utilities;

internal static class Logger
{
    // List Item Alignment Pad
    private static readonly string s_pad = new(' ', 11);

    #region Methods
    internal static void Log(string message, ConsoleColor color = ConsoleColor.White)
    {
        // Write Timestamp and Message Without String Allocation
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write('[');
        Console.Write(DateTime.Now.ToString("HH:mm:ss"));
        Console.Write("] ");
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    internal static void LogList(
        string header,
        IReadOnlyList<string> items,
        ConsoleColor itemColor = ConsoleColor.Gray
    )
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write('[');
        Console.Write(DateTime.Now.ToString("HH:mm:ss"));
        Console.Write("] ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(header);
        foreach (var item in items)
        {
            Console.Write(s_pad);
            Console.ForegroundColor = itemColor;
            Console.WriteLine($"- {item}");
        }
        Console.ResetColor();
    }

    internal static void LogItem(string item, ConsoleColor color = ConsoleColor.Gray)
    {
        Console.Write(s_pad);
        Console.ForegroundColor = color;
        Console.WriteLine($"- {item}");
        Console.ResetColor();
    }
    #endregion
}
