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
    InlineControl,
    ControlOpenBrace,
    ControlCloseBrace,
    CodeBlock,
    Protected,
}

internal enum RazorCodeBlockKind
{
    Code,
    Functions,
    Explicit,
}

internal enum RazorControlKind
{
    If,
    ElseIf,
    Else,
    For,
    Foreach,
    While,
    Switch,
    Try,
    Catch,
    Finally,
    Using,
    Lock,
    Do,
}

internal enum RazorProtectedKind
{
    RazorExpression,
    CSharpStatement,
    ScriptStyle,
    Declaration,
    RawProtected,
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

internal sealed class RazorCodeBlockMetadata(
    RazorCodeBlockKind kind,
    RazorSourceSpan bodySpan,
    RazorSourceSpan openBraceSpan,
    RazorSourceSpan closeBraceSpan)
{
    public RazorCodeBlockKind Kind { get; } = kind;
    public RazorSourceSpan BodySpan { get; } = bodySpan;
    public RazorSourceSpan OpenBraceSpan { get; } = openBraceSpan;
    public RazorSourceSpan CloseBraceSpan { get; } = closeBraceSpan;
}

internal sealed class RazorControlMetadata(
    RazorControlKind kind,
    RazorSourceSpan headerSpan,
    RazorSourceSpan? csharpHeaderSpan,
    bool isInlineComplete)
{
    public RazorControlKind Kind { get; } = kind;
    public RazorSourceSpan HeaderSpan { get; } = headerSpan;
    public RazorSourceSpan? CSharpHeaderSpan { get; } = csharpHeaderSpan;
    public bool IsInlineComplete { get; } = isInlineComplete;
}

internal sealed class RazorProtectedMetadata(RazorProtectedKind kind, RazorSourceSpan? csharpSpan = null)
{
    public RazorProtectedKind Kind { get; } = kind;
    public RazorSourceSpan? CSharpSpan { get; } = csharpSpan;
}

internal sealed class RazorRegion(
    RazorRegionKind kind,
    RazorSourceSpan span,
    string? name = null,
    RazorTagMetadata? tag = null,
    RazorCodeBlockMetadata? codeBlock = null,
    RazorControlMetadata? control = null,
    RazorProtectedMetadata? protectedMetadata = null)
{
    public RazorRegionKind Kind { get; } = kind;

    public RazorSourceSpan Span { get; } = span;

    public string? Name { get; } = name;

    public RazorTagMetadata? Tag { get; } = tag;
    public RazorCodeBlockMetadata? CodeBlock { get; } = codeBlock;
    public RazorControlMetadata? Control { get; } = control;
    public RazorProtectedMetadata? Protected { get; } = protectedMetadata;
}

internal sealed class RazorDocumentModel(bool isReliable, IReadOnlyList<RazorRegion> regions)
{
    public bool IsReliable { get; } = isReliable;

    public IReadOnlyList<RazorRegion> Regions { get; } = regions;
}
