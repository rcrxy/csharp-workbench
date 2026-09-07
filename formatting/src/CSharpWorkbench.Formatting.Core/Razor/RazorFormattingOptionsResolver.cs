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
        var indentStyle = GetHtml(properties, "html_indent_style") ?? Get(properties, "indent_style");
        var indentSizeValue = GetHtml(properties, "html_indent_size") ?? Get(properties, "indent_size");
        var tabWidthValue = GetHtml(properties, "html_tab_width") ?? Get(properties, "tab_width");
        var tabWidth = ParsePositiveInteger(tabWidthValue)
            ?? ParsePositiveInteger(indentSizeValue)
            ?? Positive(fallback.TabSize)
            ?? options.TabWidth;

        options.UseTabs = ParseIndentStyle(indentStyle)
            ?? (fallback.InsertSpaces == false);
        options.TabWidth = tabWidth;
        options.IndentSize = ParsePositiveInteger(indentSizeValue)
            ?? (Is(indentSizeValue, "tab") ? tabWidth : Positive(fallback.TabSize))
            ?? options.IndentSize;
        options.MaxLineLength = ResolveMaxLineLength(Get(properties, "max_line_length"), fallback.MaxLineLength);
        options.LineEnding = ResolveLineEnding(Get(properties, "end_of_line"), fallback.LineEnding);
        options.InsertFinalNewline = ParseBoolean(Get(properties, "insert_final_newline"))
            ?? fallback.InsertFinalNewline
            ?? options.InsertFinalNewline;
        options.TrimTrailingWhitespace = ParseBoolean(Get(properties, "trim_trailing_whitespace"))
            ?? fallback.TrimTrailingWhitespace
            ?? options.TrimTrailingWhitespace;

        var markup = options.Markup;
        markup.SpacesAroundAttributeEquals = ResolveHtmlBoolean(
            properties,
            "html_spaces_around_eq_in_attribute",
            markup.SpacesAroundAttributeEquals);
        markup.SpaceAfterLastAttribute = ResolveHtmlBoolean(
            properties,
            "html_space_after_last_attribute",
            markup.SpaceAfterLastAttribute);
        markup.SpaceBeforeSelfClosing = ResolveHtmlBoolean(
            properties,
            "html_space_before_self_closing",
            markup.SpaceBeforeSelfClosing);
        markup.AttributeStyle = ResolveHtmlEnum(
            properties,
            "html_attribute_style",
            markup.AttributeStyle,
            ("on_single_line", RazorAttributeStyle.OnSingleLine),
            ("first_attribute_on_single_line", RazorAttributeStyle.FirstAttributeOnSingleLine),
            ("on_different_lines", RazorAttributeStyle.OnDifferentLines),
            ("do_not_touch", RazorAttributeStyle.DoNotTouch));
        markup.AttributeWrap = ResolveAttributeWrap(properties);
        markup.AttributeIndent = ResolveHtmlEnum(
            properties,
            "html_attribute_indent",
            markup.AttributeIndent,
            ("single_indent", RazorAttributeIndent.SingleIndent),
            ("double_indent", RazorAttributeIndent.DoubleIndent),
            ("align_by_first_attribute", RazorAttributeIndent.AlignByFirstAttribute));
        markup.MaxBlankLinesBetweenTags = ParseNonNegativeInteger(
            GetHtml(properties, "html_max_blank_lines_between_tags"))
            ?? markup.MaxBlankLinesBetweenTags;
        markup.LineBreakBeforeAllElements = ResolveHtmlBoolean(
            properties,
            "html_linebreak_before_all_elements",
            markup.LineBreakBeforeAllElements);
        markup.LineBreakBeforeMultilineElements = ResolveHtmlBoolean(
            properties,
            "html_linebreak_before_multiline_elements",
            markup.LineBreakBeforeMultilineElements);
        markup.LineBreaksInsideMultilineElements = ResolveHtmlBoolean(
            properties,
            "html_linebreaks_inside_tags_for_multiline_elements",
            markup.LineBreaksInsideMultilineElements);
        markup.LineBreaksInsideElementsWithChildElements = ResolveHtmlBoolean(
            properties,
            "html_linebreaks_inside_tags_for_elements_with_child_elements",
            markup.LineBreaksInsideElementsWithChildElements);
        markup.NoIndentInsideElements = ResolveElementSet(
            GetHtml(properties, "html_no_indent_inside_elements"),
            markup.NoIndentInsideElements);
        markup.PreserveSpacesInsideTags = ResolveElementSet(
            GetHtml(properties, "html_preserve_spaces_inside_tags"),
            markup.PreserveSpacesInsideTags);
        markup.ExtraSpaces = ResolveHtmlEnum(
            properties,
            "html_extra_spaces",
            markup.ExtraSpaces,
            ("remove_all", RazorExtraSpaces.RemoveAll),
            ("leave_tabs", RazorExtraSpaces.LeaveTabs),
            ("leave_multiple", RazorExtraSpaces.LeaveMultiple),
            ("leave_all", RazorExtraSpaces.LeaveAll));
        return options;
    }

    private static string? Get(IReadOnlyDictionary<string, string> properties, string key)
    {
        return properties.TryGetValue(key, out var value) ? value : null;
    }

    private static string? GetHtml(IReadOnlyDictionary<string, string> properties, string key)
    {
        return Get(properties, key) ?? Get(properties, "resharper_" + key);
    }

    private static bool ResolveHtmlBoolean(
        IReadOnlyDictionary<string, string> properties,
        string key,
        bool defaultValue)
    {
        return ParseBoolean(GetHtml(properties, key)) ?? defaultValue;
    }

    private static T ResolveHtmlEnum<T>(
        IReadOnlyDictionary<string, string> properties,
        string key,
        T defaultValue,
        params (string Name, T Value)[] values)
    {
        var raw = GetHtml(properties, key)?.Trim();
        foreach (var value in values)
        {
            if (string.Equals(raw, value.Name, StringComparison.OrdinalIgnoreCase))
                return value.Value;
        }

        return defaultValue;
    }

    private static RazorAttributeWrapPolicy ResolveAttributeWrap(IReadOnlyDictionary<string, string> properties)
    {
        var raw = Get(properties, "html_attribute_wrap") ?? Get(properties, "ij_html_attribute_wrap");
        return raw?.Trim().ToLowerInvariant() switch
        {
            "normal" => RazorAttributeWrapPolicy.Normal,
            "on_every_item" => RazorAttributeWrapPolicy.OnEveryItem,
            "split_into_lines" => RazorAttributeWrapPolicy.SplitIntoLines,
            _ => RazorAttributeWrapPolicy.Off,
        };
    }

    private static HashSet<string> ResolveElementSet(string? value, HashSet<string> defaults)
    {
        if (value is null)
            return defaults;

        return new HashSet<string>(
            value.Split(',').Select(item => item.Trim()).Where(item => item.Length > 0),
            StringComparer.OrdinalIgnoreCase);
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

    private static int? ParseNonNegativeInteger(string? value)
    {
        return int.TryParse(value, out var result) && result >= 0 ? result : null;
    }

    private static int? Positive(int? value)
    {
        return value > 0 ? value : null;
    }

    private static int? ResolveMaxLineLength(string? value, int? fallback)
    {
        return Is(value, "off") ? null : ParsePositiveInteger(value) ?? Positive(fallback) ?? 120;
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
