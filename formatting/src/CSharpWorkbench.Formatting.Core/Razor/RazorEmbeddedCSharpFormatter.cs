using CSharpWorkbench.Formatting.Core.CSharp.Options;
using CSharpWorkbench.Formatting.Core.CSharp.Roslyn;
using CSharpWorkbench.Formatting.Core.Errors;

namespace CSharpWorkbench.Formatting.Core.Razor;

internal sealed class RazorEmbeddedCSharpFormatter(CSharpRoslynFormatter csharpFormatter)
{
    public async Task<RazorSourceEdit?> FormatRangeAsync(
        string source,
        RazorDocumentModel document,
        RazorRangeFormattingTarget target,
        CSharpFormattingOptions options,
        CancellationToken cancellationToken)
    {
        var region = document.Regions[target.StartRegionIndex];
        if (region.CodeBlock is not null &&
            target.CSharpContainerSpan is RazorSourceSpan containerSpan &&
            target.CSharpSnippetKind is CSharpSnippetKind snippetKind)
        {
            return await FormatCodeBlockRangeAsync(
                source,
                target.EffectiveSpan,
                containerSpan,
                snippetKind,
                options,
                cancellationToken).ConfigureAwait(false);
        }

        var edits = new List<RazorSourceEdit>();
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
        }
        else if (target.CSharpSnippetKind is CSharpSnippetKind targetSnippetKind)
        {
            await TryAddSnippetEditAsync(
                source,
                target.EffectiveSpan,
                targetSnippetKind,
                options,
                targetSnippetKind == CSharpSnippetKind.Expression,
                edits,
                cancellationToken).ConfigureAwait(false);
        }

        return edits.Count == 0 ? null : edits[0];
    }

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
                    if (RazorEmbeddedCSharpSpanResolver.TryGetAttributeCSharpSpan(
                        source,
                        attribute,
                        out var attributeSpan))
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

    private async Task<RazorSourceEdit?> FormatCodeBlockRangeAsync(
        string source,
        RazorSourceSpan requestedSpan,
        RazorSourceSpan bodySpan,
        CSharpSnippetKind snippetKind,
        CSharpFormattingOptions options,
        CancellationToken cancellationToken)
    {
        var body = source.Substring(bodySpan.Start, bodySpan.Length);
        var snippetRequest = new CSharpFormattingRequest(
            body,
            CSharpFormattingKind.Snippet,
            options,
            snippetKind: snippetKind);
        var context = SnippetFormattingContext.Create(snippetRequest);
        var localStart = requestedSpan.Start - bodySpan.Start;
        var parserRange = new CSharpTextSpan(
            context.FormattingSpan.Start + localStart,
            requestedSpan.Length);

        try
        {
            var result = await csharpFormatter.FormatAsync(
                new CSharpFormattingRequest(
                    context.ParserSource,
                    CSharpFormattingKind.Range,
                    options,
                    parserRange),
                cancellationToken).ConfigureAwait(false);
            if (result.Changes.Count == 0)
            {
                return null;
            }

            if (result.Changes.Any(change =>
                change.Span.Start < context.FormattingSpan.Start ||
                change.Span.End > context.FormattingSpan.End))
            {
                return null;
            }

            var formattedParserSource = ApplyChanges(context.ParserSource, result.Changes);
            var formattedBody = context.Extract(formattedParserSource, options.Indentation);
            return CreateMinimalEdit(body, formattedBody, bodySpan.Start);
        }
        catch (FormattingException exception) when (
            exception.Code is FormattingErrorCode.ParseFailure or FormattingErrorCode.FormattingFailure)
        {
            return null;
        }
    }

    private static RazorSourceEdit? CreateMinimalEdit(string original, string formatted, int sourceStart)
    {
        if (string.Equals(original, formatted, StringComparison.Ordinal))
        {
            return null;
        }

        var prefixLength = 0;
        var sharedLength = Math.Min(original.Length, formatted.Length);
        while (prefixLength < sharedLength && original[prefixLength] == formatted[prefixLength])
        {
            prefixLength++;
        }

        var suffixLength = 0;
        while (suffixLength < original.Length - prefixLength &&
            suffixLength < formatted.Length - prefixLength &&
            original[original.Length - suffixLength - 1] == formatted[formatted.Length - suffixLength - 1])
        {
            suffixLength++;
        }

        return new RazorSourceEdit(
            new RazorSourceSpan(
                sourceStart + prefixLength,
                original.Length - prefixLength - suffixLength),
            formatted.Substring(prefixLength, formatted.Length - prefixLength - suffixLength));
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
