using System.Text;

namespace CSharpWorkbench.Formatting.Core.Razor;

internal sealed class RazorBasicLayoutFormatter
{
    public string Format(
        string source,
        RazorDocumentModel document,
        RazorFormattingOptions options,
        CancellationToken cancellationToken)
    {
        var regions = document.Regions;
        var matchingEnds = FindMatchingEnds(regions);
        var builder = new StringBuilder(source.Length + 32);
        var markupDepth = 0;
        var controlDepth = 0;

        for (var index = 0; index < regions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var region = regions[index];
            var raw = Slice(source, region.Span);

            if (region.Kind == RazorRegionKind.StartTag &&
                matchingEnds.TryGetValue(index, out var endIndex) &&
                (IsInlineLeaf(source, regions, index, endIndex) ||
                    HasDirectTextContent(source, regions, index, endIndex)))
            {
                AppendStructuralLine(builder, GetIndent(markupDepth + controlDepth, options),
                    source.Substring(region.Span.Start, regions[endIndex].Span.End - region.Span.Start));
                index = endIndex;
                continue;
            }

            switch (region.Kind)
            {
                case RazorRegionKind.StartTag:
                    AppendStructuralLine(builder, GetIndent(markupDepth + controlDepth, options),
                        RebaseMultiline(source, region.Span.Start, raw, GetIndent(markupDepth + controlDepth, options)));
                    markupDepth++;
                    break;
                case RazorRegionKind.EndTag:
                    markupDepth = Math.Max(0, markupDepth - 1);
                    AppendStructuralLine(builder, GetIndent(markupDepth + controlDepth, options), raw);
                    break;
                case RazorRegionKind.SelfClosingTag:
                    AppendStructuralLine(builder, GetIndent(markupDepth + controlDepth, options),
                        RebaseMultiline(source, region.Span.Start, raw, GetIndent(markupDepth + controlDepth, options)));
                    break;
                case RazorRegionKind.ControlHeader:
                    AppendStructuralLine(builder, GetIndent(markupDepth + controlDepth, options), raw.TrimStart());
                    if (raw.IndexOf('{') >= 0)
                        controlDepth++;
                    break;
                case RazorRegionKind.ControlOpenBrace:
                    AppendStructuralLine(builder, GetIndent(markupDepth + controlDepth, options), raw.TrimStart());
                    controlDepth++;
                    break;
                case RazorRegionKind.ControlCloseBrace:
                    controlDepth = Math.Max(0, controlDepth - 1);
                    AppendStructuralLine(builder, GetIndent(markupDepth + controlDepth, options), raw.TrimStart());
                    break;
                case RazorRegionKind.Directive:
                    AppendStructuralLine(builder, GetIndent(markupDepth + controlDepth, options), raw.TrimStart());
                    break;
                case RazorRegionKind.HtmlComment:
                case RazorRegionKind.RazorComment:
                    AppendStructuralLine(builder, GetIndent(markupDepth + controlDepth, options), raw.TrimStart());
                    break;
                case RazorRegionKind.CodeBlock:
                case RazorRegionKind.Protected:
                    AppendProtected(builder, raw);
                    break;
                case RazorRegionKind.Text:
                    AppendText(builder, raw, markupDepth + controlDepth, options);
                    break;
            }
        }

        return builder.ToString();
    }

    private static Dictionary<int, int> FindMatchingEnds(IReadOnlyList<RazorRegion> regions)
    {
        var result = new Dictionary<int, int>();
        var stack = new Stack<int>();
        for (var index = 0; index < regions.Count; index++)
        {
            if (regions[index].Kind == RazorRegionKind.StartTag)
                stack.Push(index);
            else if (regions[index].Kind == RazorRegionKind.EndTag && stack.Count > 0)
                result[stack.Pop()] = index;
        }
        return result;
    }

