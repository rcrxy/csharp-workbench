using CSharpWorkbench.Formatting.Core.Contracts;
using CSharpWorkbench.Formatting.Core.CSharp.Options;
using CSharpWorkbench.Formatting.Core.CSharp.Roslyn;

namespace CSharpWorkbench.Formatting.Core.Razor;

internal enum RazorDocumentKind
{
    Component,
    Cshtml,
}

internal sealed class RazorFormatter(CSharpRoslynFormatter csharpFormatter)
{
    private readonly RazorDocumentScanner _scanner = new();
    private readonly RazorMarkupFormatter _markupFormatter = new();
    private readonly RazorEmbeddedCSharpFormatter _embeddedCSharpFormatter = new(csharpFormatter);

    public async Task<FormattingResult> FormatAsync(
        string source,
        RazorDocumentKind kind,
        RazorFormattingOptions options,
        CSharpFormattingOptions csharpOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var hasBom = source.Length > 0 && source[0] == '\uFEFF';
        var formattingSource = hasBom ? source.Substring(1) : source;
        var document = _scanner.Scan(formattingSource, kind, cancellationToken);
        var csharpEdits = document.IsReliable
            ? await _embeddedCSharpFormatter.FormatAsync(
                formattingSource,
                document,
                csharpOptions,
                cancellationToken).ConfigureAwait(false)
            : Array.Empty<RazorSourceEdit>();
        var formatted = document.IsReliable
            ? _markupFormatter.Format(
                formattingSource,
                document,
                options,
                csharpOptions,
                csharpEdits,
                cancellationToken)
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
