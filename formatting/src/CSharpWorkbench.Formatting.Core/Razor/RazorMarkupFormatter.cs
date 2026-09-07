using System.Text;
using CSharpWorkbench.Formatting.Core.CSharp.Options;

namespace CSharpWorkbench.Formatting.Core.Razor;

internal sealed class RazorMarkupFormatter
{
    public string Format(
        string source,
        RazorDocumentModel document,
        RazorFormattingOptions options,
        CSharpFormattingOptions csharpOptions,
        IReadOnlyList<RazorSourceEdit> csharpEdits,
        CancellationToken cancellationToken)
    {
        var overlay = new RazorSourceOverlay(source, csharpEdits);
        var regions = document.Regions;
        var matchingEnds = FindMatchingEnds(regions);
        var preserveSourceLayout = AreAllLineBreakRulesDisabled(options.Markup);
        var formattedTags = FormatTags(source, overlay, regions, options, preserveSourceLayout, cancellationToken);
        if (preserveSourceLayout)
            return ApplyTagReplacements(source, overlay, regions, formattedTags, 0, regions.Count);

        var builder = new StringBuilder(source.Length + 64);
        var markupDepth = 0;
        var controlDepth = 0;
        var previousControlClose = false;

        for (var index = 0; index < regions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var region = regions[index];
            var raw = overlay.GetText(region.Span);

            if (region.Kind == RazorRegionKind.StartTag && matchingEnds.TryGetValue(index, out var endIndex))
            {
                var name = region.Name ?? string.Empty;
                var preserveInner = options.Markup.PreserveSpacesInsideTags.Contains(name);
                var noIndentInner = options.Markup.NoIndentInsideElements.Contains(name);
                var hasDirectText = HasDirectTextContent(source, regions, index, endIndex);
                var hasChildElements = HasChildElements(regions, index, endIndex);
                var sourceMultiline = ContainsLineBreak(source, region.Span.Start, regions[endIndex].Span.End);
                var formattedOpeningMultiline = ContainsLineBreak(formattedTags[index]);

                if (preserveInner)
                {
                    AppendStructuralValue(
                        builder,
                        GetIndent(markupDepth + EffectiveControlDepth(controlDepth, csharpOptions), options),
                        ComposePreservedElement(source, overlay, regions, formattedTags, index, endIndex));
                    index = endIndex;
                    continue;
                }

                if (noIndentInner)
                {
                    AppendStructuralValue(
                        builder,
                        GetIndent(markupDepth + EffectiveControlDepth(controlDepth, csharpOptions), options),
                        ApplyTagReplacements(source, overlay, regions, formattedTags, index, endIndex + 1));
                    index = endIndex;
                    continue;
                }

                var shouldBreakInside =
                    (options.Markup.LineBreaksInsideElementsWithChildElements && hasChildElements && !hasDirectText) ||
                    (options.Markup.LineBreaksInsideMultilineElements && (sourceMultiline || formattedOpeningMultiline));
                var directTextChildBreak = hasDirectText && HasChildRequiringLineBreak(
                    source,
                    regions,
                    formattedTags,
                    index,
                    endIndex,
                    options.Markup);
                if (directTextChildBreak)
                {
                    AppendStructuralValue(
                        builder,
                        GetIndent(markupDepth + EffectiveControlDepth(controlDepth, csharpOptions), options),
                        FormatDirectTextElement(
                            source,
                            overlay,
                            regions,
                            formattedTags,
                            index,
                            endIndex,
                            markupDepth + EffectiveControlDepth(controlDepth, csharpOptions),
                            options));
                    index = endIndex;
                    continue;
                }
                if (IsInlineLeaf(source, regions, index, endIndex) ||
                    (hasDirectText && !options.Markup.LineBreakBeforeAllElements) ||
                    (!shouldBreakInside && !sourceMultiline))
                {
                    AppendStructuralValue(
                        builder,
                        GetIndent(markupDepth + controlDepth, options),
                        ApplyTagReplacements(source, overlay, regions, formattedTags, index, endIndex + 1));
                    index = endIndex;
                    continue;
                }
            }

            switch (region.Kind)
            {
                case RazorRegionKind.StartTag:
                    AppendStructuralValue(builder, GetIndent(
                        markupDepth + EffectiveControlDepth(controlDepth, csharpOptions), options), formattedTags[index]);
                    markupDepth++;
                    previousControlClose = false;
                    break;
                case RazorRegionKind.EndTag:
                    markupDepth = Math.Max(0, markupDepth - 1);
                    AppendStructuralValue(builder, GetIndent(
                        markupDepth + EffectiveControlDepth(controlDepth, csharpOptions), options), raw);
                    previousControlClose = false;
                    break;
                case RazorRegionKind.SelfClosingTag:
                    AppendStructuralValue(builder, GetIndent(
                        markupDepth + EffectiveControlDepth(controlDepth, csharpOptions), options), formattedTags[index]);
                    previousControlClose = false;
                    break;
                case RazorRegionKind.ControlHeader:
                    AppendControlHeader(
                        builder,
                        raw.TrimStart(),
                        region.Control,
                        previousControlClose,
                        GetIndent(markupDepth + EffectiveControlDepth(controlDepth, csharpOptions), options),
                        options,
                        csharpOptions);
                    if (region.Control?.IsInlineComplete == false && raw.IndexOf('{') >= 0)
                        controlDepth++;
                    previousControlClose = false;
                    break;
                case RazorRegionKind.ControlOpenBrace:
                    AppendControlOpenBrace(
                        builder,
                        raw.TrimStart(),
                        GetIndent(markupDepth + EffectiveControlDepth(controlDepth, csharpOptions), options),
                        options,
                        csharpOptions);
                    controlDepth++;
                    previousControlClose = false;
                    break;
                case RazorRegionKind.ControlCloseBrace:
                    controlDepth = Math.Max(0, controlDepth - 1);
                    AppendStructuralValue(builder, GetIndent(
                        markupDepth + EffectiveControlDepth(controlDepth, csharpOptions), options), raw.TrimStart());
                    previousControlClose = true;
                    break;
                case RazorRegionKind.Directive:
                case RazorRegionKind.HtmlComment:
                case RazorRegionKind.RazorComment:
                    AppendStructuralValue(builder, GetIndent(
                        markupDepth + EffectiveControlDepth(controlDepth, csharpOptions), options), raw.TrimStart());
                    previousControlClose = false;
                    break;
                case RazorRegionKind.CodeBlock:
                    var codeBlockIndent = GetIndent(
                        markupDepth + EffectiveControlDepth(controlDepth, csharpOptions),
                        options);
                    if (region.CodeBlock?.Kind == RazorCodeBlockKind.Functions &&
                        options.BlankLinesAroundFunctions is int blankLines)
                    {
                        EnsureTrailingLineBreaks(builder, blankLines + 1);
                        AppendCodeBlock(builder, raw, codeBlockIndent, options);
                        EnsureTrailingLineBreaks(builder, blankLines + 1);
                        previousControlClose = false;
                        break;
                    }
                    AppendCodeBlock(builder, raw, codeBlockIndent, options);
                    previousControlClose = false;
                    break;
                case RazorRegionKind.Protected:
                    if (region.Protected?.Kind == RazorProtectedKind.CSharpStatement)
                    {
                        AppendStructuralValue(
                            builder,
                            GetIndent(
                                markupDepth + EffectiveControlDepth(controlDepth, csharpOptions),
                                options),
                            raw.TrimStart());
                    }
                    else
                    {
                        AppendProtected(builder, raw);
                    }
                    previousControlClose = false;
                    break;
                case RazorRegionKind.Text:
                    AppendText(
                        builder,
                        overlay,
                        regions,
                        index,
                        markupDepth + EffectiveControlDepth(controlDepth, csharpOptions),
                        options);
                    break;
            }
        }

        return builder.ToString();
    }

