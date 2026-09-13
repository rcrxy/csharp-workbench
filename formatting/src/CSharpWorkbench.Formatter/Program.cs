namespace CSharpWorkbench.Formatter;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || !string.Equals(args[0], "server", StringComparison.Ordinal))
        {
            await Console.Error.WriteLineAsync("Usage: CSharpWorkbench.Formatter server");
            return 1;
        }

        try
        {
            var server = new FormatterServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
            await server.RunAsync();
            return 0;
        }
        catch (Protocol.WireProtocolException exception)
        {
            await Console.Error.WriteLineAsync(exception.Message);
            return 1;
        }
    }
}
