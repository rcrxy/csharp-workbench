namespace CSharpWorkbench.Formatting.Core.Razor;

internal sealed class RazorFormattingOptions
{
    public bool UseTabs { get; set; }

    public int IndentSize { get; set; } = 4;

    public int TabWidth { get; set; } = 4;

    public string LineEnding { get; set; } = "\n";

    public bool InsertFinalNewline { get; set; }

    public bool TrimTrailingWhitespace { get; set; }
}
