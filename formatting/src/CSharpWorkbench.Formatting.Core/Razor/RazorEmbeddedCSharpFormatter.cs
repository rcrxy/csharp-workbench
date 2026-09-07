using CSharpWorkbench.Formatting.Core.CSharp.Options;
using CSharpWorkbench.Formatting.Core.CSharp.Roslyn;
using CSharpWorkbench.Formatting.Core.Errors;

namespace CSharpWorkbench.Formatting.Core.Razor;

internal sealed class RazorEmbeddedCSharpFormatter(CSharpRoslynFormatter csharpFormatter)
{
    public async Task<IReadOnlyList<RazorSourceEdit>> FormatAsync(
        string source,
        RazorDocumentModel document,
        CSharpFormattingOptions options,
        CancellationToken cancellationToken)
    {
        var edits = new List<RazorSourceEdit>();
        foreach (var region in document.Regions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (region.CodeBlock is RazorCodeBlockMetadata codeBlock)
            {
                var snippetKind = codeBlock.Kind == RazorCodeBlockKind.Explicit
                    ? CSharpSnippetKind.Statements
                    : CSharpSnippetKind.TypeMembers;
                await TryAddSnippetEditAsync(
                    source,
                    codeBlock.BodySpan,
                    snippetKind,
                    options,
                    false,
                    edits,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (region.Control is RazorControlMetadata control &&
                control.CSharpHeaderSpan is RazorSourceSpan headerSpan)
            {
                await TryAddControlHeaderEditAsync(
                    source,
                    control,
                    headerSpan,
                    options,
                    edits,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (region.Protected is RazorProtectedMetadata protectedMetadata &&
                protectedMetadata.CSharpSpan is RazorSourceSpan csharpSpan)
            {
                var snippetKind = protectedMetadata.Kind == RazorProtectedKind.RazorExpression
                    ? CSharpSnippetKind.Expression
                    : CSharpSnippetKind.Statements;
                await TryAddSnippetEditAsync(
                    source,
                    csharpSpan,
                    snippetKind,
                    options,
                    protectedMetadata.Kind == RazorProtectedKind.RazorExpression,
                    edits,
                    cancellationToken).ConfigureAwait(false);
            }

            if (region.Tag is RazorTagMetadata tag && tag.AttributesReliable)
            {
                foreach (var attribute in tag.Attributes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryGetAttributeCSharpSpan(source, attribute, out var attributeSpan))
                    {
                        await TryAddSnippetEditAsync(
                            source,
                            attributeSpan,
                            CSharpSnippetKind.Expression,
                            options,
                            true,
                            edits,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        return ValidateAndOrder(edits);
    }

    private async Task TryAddControlHeaderEditAsync(
        string source,
        RazorControlMetadata control,
        RazorSourceSpan span,
        CSharpFormattingOptions options,
        ICollection<RazorSourceEdit> edits,
        CancellationToken cancellationToken)
    {
        var original = source.Substring(span.Start, span.Length).TrimEnd();
        string synthetic;
        string extractionMarker;
        switch (control.Kind)
        {
            case RazorControlKind.ElseIf:
                synthetic = original.Substring(original.IndexOf("if", StringComparison.OrdinalIgnoreCase)) + " { }";
                extractionMarker = "if";
                break;
            case RazorControlKind.Catch:
                synthetic = "try { } " + original + " { }";
                extractionMarker = "catch";
                break;
            case RazorControlKind.While when original.EndsWith(";", StringComparison.Ordinal):
                synthetic = original;
                extractionMarker = "while";
                break;
            default:
                synthetic = original + " { }";
                extractionMarker = original.Substring(0, original.IndexOfAny(new[] { ' ', '(' }) is var markerEnd && markerEnd >= 0
                    ? markerEnd
                    : original.Length);
                break;
        }

        try
        {
            var result = await csharpFormatter.FormatAsync(
                new CSharpFormattingRequest(
                    synthetic,
                    CSharpFormattingKind.Snippet,
                    options,
                    snippetKind: CSharpSnippetKind.Statements),
                cancellationToken).ConfigureAwait(false);
            var formatted = ApplyChanges(synthetic, result.Changes);
            var markerStart = formatted.IndexOf(extractionMarker, StringComparison.OrdinalIgnoreCase);
            if (markerStart < 0)
                return;
            var headerEnd = FindHeaderEnd(formatted, markerStart);
            var formattedHeader = formatted.Substring(markerStart, headerEnd - markerStart).TrimEnd();
            if (control.Kind == RazorControlKind.ElseIf)
                formattedHeader = "else " + formattedHeader;
            if (control.Kind == RazorControlKind.While && formattedHeader.EndsWith(";", StringComparison.Ordinal))
                formattedHeader = formattedHeader.TrimEnd(';').TrimEnd() + ";";
            if (HasDifferentLineBreakShape(original, formattedHeader))
                return;
            if (!string.Equals(original, formattedHeader, StringComparison.Ordinal))
                edits.Add(new RazorSourceEdit(span, formattedHeader));
        }
        catch (FormattingException exception) when (
            exception.Code is FormattingErrorCode.ParseFailure or FormattingErrorCode.FormattingFailure)
        {
        }
    }

    private static int FindHeaderEnd(string source, int start)
    {
        var brace = source.IndexOf('{', start);
        var semicolon = source.IndexOf(';', start);
        if (semicolon >= 0 && (brace < 0 || semicolon < brace))
            return semicolon + 1;
        return brace >= 0 ? brace : source.Length;
    }

    private static string ApplyChanges(string source, IReadOnlyList<CSharpTextChange> changes)
    {
        return changes.OrderByDescending(change => change.Span.Start).Aggregate(
            source,
            (current, change) => current.Substring(0, change.Span.Start) + change.NewText +
                current.Substring(change.Span.End));
    }

    private async Task TryAddSnippetEditAsync(
        string source,
        RazorSourceSpan span,
        CSharpSnippetKind snippetKind,
        CSharpFormattingOptions options,
        bool requireSingleLine,
        ICollection<RazorSourceEdit> edits,
        CancellationToken cancellationToken)
    {
        var original = source.Substring(span.Start, span.Length);
        try
        {
            var result = await csharpFormatter.FormatAsync(
                new CSharpFormattingRequest(
                    original,
                    CSharpFormattingKind.Snippet,
                    options,
                    snippetKind: snippetKind),
                cancellationToken).ConfigureAwait(false);
            if (result.Changes.Count == 0)
                return;

            var change = result.Changes.Count == 1
                ? result.Changes[0]
                : throw new InvalidOperationException("C# snippet formatting returned multiple source edits.");
            if (change.Span.Start != 0 || change.Span.Length != original.Length)
                throw new InvalidOperationException("C# snippet formatting returned a non-whole-snippet edit.");
            if (requireSingleLine && HasDifferentLineBreakShape(original, change.NewText))
                return;

            edits.Add(new RazorSourceEdit(span, change.NewText));
        }
        catch (FormattingException exception) when (
            exception.Code is FormattingErrorCode.ParseFailure or FormattingErrorCode.FormattingFailure)
        {
        }
    }

    private static bool TryGetAttributeCSharpSpan(
        string source,
        RazorAttributeMetadata attribute,
        out RazorSourceSpan span)
    {
        span = default;
        if (attribute.ValueSpan is not RazorSourceSpan valueSpan || valueSpan.Length == 0)
            return false;

        var name = source.Substring(attribute.NameSpan.Start, attribute.NameSpan.Length);
        var valueStart = valueSpan.Start;
        var valueEnd = valueSpan.End;
        if (source[valueStart] is '"' or '\'')
        {
            valueStart++;
            valueEnd--;
        }
        if (valueEnd <= valueStart)
            return false;

        if (source[valueStart] == '@')
        {
            if (valueStart + 1 < valueEnd && source[valueStart + 1] == '(' && source[valueEnd - 1] == ')')
                span = new RazorSourceSpan(valueStart + 2, valueEnd - valueStart - 3);
            else
                span = new RazorSourceSpan(valueStart + 1, valueEnd - valueStart - 1);
            return span.Length > 0;
        }

        if (name.StartsWith("@", StringComparison.Ordinal))
        {
            span = new RazorSourceSpan(valueStart, valueEnd - valueStart);
            return true;
        }

        return false;
    }

    private static bool HasDifferentLineBreakShape(string original, string formatted)
    {
        return CountLineBreaks(original) != CountLineBreaks(formatted);
    }

    private static int CountLineBreaks(string value)
    {
        return value.Count(character => character == '\n') +
            value.Count(character => character == '\r' && !value.Contains("\r\n", StringComparison.Ordinal));
    }

    private static IReadOnlyList<RazorSourceEdit> ValidateAndOrder(IEnumerable<RazorSourceEdit> edits)
    {
        var ordered = edits.OrderBy(edit => edit.Span.Start).ToArray();
        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index - 1].Span.End > ordered[index].Span.Start)
                throw new InvalidOperationException("Razor embedded C# source edits overlap.");
        }
        return ordered;
    }
}