    private static bool IsInlineLeaf(
        string source,
        IReadOnlyList<RazorRegion> regions,
        int startIndex,
        int endIndex)
    {
        var start = regions[startIndex].Span.End;
        var end = regions[endIndex].Span.Start;
        if (source.IndexOfAny(new[] { '\r', '\n' }, start, end - start) >= 0)
            return false;

        for (var index = startIndex + 1; index < endIndex; index++)
        {
            if (regions[index].Kind is RazorRegionKind.StartTag or RazorRegionKind.SelfClosingTag or
                RazorRegionKind.ControlHeader or RazorRegionKind.Directive)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasDirectTextContent(
        string source,
        IReadOnlyList<RazorRegion> regions,
        int startIndex,
        int endIndex)
    {
        var depth = 0;
        for (var index = startIndex + 1; index < endIndex; index++)
        {
            var region = regions[index];
            if (region.Kind == RazorRegionKind.StartTag)
            {
                depth++;
                continue;
            }
            if (region.Kind == RazorRegionKind.EndTag)
            {
                depth--;
                continue;
            }
            if (depth == 0 && region.Kind == RazorRegionKind.Text &&
                !string.IsNullOrWhiteSpace(Slice(source, region.Span)))
            {
                return true;
            }
        }
        return false;
    }

    private static void AppendStructuralLine(StringBuilder builder, string indent, string value)
    {
        EnsureLineStart(builder);
        builder.Append(indent).Append(value.TrimEnd(' ', '\t', '\r', '\n')).Append('\n');
    }

    private static void AppendProtected(StringBuilder builder, string value)
    {
        if (value.Length == 0)
            return;
        if (builder.Length > 0 && builder[builder.Length - 1] != '\n' && builder[builder.Length - 1] != '\r')
            builder.Append('\n');
        builder.Append(value);
        if (value[value.Length - 1] != '\n' && value[value.Length - 1] != '\r')
            builder.Append('\n');
    }

    private static void AppendText(
        StringBuilder builder,
        string value,
        int depth,
        RazorFormattingOptions options)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var content = value.Trim(' ', '\t', '\r', '\n');
        if (content.Length == 0)
            return;
        EnsureLineStart(builder);
        builder.Append(GetIndent(depth, options)).Append(content).Append('\n');
    }

    private static string RebaseMultiline(string source, int start, string value, string targetIndent)
    {
        if (value.IndexOfAny(new[] { '\r', '\n' }) < 0)
            return value;

        var sourceIndent = GetSourceLineIndent(source, start);
        var lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder(value.Length + targetIndent.Length * lines.Length);
        builder.Append(lines[0]);
        for (var index = 1; index < lines.Length; index++)
        {
            builder.Append('\n').Append(targetIndent);
            builder.Append(lines[index].StartsWith(sourceIndent, StringComparison.Ordinal)
                ? lines[index].Substring(sourceIndent.Length)
                : lines[index]);
        }
        return builder.ToString();
    }

    private static string GetSourceLineIndent(string source, int position)
    {
        var lineStart = position;
        while (lineStart > 0 && source[lineStart - 1] != '\n' && source[lineStart - 1] != '\r')
            lineStart--;
        var cursor = lineStart;
        while (cursor < position && (source[cursor] == ' ' || source[cursor] == '\t'))
            cursor++;
        return source.Substring(lineStart, cursor - lineStart);
    }

    private static void EnsureLineStart(StringBuilder builder)
    {
        while (builder.Length > 0 && (builder[builder.Length - 1] == ' ' || builder[builder.Length - 1] == '\t'))
            builder.Length--;
        if (builder.Length > 0 && builder[builder.Length - 1] != '\n' && builder[builder.Length - 1] != '\r')
            builder.Append('\n');
    }

    private static string GetIndent(int depth, RazorFormattingOptions options)
    {
        if (depth <= 0)
            return string.Empty;
        return options.UseTabs ? new string('\t', depth) : new string(' ', depth * options.IndentSize);
    }

    private static string Slice(string source, RazorSourceSpan span) => source.Substring(span.Start, span.Length);
}