    private static Dictionary<int, string> FormatTags(
        string source,
        RazorSourceOverlay overlay,
        IReadOnlyList<RazorRegion> regions,
        RazorFormattingOptions options,
        bool preserveSourceLayout,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, string>();
        var markupDepth = 0;
        var controlDepth = 0;
        for (var index = 0; index < regions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var region = regions[index];
            if (region.Kind == RazorRegionKind.EndTag)
                markupDepth = Math.Max(0, markupDepth - 1);
            else if (region.Kind == RazorRegionKind.ControlCloseBrace)
                controlDepth = Math.Max(0, controlDepth - 1);

            if (region.Kind is RazorRegionKind.StartTag or RazorRegionKind.SelfClosingTag)
            {
                var baseIndent = preserveSourceLayout
                    ? GetSourceLineIndent(source, region.Span.Start)
                    : GetIndent(markupDepth + controlDepth, options);
                result[index] = FormatTag(source, overlay, region, baseIndent, options);
            }

            if (region.Kind == RazorRegionKind.StartTag)
                markupDepth++;
            else if (region.Kind == RazorRegionKind.ControlOpenBrace ||
                (region.Kind == RazorRegionKind.ControlHeader && Slice(source, region.Span).IndexOf('{') >= 0))
            {
                controlDepth++;
            }
        }
        return result;
    }

