using CSharpWorkbench.Formatting.Core.Contracts;

namespace CSharpWorkbench.Formatting.Core.Razor;

internal static class RazorFormattingOptionsResolver
{
    public static RazorFormattingOptions Resolve(
        IReadOnlyDictionary<string, string> properties,
        EditorFallback? fallback = null)
    {
        if (properties is null)
            throw new ArgumentNullException(nameof(properties));

        fallback ??= new EditorFallback();
        var options = new RazorFormattingOptions();
        var tabWidth = ParsePositiveInteger(Get(properties, "tab_width"))
            ?? ParsePositiveInteger(Get(properties, "indent_size"))
            ?? Positive(fallback.TabSize)
            ?? options.TabWidth;

        options.UseTabs = ParseIndentStyle(Get(properties, "indent_style"))
            ?? (fallback.InsertSpaces == false);
        options.TabWidth = tabWidth;
        options.IndentSize = ParsePositiveInteger(Get(properties, "indent_size"))
            ?? (Is(Get(properties, "indent_size"), "tab") ? tabWidth : Positive(fallback.TabSize))
            ?? options.IndentSize;
        options.LineEnding = ResolveLineEnding(Get(properties, "end_of_line"), fallback.LineEnding);
        options.InsertFinalNewline = ParseBoolean(Get(properties, "insert_final_newline"))
            ?? fallback.InsertFinalNewline
            ?? options.InsertFinalNewline;
        options.TrimTrailingWhitespace = ParseBoolean(Get(properties, "trim_trailing_whitespace"))
            ?? fallback.TrimTrailingWhitespace
            ?? options.TrimTrailingWhitespace;
        return options;
    }

    private static string? Get(IReadOnlyDictionary<string, string> properties, string key)
    {
        return properties.TryGetValue(key, out var value) ? value : null;
    }

    private static bool Is(string? value, string expected)
    {
        return string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool? ParseIndentStyle(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "tab" => true,
            "space" => false,
            _ => null,
        };
    }

    private static bool? ParseBoolean(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        };
    }

    private static int? ParsePositiveInteger(string? value)
    {
        return int.TryParse(value, out var result) && result > 0 ? result : null;
    }

    private static int? Positive(int? value)
    {
        return value > 0 ? value : null;
    }

    private static string ResolveLineEnding(string? value, string? fallback)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "crlf" => "\r\n",
            "lf" => "\n",
            _ when fallback == "\r\n" => "\r\n",
            _ => "\n",
        };
    }
}
