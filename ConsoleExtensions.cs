using System.Threading.Tasks;

public static class ConsoleEx
{
    private static bool verbose = false;
    public static void SetVerboseOption(bool verbose)
    {
        ConsoleEx.verbose = verbose;
    }
    public static async Task Verbose(params string[] s)
    {
        if (!verbose) return;
        var line = string.Join(" ", s);
        await System.Console.Out.WriteLineAsync(line);
    }

    public static async Task Error(params string[] s)
    {
        var line = string.Join(" ", s);
        await System.Console.Error.WriteLineAsync(line);
    }

    public static async Task Write(params string[] s)
    {
        var line = string.Join(" ", s);
        await System.Console.Out.WriteLineAsync(line);
    }
}