    private static string FormatTag(
        string source,
        RazorSourceOverlay overlay,
        RazorRegion region,
        string baseIndent,
        RazorFormattingOptions options)
    {
        var original = overlay.GetText(region.Span);
        var tag = region.Tag;
        if (tag is null || !tag.AttributesReliable)
            return original;

        var markup = options.Markup;
        if (markup.AttributeStyle == RazorAttributeStyle.DoNotTouch)
            return FormatDoNotTouchTag(source, overlay, region, tag, options);

        var name = overlay.GetText(tag.NameSpan);
        var attributes = tag.Attributes.Select(attribute => FormatAttribute(overlay, attribute, markup)).ToArray();
        var close = GetTagClose(tag.IsSelfClosingSyntax, attributes.Length > 0, markup);
        if (attributes.Length == 0)
            return "<" + name + close;

        var candidate = "<" + name + " " + string.Join(" ", attributes) + close;
        var normalWrap = markup.AttributeWrap == RazorAttributeWrapPolicy.Normal &&
            options.MaxLineLength is int maxLineLength &&
            VisualLength(baseIndent + candidate, options.TabWidth) > maxLineLength;
        var multiline = markup.AttributeStyle switch
        {
            RazorAttributeStyle.OnDifferentLines => true,
            RazorAttributeStyle.FirstAttributeOnSingleLine => attributes.Length > 1,
            _ => normalWrap,
        };
        if (!multiline)
            return candidate;

        var attributeIndent = GetAttributeIndent(baseIndent, name, options);
        return markup.AttributeStyle switch
        {
            RazorAttributeStyle.FirstAttributeOnSingleLine when attributes.Length > 1 =>
                "<" + name + " " + attributes[0] + string.Concat(attributes.Skip(1).Select(attribute =>
                    options.LineEnding + attributeIndent + attribute)) + close,
            RazorAttributeStyle.OnDifferentLines =>
                "<" + name + string.Concat(attributes.Select(attribute =>
                    options.LineEnding + attributeIndent + attribute)) + close,
            _ => "<" + name + options.LineEnding + attributeIndent + string.Join(" ", attributes) + close,
        };
    }

    private static string FormatDoNotTouchTag(
        string source,
        RazorSourceOverlay overlay,
        RazorRegion region,
        RazorTagMetadata tag,
        RazorFormattingOptions options)
    {
        var replacements = new List<(RazorSourceSpan Span, string NewText)>();
        foreach (var attribute in tag.Attributes)
        {
            if (attribute.EqualsSpan is not RazorSourceSpan equalsSpan ||
                attribute.ValueSpan is not RazorSourceSpan valueSpan)
            {
                continue;
            }

            replacements.Add((
                new RazorSourceSpan(attribute.NameSpan.End, valueSpan.Start - attribute.NameSpan.End),
                options.Markup.SpacesAroundAttributeEquals ? " = " : "="));
        }

        var result = overlay.GetText(region.Span, replacements);
        return ApplyClosingDelimiterSpacing(result, tag.IsSelfClosingSyntax, tag.Attributes.Count > 0, options.Markup);
    }

    private static string FormatAttribute(
        RazorSourceOverlay overlay,
        RazorAttributeMetadata attribute,
        RazorMarkupFormattingOptions options)
    {
        var name = overlay.GetText(attribute.NameSpan);
        if (attribute.ValueSpan is not RazorSourceSpan valueSpan)
            return name;

        var equals = options.SpacesAroundAttributeEquals ? " = " : "=";
        return name + equals + overlay.GetText(valueSpan);
    }

    private static string GetTagClose(
        bool selfClosing,
        bool hasAttributes,
        RazorMarkupFormattingOptions options)
    {
        if (selfClosing)
            return options.SpaceBeforeSelfClosing ? " />" : "/>";
        return hasAttributes && options.SpaceAfterLastAttribute ? " >" : ">";
    }

