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

internal sealed class RazorRegion(RazorRegionKind kind, RazorSourceSpan span, string? name = null)
{
    public RazorRegionKind Kind { get; } = kind;

    public RazorSourceSpan Span { get; } = span;

    public string? Name { get; } = name;
}

internal sealed class RazorDocumentModel(bool isReliable, IReadOnlyList<RazorRegion> regions)
{
    public bool IsReliable { get; } = isReliable;

    public IReadOnlyList<RazorRegion> Regions { get; } = regions;
}
