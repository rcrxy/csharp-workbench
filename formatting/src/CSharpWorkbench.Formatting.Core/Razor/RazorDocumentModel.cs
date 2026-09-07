namespace CSharpWorkbench.Formatting.Core.Razor;

internal enum RazorRegionKind
{
    StartTag,
    EndTag,
    SelfClosingTag,
    Text,
    HtmlComment,
    RazorComment,
    Directive,
    ControlHeader,
    ControlOpenBrace,
    ControlCloseBrace,
    CodeBlock,
    Protected,
}

internal readonly struct RazorSourceSpan
{
    public RazorSourceSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => checked(Start + Length);
}

internal sealed class RazorAttributeMetadata(
    RazorSourceSpan span,
    RazorSourceSpan nameSpan,
    RazorSourceSpan? equalsSpan,
    RazorSourceSpan? valueSpan)
{
    public RazorSourceSpan Span { get; } = span;

    public RazorSourceSpan NameSpan { get; } = nameSpan;

    public RazorSourceSpan? EqualsSpan { get; } = equalsSpan;

    public RazorSourceSpan? ValueSpan { get; } = valueSpan;
}

internal sealed class RazorTagMetadata(
    RazorSourceSpan nameSpan,
    IReadOnlyList<RazorAttributeMetadata> attributes,
    bool attributesReliable,
    bool isSelfClosingSyntax)
{
    public RazorSourceSpan NameSpan { get; } = nameSpan;

    public IReadOnlyList<RazorAttributeMetadata> Attributes { get; } = attributes;

    public bool AttributesReliable { get; } = attributesReliable;

    public bool IsSelfClosingSyntax { get; } = isSelfClosingSyntax;
}

internal sealed class RazorRegion(
    RazorRegionKind kind,
    RazorSourceSpan span,
    string? name = null,
    RazorTagMetadata? tag = null)
{
    public RazorRegionKind Kind { get; } = kind;

    public RazorSourceSpan Span { get; } = span;

    public string? Name { get; } = name;

    public RazorTagMetadata? Tag { get; } = tag;
}

internal sealed class RazorDocumentModel(bool isReliable, IReadOnlyList<RazorRegion> regions)
{
    public bool IsReliable { get; } = isReliable;

    public IReadOnlyList<RazorRegion> Regions { get; } = regions;
}