    private static string ApplyClosingDelimiterSpacing(
        string value,
        bool selfClosing,
        bool hasAttributes,
        RazorMarkupFormattingOptions options)
    {
        var delimiterStart = value.Length - (selfClosing ? 2 : 1);
        while (delimiterStart > 0 && (value[delimiterStart - 1] == ' ' || value[delimiterStart - 1] == '\t'))
            delimiterStart--;
        var close = GetTagClose(selfClosing, hasAttributes, options);
        return value.Substring(0, delimiterStart) + close;
    }

    private static string GetAttributeIndent(string baseIndent, string name, RazorFormattingOptions options)
    {
        return options.Markup.AttributeIndent switch
        {
            RazorAttributeIndent.DoubleIndent => baseIndent + GetIndent(2, options),
            RazorAttributeIndent.AlignByFirstAttribute => baseIndent + new string(' ', name.Length + 2),
            _ => baseIndent + GetIndent(1, options),
        };
    }

    private static int VisualLength(string value, int tabWidth)
    {
        var column = 0;
        foreach (var character in value)
        {
            if (character == '\t')
                column += tabWidth - (column % tabWidth);
            else if (character == '\r' || character == '\n')
                column = 0;
            else
                column++;
        }
        return column;
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
        if (ContainsLineBreak(source, regions[startIndex].Span.End, regions[endIndex].Span.Start))
            return false;
        return !HasChildElements(regions, startIndex, endIndex);
    }

