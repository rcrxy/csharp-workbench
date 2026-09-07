namespace CSharpWorkbench.Formatting.Core.Razor;

internal readonly struct RazorSourceEdit(RazorSourceSpan span, string newText)
{
    public RazorSourceSpan Span { get; } = span;

    public string NewText { get; } = newText ?? throw new ArgumentNullException(nameof(newText));
}

internal sealed class RazorSourceOverlay(string source, IReadOnlyList<RazorSourceEdit> edits)
{
    public string GetText(RazorSourceSpan span)
    {
        return GetText(span.Start, span.Length);
    }

    public string GetText(int start, int length)
    {
        var end = checked(start + length);
        var contained = edits.Where(edit => edit.Span.Start >= start && edit.Span.End <= end).ToArray();
        if (edits.Any(edit => edit.Span.Start < end && edit.Span.End > start &&
            (edit.Span.Start < start || edit.Span.End > end)))
        {
            throw new InvalidOperationException("A Razor source edit crosses a requested overlay span.");
        }
        if (contained.Length == 0)
            return source.Substring(start, length);

        var builder = new System.Text.StringBuilder(length + 32);
        var cursor = start;
        foreach (var edit in contained)
        {
            builder.Append(source, cursor, edit.Span.Start - cursor);
            builder.Append(edit.NewText);
            cursor = edit.Span.End;
        }
        builder.Append(source, cursor, end - cursor);
        return builder.ToString();
    }

    public string GetText(
        RazorSourceSpan span,
        IEnumerable<(RazorSourceSpan Span, string NewText)> additionalEdits)
    {
        var combined = edits
            .Where(edit => edit.Span.Start >= span.Start && edit.Span.End <= span.End)
            .Select(edit => (edit.Span, edit.NewText))
            .Concat(additionalEdits)
            .OrderBy(edit => edit.Span.Start)
            .ToArray();
        for (var index = 1; index < combined.Length; index++)
        {
            if (combined[index - 1].Span.End > combined[index].Span.Start)
                throw new InvalidOperationException("Razor overlay edits overlap.");
        }

        var builder = new System.Text.StringBuilder(span.Length + 32);
        var cursor = span.Start;
        foreach (var edit in combined)
        {
            builder.Append(source, cursor, edit.Span.Start - cursor);
            builder.Append(edit.NewText);
            cursor = edit.Span.End;
        }
        builder.Append(source, cursor, span.End - cursor);
        return builder.ToString();
    }
}
