using CSharpWorkbench.Formatting.Core.CSharp.Options;

namespace CSharpWorkbench.Formatting.Core.Razor;

internal enum RazorRangeFormattingTargetKind
{
    EmbeddedCSharp,
    Tag,
    MarkupStructure,
    StructuralLeaf,
}

internal readonly struct RazorRangeFormattingTarget
{
    public RazorRangeFormattingTarget(
        RazorRangeFormattingTargetKind kind,
        RazorSourceSpan effectiveSpan,
        int startRegionIndex,
        int endRegionIndex,
        int initialMarkupDepth,
        int initialControlDepth,
        RazorSourceSpan? csharpContainerSpan = null,
        CSharpSnippetKind? csharpSnippetKind = null)
    {
        Kind = kind;
        EffectiveSpan = effectiveSpan;
        StartRegionIndex = startRegionIndex;
        EndRegionIndex = endRegionIndex;
        InitialMarkupDepth = initialMarkupDepth;
        InitialControlDepth = initialControlDepth;
        CSharpContainerSpan = csharpContainerSpan;
        CSharpSnippetKind = csharpSnippetKind;
    }

    public RazorRangeFormattingTargetKind Kind { get; }
    public RazorSourceSpan EffectiveSpan { get; }
    public int StartRegionIndex { get; }
    public int EndRegionIndex { get; }
    public int InitialMarkupDepth { get; }
    public int InitialControlDepth { get; }
    public RazorSourceSpan? CSharpContainerSpan { get; }
    public CSharpSnippetKind? CSharpSnippetKind { get; }
}