    private static bool HasChildElements(IReadOnlyList<RazorRegion> regions, int startIndex, int endIndex)
    {
        var depth = 0;
        for (var index = startIndex + 1; index < endIndex; index++)
        {
            if (regions[index].Kind == RazorRegionKind.StartTag)
            {
                if (depth == 0)
                    return true;
                depth++;
            }
            else if (regions[index].Kind == RazorRegionKind.SelfClosingTag && depth == 0)
            {
                return true;
            }
            else if (regions[index].Kind == RazorRegionKind.EndTag)
            {
                depth--;
            }
        }
        return false;
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

    private static string ComposePreservedElement(
        string source,
        RazorSourceOverlay overlay,
        IReadOnlyList<RazorRegion> regions,
        IReadOnlyDictionary<int, string> formattedTags,
        int startIndex,
        int endIndex)
    {
        var start = regions[startIndex];
        var end = regions[endIndex];
        return formattedTags[startIndex] +
            overlay.GetText(start.Span.End, end.Span.Start - start.Span.End) +
            overlay.GetText(end.Span);
    }

    private static bool HasChildRequiringLineBreak(
        string source,
        IReadOnlyList<RazorRegion> regions,
        IReadOnlyDictionary<int, string> formattedTags,
        int startIndex,
        int endIndex,
        RazorMarkupFormattingOptions options)
    {
        var depth = 0;
        for (var index = startIndex + 1; index < endIndex; index++)
        {
            var region = regions[index];
            if (region.Kind == RazorRegionKind.EndTag)
                depth--;

            if (depth == 0 && region.Kind is RazorRegionKind.StartTag or RazorRegionKind.SelfClosingTag)
            {
                if (options.LineBreakBeforeAllElements ||
                    (options.LineBreakBeforeMultilineElements &&
                        (ContainsLineBreak(formattedTags[index]) || ContainsLineBreak(Slice(source, region.Span)))))
                {
                    return true;
                }
            }

            if (region.Kind == RazorRegionKind.StartTag)
                depth++;
        }
        return false;
    }

    private static string FormatDirectTextElement(
        string source,
        RazorSourceOverlay overlay,
        IReadOnlyList<RazorRegion> regions,
        IReadOnlyDictionary<int, string> formattedTags,
        int startIndex,
        int endIndex,
        int outerDepth,
        RazorFormattingOptions options)
    {
        var start = regions[startIndex].Span.Start;
        var end = regions[endIndex].Span.End;
        var builder = new StringBuilder(end - start + 32);
        var cursor = start;
        var depth = 0;

        for (var index = startIndex; index <= endIndex; index++)
        {
            var region = regions[index];
            if (region.Kind == RazorRegionKind.EndTag)
                depth--;

            if (!formattedTags.TryGetValue(index, out var formatted))
                continue;

            builder.Append(overlay.GetText(cursor, region.Span.Start - cursor));
            var isTopLevelChild = index > startIndex && depth == 1 &&
                region.Kind is RazorRegionKind.StartTag or RazorRegionKind.SelfClosingTag;
            var shouldBreak = isTopLevelChild &&
                (options.Markup.LineBreakBeforeAllElements ||
                    (options.Markup.LineBreakBeforeMultilineElements &&
                        (ContainsLineBreak(formatted) || ContainsLineBreak(Slice(source, region.Span)))));
            if (shouldBreak)
            {
                while (builder.Length > 0 && (builder[builder.Length - 1] == ' ' || builder[builder.Length - 1] == '\t'))
                    builder.Length--;
                if (builder.Length > 0 && builder[builder.Length - 1] != '\n' && builder[builder.Length - 1] != '\r')
                    builder.Append(options.LineEnding).Append(GetIndent(outerDepth + 1, options));
            }
            builder.Append(formatted);
            cursor = region.Span.End;

            if (region.Kind == RazorRegionKind.StartTag)
                depth++;
        }

        builder.Append(overlay.GetText(cursor, end - cursor));
        return builder.ToString();
    }

    private static string ApplyTagReplacements(
        string source,
        RazorSourceOverlay overlay,
        IReadOnlyList<RazorRegion> regions,
        IReadOnlyDictionary<int, string> formattedTags,
        int startIndex,
        int endIndex)
    {
        if (startIndex >= endIndex)
            return string.Empty;

        var start = regions[startIndex].Span.Start;
        var end = regions[endIndex - 1].Span.End;
        var builder = new StringBuilder(end - start + 32);
        var cursor = start;
        for (var index = startIndex; index < endIndex; index++)
        {
            var region = regions[index];
            if (!formattedTags.TryGetValue(index, out var formatted))
                continue;
            builder.Append(overlay.GetText(cursor, region.Span.Start - cursor));
            builder.Append(formatted);
            cursor = region.Span.End;
        }
        builder.Append(overlay.GetText(cursor, end - cursor));
        return builder.ToString();
    }

    private static void AppendStructuralValue(StringBuilder builder, string indent, string value)
    {
        EnsureLineStart(builder);
        builder.Append(indent).Append(value.TrimEnd(' ', '\t', '\r', '\n')).Append('\n');
    }

    private static void AppendControlHeader(
        StringBuilder builder,
        string value,
        RazorControlMetadata? control,
        bool previousControlClose,
        string indent,
        RazorFormattingOptions razorOptions,
        CSharpFormattingOptions csharpOptions)
    {
        var joinContinuation = previousControlClose && control is not null && ShouldJoinContinuation(control.Kind, csharpOptions);
        if (joinContinuation)
        {
            RemoveSingleTrailingLineEnding(builder);
            builder.Append(' ').Append(value.TrimEnd(' ', '\t', '\r', '\n')).Append('\n');
            return;
        }

        if (control?.IsInlineComplete == false && ShouldPlaceControlBraceOnNewLine(csharpOptions))
        {
            var brace = value.IndexOf('{');
            if (brace >= 0)
            {
                AppendStructuralValue(builder, indent, value.Substring(0, brace).TrimEnd());
                AppendStructuralValue(builder, indent, value.Substring(brace).TrimStart());
                return;
            }
        }

        if (control?.IsInlineComplete == true)
        {
            var brace = value.IndexOf('{');
            if (brace > 0 && value[brace - 1] != ' ' && value[brace - 1] != '\t')
                value = value.Substring(0, brace) + " " + value.Substring(brace);
        }

        AppendStructuralValue(builder, indent, value);
    }

    private static void AppendControlOpenBrace(
        StringBuilder builder,
        string value,
        string indent,
        RazorFormattingOptions razorOptions,
        CSharpFormattingOptions csharpOptions)
    {
        if (!ShouldPlaceControlBraceOnNewLine(csharpOptions))
        {
            RemoveSingleTrailingLineEnding(builder);
            builder.Append(' ').Append(value.TrimEnd(' ', '\t', '\r', '\n')).Append('\n');
            return;
        }

        AppendStructuralValue(builder, indent, value);
    }

    private static bool ShouldPlaceControlBraceOnNewLine(CSharpFormattingOptions options)
    {
        return options.CSharpNewLines.BeforeOpenBrace == CSharpOpenBraceMode.All ||
            options.CSharpNewLines.BeforeOpenBrace == CSharpOpenBraceMode.Selected &&
            options.CSharpNewLines.OpenBraceContexts.Contains(CSharpOpenBraceContext.ControlBlocks);
    }

    private static bool ShouldJoinContinuation(RazorControlKind kind, CSharpFormattingOptions options)
    {
        return kind switch
        {
            RazorControlKind.Else or RazorControlKind.ElseIf => !options.CSharpNewLines.BeforeElse,
            RazorControlKind.Catch => !options.CSharpNewLines.BeforeCatch,
            RazorControlKind.Finally => !options.CSharpNewLines.BeforeFinally,
            RazorControlKind.While => true,
            _ => false,
        };
    }

    private static int EffectiveControlDepth(int controlDepth, CSharpFormattingOptions options)
    {
        return options.CSharpIndentation.IndentBlockContents ? controlDepth : 0;
    }

    private static void RemoveSingleTrailingLineEnding(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[builder.Length - 1] == '\n')
        {
            builder.Length--;
            if (builder.Length > 0 && builder[builder.Length - 1] == '\r')
                builder.Length--;
        }
    }

