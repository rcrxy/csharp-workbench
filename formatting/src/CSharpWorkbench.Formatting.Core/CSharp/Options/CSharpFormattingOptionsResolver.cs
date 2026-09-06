using CSharpWorkbench.Formatting.Core.Contracts;

namespace CSharpWorkbench.Formatting.Core.CSharp.Options;

public static class CSharpFormattingOptionsResolver
{
    public static CSharpFormattingOptions Resolve(
        IReadOnlyDictionary<string, string> properties,
        EditorFallback? fallback = null)
    {
        if (properties is null)
            throw new ArgumentNullException(nameof(properties));

        fallback ??= new EditorFallback();

        var options = new CSharpFormattingOptions();
        var insertSpaces = ParseIndentStyle(Get(properties, "indent_style")) ?? fallback.InsertSpaces;
        var tabSize = ParsePositiveInteger(Get(properties, "tab_width"))
        ?? ParsePositiveInteger(Get(properties, "indent_size"))
        ?? Positive(fallback.TabSize)
        ?? options.Indentation.TabWidth;
        var indentSize = ParsePositiveInteger(Get(properties, "indent_size"))
        ?? (Is(Get(properties, "indent_size"), "tab") ? tabSize : Positive(fallback.TabSize))
        ?? options.Indentation.Size;

        options.Indentation.Style = insertSpaces == false ? CSharpIndentationStyle.Tab : CSharpIndentationStyle.Space;
        options.Indentation.Size = indentSize;
        options.Indentation.TabWidth = tabSize;
        options.MaxLineLength = ResolveMaxLineLength(Get(properties, "max_line_length"), fallback.MaxLineLength);
        options.LineEnding = ResolveLineEnding(Get(properties, "end_of_line"), fallback.LineEnding);
        options.InsertFinalNewline = ParseBoolean(Get(properties, "insert_final_newline"))
        ?? fallback.InsertFinalNewline
        ?? options.InsertFinalNewline;
        options.TrimTrailingWhitespace = ParseBoolean(Get(properties, "trim_trailing_whitespace"))
        ?? fallback.TrimTrailingWhitespace
        ?? options.TrimTrailingWhitespace;
        options.Charset = ResolveCharset(Get(properties, "charset"), fallback.Charset);

        options.CSharpIndentation.IndentBlockContents = ResolveBoolean(
            properties,
            "csharp_indent_block_contents",
            options.CSharpIndentation.IndentBlockContents);
        options.CSharpIndentation.IndentBraces = ResolveBoolean(
            properties,
            "csharp_indent_braces",
            options.CSharpIndentation.IndentBraces);
        options.CSharpIndentation.IndentCaseContents = ResolveBoolean(
            properties,
            "csharp_indent_case_contents",
            options.CSharpIndentation.IndentCaseContents);
        options.CSharpIndentation.IndentSwitchLabels = ResolveBoolean(
            properties,
            "csharp_indent_switch_labels",
            options.CSharpIndentation.IndentSwitchLabels);
        options.CSharpIndentation.IndentCaseContentsWhenBlock = ResolveBoolean(
            properties,
            "csharp_indent_case_contents_when_block",
            options.CSharpIndentation.IndentCaseContentsWhenBlock);
        options.CSharpIndentation.IndentLabels = ResolveLabelIndentation(Get(properties, "csharp_indent_labels"));

        ResolveNewLineOptions(properties, options.CSharpNewLines);
        ResolveSpacingOptions(properties, options.CSharpSpacing);
        options.CSharpWrapping.PreserveSingleLineStatements = ResolveBoolean(
            properties,
            "csharp_preserve_single_line_statements",
            options.CSharpWrapping.PreserveSingleLineStatements);
        options.CSharpWrapping.PreserveSingleLineBlocks = ResolveBoolean(
            properties,
            "csharp_preserve_single_line_blocks",
            options.CSharpWrapping.PreserveSingleLineBlocks);

        return options;
    }

