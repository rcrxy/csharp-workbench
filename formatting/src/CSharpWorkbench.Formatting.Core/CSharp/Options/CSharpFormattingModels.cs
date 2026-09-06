namespace CSharpWorkbench.Formatting.Core.CSharp.Options;

public enum CSharpFormattingKind
{
    Document,
    Range,
    Snippet,
}

public enum CSharpSnippetKind
{
    TypeMembers,
    Statements,
}

public enum CSharpIndentationStyle
{
    Space,
    Tab,
}

public enum CSharpLabelIndentation
{
    FlushLeft,
    OneLessThanCurrent,
    NoChange,
}

public enum CSharpOpenBraceMode
{
    All,
    None,
    Selected,
}

public enum CSharpOpenBraceContext
{
    Accessors,
    AnonymousMethods,
    AnonymousTypes,
    ControlBlocks,
    Events,
    Indexers,
    Lambdas,
    LocalFunctions,
    Methods,
    ObjectCollectionArrayInitializers,
    Properties,
    Types,
}

public enum CSharpBinaryOperatorSpacing
{
    BeforeAndAfter,
    None,
    Ignore,
}

public enum CSharpWrappedOperatorPlacement
{
    BeginningOfLine,
    EndOfLine,
}

public enum CSharpParenthesisSpacingContext
{
    ControlFlowStatements,
    Expressions,
    TypeCasts,
}

public enum CSharpCharset
{
    Utf8,
    Utf8Bom,
    Utf16BigEndian,
    Utf16LittleEndian,
    Latin1,
}

public readonly struct CSharpTextSpan
{
    public CSharpTextSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => checked(Start + Length);
}

public sealed class CSharpTextChange
{
    public CSharpTextChange(CSharpTextSpan span, string newText)
    {
        Span = span;
        NewText = newText ?? throw new ArgumentNullException(nameof(newText));
    }

    public CSharpTextSpan Span { get; }

    public string NewText { get; }
}

public sealed class CSharpFormattingResult(IReadOnlyList<CSharpTextChange> changes)
{
    public static CSharpFormattingResult Unchanged { get; } = new([]);

    public IReadOnlyList<CSharpTextChange> Changes { get; } = changes ?? throw new ArgumentNullException(nameof(changes));
}

public sealed class CSharpFormattingRequest(
    string source,
    CSharpFormattingKind kind,
    CSharpFormattingOptions options,
    CSharpTextSpan? span = null,
    CSharpSnippetKind? snippetKind = null)
{
    public string Source { get; } = source ?? throw new ArgumentNullException(nameof(source));
    public CSharpFormattingKind Kind { get; } = kind;

    public CSharpFormattingOptions Options { get; } = options ?? throw new ArgumentNullException(nameof(options));

    public CSharpTextSpan? Span { get; } = span;

    public CSharpSnippetKind? SnippetKind { get; } = snippetKind;
}

public sealed class CSharpFormattingOptions
{
    public IndentationOptions Indentation { get; set; } = new();

    public int? MaxLineLength { get; set; }

    public CSharpIndentationOptions CSharpIndentation { get; set; } = new();

    public CSharpNewLineOptions CSharpNewLines { get; set; } = new();

    public CSharpSpacingOptions CSharpSpacing { get; set; } = new();

    public CSharpWrappingOptions CSharpWrapping { get; set; } = new();

    public string LineEnding { get; set; } = "\n";

    public bool InsertFinalNewline { get; set; }

    public bool TrimTrailingWhitespace { get; set; }

    public CSharpCharset Charset { get; set; } = CSharpCharset.Utf8;
}

public sealed class CSharpIndentationOptions
{
    public bool IndentBlockContents { get; set; } = true;

    public bool IndentBraces { get; set; }

    public bool IndentCaseContents { get; set; } = true;
    public bool IndentSwitchLabels { get; set; } = true;

    public bool IndentCaseContentsWhenBlock { get; set; } = true;

    public CSharpLabelIndentation IndentLabels { get; set; } = CSharpLabelIndentation.OneLessThanCurrent;
}

public sealed class IndentationOptions
{
    public CSharpIndentationStyle Style { get; set; } = CSharpIndentationStyle.Space;

    public int Size { get; set; } = 4;

    public int TabWidth { get; set; } = 4;
}

public sealed class CSharpNewLineOptions
{
    public CSharpOpenBraceMode BeforeOpenBrace { get; set; } = CSharpOpenBraceMode.All;
    public IReadOnlyList<CSharpOpenBraceContext> OpenBraceContexts
    { get; set; } = [];

    public bool BeforeElse { get; set; } = true;

    public bool BeforeCatch { get; set; } = true;

    public bool BeforeFinally { get; set; } = true;

    public bool BeforeMembersInObjectInitializers { get; set; } = true;

    public bool BeforeMembersInAnonymousTypes { get; set; } = true;

    public bool BetweenQueryExpressionClauses { get; set; } = true;
}

public sealed class CSharpSpacingOptions
{
    public bool AfterControlFlowKeyword { get; set; } = true;

    public CSharpBinaryOperatorSpacing AroundBinaryOperators
    { get; set; } = CSharpBinaryOperatorSpacing.BeforeAndAfter;

    public bool AfterComma { get; set; } = true;

    public bool BeforeComma { get; set; }

    public bool AfterForSemicolon { get; set; } = true;

    public bool BeforeForSemicolon { get; set; }

    public bool AfterCast { get; set; }

    public bool BeforeInheritanceColon { get; set; } = true;

    public bool AfterInheritanceColon { get; set; } = true;

    public bool AfterDot { get; set; }

    public bool BeforeDot { get; set; }

    public bool BeforeOpenSquareBracket { get; set; }

    public bool BetweenEmptySquareBrackets { get; set; }

    public bool BetweenSquareBrackets { get; set; }

    public bool IgnoreSpacesAroundVariableDeclaration { get; set; }

    public bool BetweenMethodCallNameAndOpeningParenthesis { get; set; }

    public bool BetweenMethodCallParameterListParentheses { get; set; }

    public bool BetweenMethodCallEmptyParameterListParentheses { get; set; }

    public bool BetweenMethodDeclarationNameAndOpeningParenthesis { get; set; }
    public bool BetweenMethodDeclarationParameterListParentheses { get; set; }

    public bool BetweenMethodDeclarationEmptyParameterListParentheses { get; set; }

    public IReadOnlyList<CSharpParenthesisSpacingContext> BetweenParentheses { get; set; } = [];
}

public sealed class CSharpWrappingOptions
{
    public bool PreserveSingleLineStatements { get; set; } = true;
    public bool PreserveSingleLineBlocks { get; set; } = true;

    public CSharpWrappedOperatorPlacement OperatorPlacement { get; set; } = CSharpWrappedOperatorPlacement.BeginningOfLine;
}
