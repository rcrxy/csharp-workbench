using CSharpWorkbench.Formatting.Core.Contracts;

namespace CSharpWorkbench.Formatting.Core.Razor;

internal enum RazorDocumentKind
{
    Component,
    Cshtml,
}

internal sealed class RazorFormatter
{
    private readonly RazorDocumentScanner _scanner = new();
    private readonly RazorBasicLayoutFormatter _layoutFormatter = new();

    public FormattingResult Format(
        string source,
        RazorDocumentKind kind,
        RazorFormattingOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasBom = source.Length > 0 && source[0] == '\uFEFF';
        var formattingSource = hasBom ? source.Substring(1) : source;
        var document = _scanner.Scan(formattingSource, kind, cancellationToken);
        var formatted = document.IsReliable
            ? _layoutFormatter.Format(formattingSource, document, options, cancellationToken)
            : formattingSource;
        var normalized = NormalizeDocument(formatted, options);
        if (hasBom)
            normalized = "\uFEFF" + normalized;
        return string.Equals(source, normalized, StringComparison.Ordinal)
            ? FormattingResult.Unchanged
            : new FormattingResult(new[]
            {
                new FormattingTextChange(new FormattingTextSpan(0, source.Length), normalized),
            });
    }

    private static string NormalizeDocument(string source, RazorFormattingOptions options)
    {
        var text = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = text.Split('\n');
        if (options.TrimTrailingWhitespace)
        {
            for (var index = 0; index < lines.Length; index++)
            {
                lines[index] = lines[index].TrimEnd(' ', '\t');
            }
        }

        text = string.Join(options.LineEnding, lines);
        text = text.TrimEnd('\r', '\n');
        return options.InsertFinalNewline ? text + options.LineEnding : text;
    }
}
