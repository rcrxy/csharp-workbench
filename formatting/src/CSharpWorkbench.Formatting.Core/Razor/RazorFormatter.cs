using CSharpWorkbench.Formatting.Core.Contracts;
using CSharpWorkbench.Formatting.Core.CSharp.Options;
using CSharpWorkbench.Formatting.Core.CSharp.Roslyn;
using CSharpWorkbench.Formatting.Core.Errors;

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

    public async Task<FormattingResult> FormatRangeAsync(
        string source,
        RazorDocumentKind kind,
        RazorSourceSpan requestedRange,
        RazorFormattingOptions options,
        CSharpFormattingOptions csharpOptions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRange(source, requestedRange);
        if (requestedRange.Length == 0)
        {
            return FormattingResult.Unchanged;
        }

        var hasBom = source.Length > 0 && source[0] == '\uFEFF';
        var formattingSource = hasBom ? source.Substring(1) : source;
        if (hasBom && requestedRange.End <= 1)
        {
            return FormattingResult.Unchanged;
        }

        var formattingRange = hasBom
            ? new RazorSourceSpan(
                Math.Max(0, requestedRange.Start - 1),
                requestedRange.End - 1 - Math.Max(0, requestedRange.Start - 1))
            : requestedRange;
        var document = _scanner.Scan(formattingSource, kind, cancellationToken);
        if (!RazorRangeFormattingTargetResolver.TryResolve(
            formattingSource,
            document,
            formattingRange,
            out var target) ||
            target.Kind != RazorRangeFormattingTargetKind.EmbeddedCSharp)
        {
            return FormattingResult.Unchanged;
        }

        var edit = await _embeddedCSharpFormatter.FormatRangeAsync(
            formattingSource,
            document,
            target,
            csharpOptions,
            cancellationToken).ConfigureAwait(false);
        if (edit is null)
        {
            return FormattingResult.Unchanged;
        }

        var resolvedEdit = edit.Value;
        var offset = hasBom ? 1 : 0;
        return new FormattingResult(new[]
        {
            new FormattingTextChange(
                new FormattingTextSpan(resolvedEdit.Span.Start + offset, resolvedEdit.Span.Length),
                resolvedEdit.NewText),
        });
    }

    private static void ValidateRange(string source, RazorSourceSpan range)
    {
        if (range.Start < 0 || range.Length < 0 || range.Start > source.Length - range.Length)
        {
            throw new FormattingException(
                FormattingErrorCode.InvalidSpan,
                "The formatting span must be contained within the source text.");
        }
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
