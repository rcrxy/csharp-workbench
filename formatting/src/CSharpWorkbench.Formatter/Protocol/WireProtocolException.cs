namespace CSharpWorkbench.Formatter.Protocol;

internal sealed class WireProtocolException : Exception
{
    public WireProtocolException(string message)
    : base(message)
    {
    }
}