    private static void ResolveNewLineOptions(
        IReadOnlyDictionary<string, string> properties,
        CSharpNewLineOptions options)
    {
        var openBraceValue = Get(properties, "csharp_new_line_before_open_brace");
        if (Is(openBraceValue, "none"))
        {
            options.BeforeOpenBrace = CSharpOpenBraceMode.None;
        }
        else if (!string.IsNullOrWhiteSpace(openBraceValue) && !Is(openBraceValue, "all"))
        {
            var contexts = ParseOpenBraceContexts(openBraceValue!);
            if (contexts.Count > 0)
            {
                options.BeforeOpenBrace = CSharpOpenBraceMode.Selected;
                options.OpenBraceContexts = contexts;
            }
        }

        options.BeforeElse = ResolveBoolean(properties, "csharp_new_line_before_else", options.BeforeElse);
        options.BeforeCatch = ResolveBoolean(properties, "csharp_new_line_before_catch", options.BeforeCatch);
        options.BeforeFinally = ResolveBoolean(properties, "csharp_new_line_before_finally", options.BeforeFinally);
        options.BeforeMembersInObjectInitializers = ResolveBoolean(
            properties,
            "csharp_new_line_before_members_in_object_initializers",
            options.BeforeMembersInObjectInitializers);
        options.BeforeMembersInAnonymousTypes = ResolveBoolean(
            properties,
            "csharp_new_line_before_members_in_anonymous_types",
            options.BeforeMembersInAnonymousTypes);
        options.BetweenQueryExpressionClauses = ResolveBoolean(
            properties,
            "csharp_new_line_between_query_expression_clauses",
            options.BetweenQueryExpressionClauses);
    }

    private static void ResolveSpacingOptions(
        IReadOnlyDictionary<string, string> properties,
        CSharpSpacingOptions options)
    {
        options.AfterControlFlowKeyword = ResolveBoolean(
            properties,
            "csharp_space_after_keywords_in_control_flow_statements",
            options.AfterControlFlowKeyword);
        options.AroundBinaryOperators = ResolveBinaryOperatorSpacing(Get(properties, "csharp_space_around_binary_operators"));
        options.AfterComma = ResolveBoolean(properties, "csharp_space_after_comma", options.AfterComma);
        options.BeforeComma = ResolveBoolean(properties, "csharp_space_before_comma", options.BeforeComma);
        options.AfterForSemicolon = ResolveBoolean(
            properties,
            "csharp_space_after_semicolon_in_for_statement",
            options.AfterForSemicolon);
        options.BeforeForSemicolon = ResolveBoolean(
            properties,
            "csharp_space_before_semicolon_in_for_statement",
            options.BeforeForSemicolon);
        options.AfterCast = ResolveBoolean(properties, "csharp_space_after_cast", options.AfterCast);
        options.BeforeInheritanceColon = ResolveBoolean(
            properties,
            "csharp_space_before_colon_in_inheritance_clause",
            options.BeforeInheritanceColon);
        options.AfterInheritanceColon = ResolveBoolean(
            properties,
            "csharp_space_after_colon_in_inheritance_clause",
            options.AfterInheritanceColon);
        options.BetweenMethodCallNameAndOpeningParenthesis = ResolveBoolean(
            properties,
            "csharp_space_between_method_call_name_and_opening_parenthesis",
            options.BetweenMethodCallNameAndOpeningParenthesis);
        options.BetweenMethodCallParameterListParentheses = ResolveBoolean(
            properties,
            "csharp_space_between_method_call_parameter_list_parentheses",
            options.BetweenMethodCallParameterListParentheses);
        options.BetweenMethodCallEmptyParameterListParentheses = ResolveBoolean(
            properties,
            "csharp_space_between_method_call_empty_parameter_list_parentheses",
            options.BetweenMethodCallEmptyParameterListParentheses);
        options.BetweenMethodDeclarationNameAndOpeningParenthesis = ResolveBoolean(
            properties,
            "csharp_space_between_method_declaration_name_and_open_parenthesis",
            options.BetweenMethodDeclarationNameAndOpeningParenthesis);
        options.BetweenMethodDeclarationParameterListParentheses = ResolveBoolean(
            properties,
            "csharp_space_between_method_declaration_parameter_list_parentheses",
            options.BetweenMethodDeclarationParameterListParentheses);
        options.BetweenMethodDeclarationEmptyParameterListParentheses = ResolveBoolean(
            properties,
            "csharp_space_between_method_declaration_empty_parameter_list_parentheses",
            options.BetweenMethodDeclarationEmptyParameterListParentheses);
        options.BetweenParentheses = ParseParenthesisContexts(Get(properties, "csharp_space_between_parentheses"));
    }

