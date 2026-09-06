namespace CSharpWorkbench.Formatting.Core.Contracts;

public enum FormattingLanguage
{
    CSharp,
}

public readonly struct FormattingTextSpan
{
    public FormattingTextSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public int Start { get; }
    public int Length { get; }
}

public sealed class FormattingTextChange(FormattingTextSpan span, string newText)
{
    public FormattingTextSpan Span { get; } = span;

    public string NewText { get; } = newText
        ?? throw new ArgumentNullException(nameof(newText));
}

public sealed class FormattingResult(IReadOnlyList<FormattingTextChange> changes)
{
    public static FormattingResult Unchanged { get; } = new([]);

    public IReadOnlyList<FormattingTextChange> Changes { get; } = changes
        ?? throw new ArgumentNullException(nameof(changes));
}

public sealed class EditorFallback
{
    public bool? InsertSpaces { get; set; }

    public int? TabSize { get; set; }

    public int? MaxLineLength { get; set; }

    public string? LineEnding { get; set; }

    public bool? InsertFinalNewline { get; set; }

    public bool? TrimTrailingWhitespace { get; set; }

    public string? Charset { get; set; }
}

public sealed class FormattingRequest
{
    public FormattingRequest(
        FormattingLanguage language,
        string source,
        IReadOnlyDictionary<string, string>? resolvedEditorConfig = null,
        EditorFallback? editorFallback = null)
    {
        Language = language;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        ResolvedEditorConfig = resolvedEditorConfig ?? new Dictionary<string, string>();
        EditorFallback = editorFallback ?? new EditorFallback();
    }

    public FormattingLanguage Language { get; }

    public string Source { get; }

    public IReadOnlyDictionary<string, string> ResolvedEditorConfig { get; }
    public EditorFallback EditorFallback { get; }
}