    private static void EnsureTrailingLineBreaks(StringBuilder builder, int count)
    {
        var existing = 0;
        for (var index = builder.Length - 1; index >= 0 && builder[index] == '\n'; index--)
            existing++;
        if (existing < count)
            builder.Append('\n', count - existing);
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

    private static void AppendCodeBlock(
        StringBuilder builder,
        string value,
        string baseIndent,
        RazorFormattingOptions options)
    {
        EnsureLineStart(builder);
        var lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length == 1)
        {
            builder.Append(baseIndent).Append(lines[0].TrimStart()).Append('\n');
            return;
        }

        var bodyIndent = baseIndent + GetIndent(1, options);
        builder.Append(baseIndent).Append(lines[0].TrimStart()).Append('\n');
        for (var index = 1; index < lines.Length - 1; index++)
        {
            if (lines[index].Length == 0)
                builder.Append('\n');
            else
                builder.Append(bodyIndent).Append(lines[index]).Append('\n');
        }
        builder.Append(baseIndent).Append(lines[lines.Length - 1].TrimStart()).Append('\n');
    }

    private static void AppendText(
        StringBuilder builder,
        RazorSourceOverlay overlay,
        IReadOnlyList<RazorRegion> regions,
        int index,
        int depth,
        RazorFormattingOptions options)
    {
        var value = overlay.GetText(regions[index].Span);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (index > 0 && index + 1 < regions.Count && IsTag(regions[index - 1]) && IsTag(regions[index + 1]))
            {
                var blankLines = Math.Max(0, CountLineBreaks(value) - 1);
                var kept = Math.Min(blankLines, options.Markup.MaxBlankLinesBetweenTags);
                builder.Append('\n', kept);
            }
            return;
        }

        EnsureLineStart(builder);
        builder.Append(GetIndent(depth, options)).Append(value.Trim(' ', '\t', '\r', '\n')).Append('\n');
    }

    private static bool IsTag(RazorRegion region)
    {
        return region.Kind is RazorRegionKind.StartTag or RazorRegionKind.EndTag or RazorRegionKind.SelfClosingTag;
    }

    private static int CountLineBreaks(string value)
    {
        var count = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\r')
            {
                count++;
                if (index + 1 < value.Length && value[index + 1] == '\n')
                    index++;
            }
            else if (value[index] == '\n')
            {
                count++;
            }
        }
        return count;
    }

    private static bool AreAllLineBreakRulesDisabled(RazorMarkupFormattingOptions options)
    {
        return !options.LineBreakBeforeAllElements &&
            !options.LineBreakBeforeMultilineElements &&
            !options.LineBreaksInsideMultilineElements &&
            !options.LineBreaksInsideElementsWithChildElements;
    }

    private static bool ContainsLineBreak(string value)
    {
        return value.IndexOfAny(new[] { '\r', '\n' }) >= 0;
    }

    private static bool ContainsLineBreak(string source, int start, int end)
    {
        return end > start && source.IndexOfAny(new[] { '\r', '\n' }, start, end - start) >= 0;
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