    private static IReadOnlyList<CSharpOpenBraceContext> ParseOpenBraceContexts(string value)
    {
        var contexts = new List<CSharpOpenBraceContext>();
        foreach (var item in Split(value))
        {
            if (TryParseOpenBraceContext(item, out var context))
            {
                contexts.Add(context);
            }
        }

        return contexts;
    }

    private static bool TryParseOpenBraceContext(string value, out CSharpOpenBraceContext context)
    {
        switch (value)
        {
            case "accessors": context = CSharpOpenBraceContext.Accessors; return true;
            case "anonymous_methods": context = CSharpOpenBraceContext.AnonymousMethods; return true;
            case "anonymous_types": context = CSharpOpenBraceContext.AnonymousTypes; return true;
            case "control_blocks": context = CSharpOpenBraceContext.ControlBlocks; return true;
            case "events": context = CSharpOpenBraceContext.Events; return true;
            case "indexers": context = CSharpOpenBraceContext.Indexers; return true;
            case "lambdas": context = CSharpOpenBraceContext.Lambdas; return true;
            case "local_functions": context = CSharpOpenBraceContext.LocalFunctions; return true;
            case "methods": context = CSharpOpenBraceContext.Methods; return true;
            case "object_collection_array_initializers": context = CSharpOpenBraceContext.ObjectCollectionArrayInitializers; return true;
            case "properties": context = CSharpOpenBraceContext.Properties; return true;
            case "types": context = CSharpOpenBraceContext.Types; return true;
            default: context = default; return false;
        }
    }

    private static IReadOnlyList<CSharpParenthesisSpacingContext> ParseParenthesisContexts(string? value)
    {
        if (value is null)
        {
            return Array.Empty<CSharpParenthesisSpacingContext>();
        }

        var contexts = new List<CSharpParenthesisSpacingContext>();
        foreach (var item in Split(value))
        {
            switch (item)
            {
                case "control_flow_statements": contexts.Add(CSharpParenthesisSpacingContext.ControlFlowStatements); break;
                case "expressions": contexts.Add(CSharpParenthesisSpacingContext.Expressions); break;
                case "type_casts": contexts.Add(CSharpParenthesisSpacingContext.TypeCasts); break;
            }
        }

        return contexts;
    }

    private static CSharpLabelIndentation ResolveLabelIndentation(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "flush_left" => CSharpLabelIndentation.FlushLeft,
            "no_change" => CSharpLabelIndentation.NoChange,
            _ => CSharpLabelIndentation.OneLessThanCurrent,
        };
    }

    private static CSharpBinaryOperatorSpacing ResolveBinaryOperatorSpacing(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "none" => CSharpBinaryOperatorSpacing.None,
            "ignore" => CSharpBinaryOperatorSpacing.Ignore,
            _ => CSharpBinaryOperatorSpacing.BeforeAndAfter,
        };
    }

    private static int? ResolveMaxLineLength(string? value, int? fallback)
    {
        return Is(value, "off") ? null : ParsePositiveInteger(value) ?? Positive(fallback) ?? 80;
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

    private static CSharpCharset ResolveCharset(string? value, string? fallback)
    {
        return (value ?? fallback)?.Trim().ToLowerInvariant() switch
        {
            "utf-8-bom" => CSharpCharset.Utf8Bom,
            "utf-16be" => CSharpCharset.Utf16BigEndian,
            "utf-16le" => CSharpCharset.Utf16LittleEndian,
            "latin1" => CSharpCharset.Latin1,
            _ => CSharpCharset.Utf8,
        };
    }

    private static bool ResolveBoolean(IReadOnlyDictionary<string, string> properties, string key, bool defaultValue)
    {
        return ParseBoolean(Get(properties, key)) ?? defaultValue;
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

    private static bool? ParseIndentStyle(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "space" => true,
            "tab" => false,
            _ => null,
        };
    }

    private static int? ParsePositiveInteger(string? value)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;
    }

    private static int? Positive(int? value)
    {
        return value > 0 ? value : null;
    }

    private static string? Get(IReadOnlyDictionary<string, string> properties, string key)
    {
        return properties.TryGetValue(key, out var value) ? value : null;
    }

    private static bool Is(string? value, string expected)
    {
        return string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Split(string value)
    {
        return value.Split(',').Select(item => item.Trim().ToLowerInvariant()).Where(item => item.Length > 0);
    }
}