internal static class RazorRangeFormattingTargetResolver
{
    public static bool TryResolve(
        string source,
        RazorDocumentModel document,
        RazorSourceSpan requestedSpan,
        out RazorRangeFormattingTarget target)
    {
        target = default;
        if (!document.IsReliable || requestedSpan.Length == 0 ||
            string.IsNullOrWhiteSpace(source.Substring(requestedSpan.Start, requestedSpan.Length)))
        {
            return false;
        }

        var facts = BuildFacts(source, document.Regions);
        if (TryResolveEmbeddedCSharp(source, document.Regions, facts, requestedSpan, out target))
        {
            return true;
        }

        if (document.Regions.Any(region =>
            IsHardProtected(region) && Intersects(region.Span, requestedSpan)))
        {
            return false;
        }

        for (var index = 0; index < document.Regions.Count; index++)
        {
            var region = document.Regions[index];
            if (region.Tag is not null && Contains(region.Span, requestedSpan))
            {
                target = CreateTarget(
                    RazorRangeFormattingTargetKind.Tag,
                    region.Span,
                    index,
                    index,
                    facts);
                return true;
            }
        }

        if (TryResolveSiblingRange(document.Regions, facts, requestedSpan, out target) ||
            TryResolveElement(document.Regions, facts, requestedSpan, out target))
        {
            return true;
        }

        for (var index = 0; index < document.Regions.Count; index++)
        {
            var region = document.Regions[index];
            if (region.Kind is RazorRegionKind.Directive or RazorRegionKind.HtmlComment or RazorRegionKind.RazorComment &&
                Contains(region.Span, requestedSpan))
            {
                target = CreateTarget(
                    RazorRangeFormattingTargetKind.StructuralLeaf,
                    region.Span,
                    index,
                    index,
                    facts);
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveEmbeddedCSharp(
        string source,
        IReadOnlyList<RazorRegion> regions,
        StructureFacts facts,
        RazorSourceSpan requestedSpan,
        out RazorRangeFormattingTarget target)
    {
        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];
            if (region.CodeBlock is RazorCodeBlockMetadata codeBlock && Contains(codeBlock.BodySpan, requestedSpan))
            {
                target = CreateEmbeddedTarget(
                    requestedSpan,
                    codeBlock.BodySpan,
                    codeBlock.Kind == RazorCodeBlockKind.Explicit
                        ? CSharpSnippetKind.Statements
                        : CSharpSnippetKind.TypeMembers,
                    index,
                    facts);
                return true;
            }

            if (region.Control?.CSharpHeaderSpan is RazorSourceSpan headerSpan && Contains(headerSpan, requestedSpan))
            {
                target = CreateEmbeddedTarget(
                    headerSpan,
                    headerSpan,
                    CSharpSnippetKind.Statements,
                    index,
                    facts);
                return true;
            }

            if (region.Protected?.CSharpSpan is RazorSourceSpan protectedSpan && Contains(protectedSpan, requestedSpan))
            {
                target = CreateEmbeddedTarget(
                    protectedSpan,
                    protectedSpan,
                    region.Protected.Kind == RazorProtectedKind.RazorExpression
                        ? CSharpSnippetKind.Expression
                        : CSharpSnippetKind.Statements,
                    index,
                    facts);
                return true;
            }

            if (region.Tag is null || !region.Tag.AttributesReliable)
            {
                continue;
            }

            foreach (var attribute in region.Tag.Attributes)
            {
                if (RazorEmbeddedCSharpSpanResolver.TryGetAttributeCSharpSpan(source, attribute, out var span) &&
                    Contains(span, requestedSpan))
                {
                    target = CreateEmbeddedTarget(
                        span,
                        span,
                        CSharpSnippetKind.Expression,
                        index,
                        facts);
                    return true;
                }
            }
        }

        target = default;
        return false;
    }

    private static bool TryResolveSiblingRange(
        IReadOnlyList<RazorRegion> regions,
        StructureFacts facts,
        RazorSourceSpan requestedSpan,
        out RazorRangeFormattingTarget target)
    {
        var contained = facts.Elements
            .Where(element => Contains(requestedSpan, element.Span))
            .OrderBy(element => element.Span.Start)
            .ToArray();
        if (contained.Length == 0)
        {
            target = default;
            return false;
        }

        var parent = contained[0].ParentStartIndex;
        if (contained.Any(element => element.ParentStartIndex != parent))
        {
            target = default;
            return false;
        }

        var effectiveSpan = new RazorSourceSpan(
            contained[0].Span.Start,
            contained[contained.Length - 1].Span.End - contained[0].Span.Start);
        var hasOutsideStructure = regions.Any(region =>
            region.Kind != RazorRegionKind.Text &&
            Intersects(region.Span, requestedSpan) &&
            !Contains(effectiveSpan, region.Span));
        if (hasOutsideStructure)
        {
            target = default;
            return false;
        }

        target = CreateTarget(
            RazorRangeFormattingTargetKind.MarkupStructure,
            effectiveSpan,
            contained[0].StartIndex,
            contained[contained.Length - 1].EndIndex,
            facts);
        return true;
    }

    private static bool TryResolveElement(
        IReadOnlyList<RazorRegion> regions,
        StructureFacts facts,
        RazorSourceSpan requestedSpan,
        out RazorRangeFormattingTarget target)
    {
        var intersectsStructure = regions.Any(region =>
            region.Kind != RazorRegionKind.Text && Intersects(region.Span, requestedSpan));
        if (!intersectsStructure)
        {
            target = default;
            return false;
        }

        var element = facts.Elements
            .Where(candidate => Contains(candidate.Span, requestedSpan))
            .OrderBy(candidate => candidate.Span.Length)
            .FirstOrDefault();
        if (element.Span.Length == 0)
        {
            target = default;
            return false;
        }

        target = CreateTarget(
            RazorRangeFormattingTargetKind.MarkupStructure,
            element.Span,
            element.StartIndex,
            element.EndIndex,
            facts);
        return true;
    }

    private static RazorRangeFormattingTarget CreateEmbeddedTarget(
        RazorSourceSpan effectiveSpan,
        RazorSourceSpan containerSpan,
        CSharpSnippetKind snippetKind,
        int regionIndex,
        StructureFacts facts)
    {
        return new RazorRangeFormattingTarget(
            RazorRangeFormattingTargetKind.EmbeddedCSharp,
            effectiveSpan,
            regionIndex,
            regionIndex,
            facts.MarkupDepthBefore[regionIndex],
            facts.ControlDepthBefore[regionIndex],
            containerSpan,
            snippetKind);
    }

    private static RazorRangeFormattingTarget CreateTarget(
        RazorRangeFormattingTargetKind kind,
        RazorSourceSpan span,
        int startIndex,
        int endIndex,
        StructureFacts facts)
    {
        return new RazorRangeFormattingTarget(
            kind,
            span,
            startIndex,
            endIndex,
            facts.MarkupDepthBefore[startIndex],
            facts.ControlDepthBefore[startIndex]);
    }

    private static bool IsHardProtected(RazorRegion region)
    {
        return region.Protected?.Kind is RazorProtectedKind.ScriptStyle or
            RazorProtectedKind.Declaration or
            RazorProtectedKind.RawProtected;
    }

    private static StructureFacts BuildFacts(string source, IReadOnlyList<RazorRegion> regions)
    {
        var markupDepthBefore = new int[regions.Count];
        var controlDepthBefore = new int[regions.Count];
        var elements = new List<ElementFact>();
        var stack = new Stack<int>();
        var parentByStart = new Dictionary<int, int>();
        var markupDepth = 0;
        var controlDepth = 0;

        for (var index = 0; index < regions.Count; index++)
        {
            var region = regions[index];
            if (region.Kind == RazorRegionKind.EndTag)
            {
                markupDepth = Math.Max(0, markupDepth - 1);
            }
            else if (region.Kind == RazorRegionKind.ControlCloseBrace)
            {
                controlDepth = Math.Max(0, controlDepth - 1);
            }

            markupDepthBefore[index] = markupDepth;
            controlDepthBefore[index] = controlDepth;

            if (region.Kind == RazorRegionKind.StartTag)
            {
                parentByStart[index] = stack.Count > 0 ? stack.Peek() : -1;
                stack.Push(index);
                markupDepth++;
            }
            else if (region.Kind == RazorRegionKind.SelfClosingTag)
            {
                elements.Add(new ElementFact(index, index, stack.Count > 0 ? stack.Peek() : -1, region.Span));
            }
            else if (region.Kind == RazorRegionKind.EndTag && stack.Count > 0)
            {
                var startIndex = stack.Pop();
                var start = regions[startIndex].Span.Start;
                elements.Add(new ElementFact(
                    startIndex,
                    index,
                    parentByStart[startIndex],
                    new RazorSourceSpan(start, region.Span.End - start)));
            }

            if (region.Kind == RazorRegionKind.ControlOpenBrace ||
                region.Kind == RazorRegionKind.ControlHeader &&
                region.Control?.IsInlineComplete == false &&
                source.Substring(region.Span.Start, region.Span.Length).IndexOf('{') >= 0)
            {
                controlDepth++;
            }
        }

        return new StructureFacts(markupDepthBefore, controlDepthBefore, elements);
    }

    private static bool Contains(RazorSourceSpan outer, RazorSourceSpan inner)
    {
        return inner.Start >= outer.Start && inner.End <= outer.End;
    }

    private static bool Intersects(RazorSourceSpan left, RazorSourceSpan right)
    {
        return left.Start < right.End && right.Start < left.End;
    }

    private sealed class StructureFacts(
        int[] markupDepthBefore,
        int[] controlDepthBefore,
        IReadOnlyList<ElementFact> elements)
    {
        public int[] MarkupDepthBefore { get; } = markupDepthBefore;
        public int[] ControlDepthBefore { get; } = controlDepthBefore;
        public IReadOnlyList<ElementFact> Elements { get; } = elements;
    }

    private readonly struct ElementFact(
        int startIndex,
        int endIndex,
        int parentStartIndex,
        RazorSourceSpan span)
    {
        public int StartIndex { get; } = startIndex;
        public int EndIndex { get; } = endIndex;
        public int ParentStartIndex { get; } = parentStartIndex;
        public RazorSourceSpan Span { get; } = span;
    }
}
