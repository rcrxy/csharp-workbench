using CSharpWorkbench.Formatting.Core.Contracts;
using Xunit;

namespace CSharpWorkbench.Formatting.Core.Tests;

public sealed class RazorFormatterTests
{
    [Theory]
    [InlineData(FormattingLanguage.Razor)]
    [InlineData(FormattingLanguage.Cshtml)]
    public async Task FormattingEngineDispatchesRazorLanguages(FormattingLanguage language)
    {
        var formatted = await FormatAsync("<div><span>Value</span></div>", language);

        Assert.Equal("<div>\n    <span>Value</span>\n</div>", formatted);
    }

    [Theory]
    [InlineData(FormattingLanguage.Razor)]
    [InlineData(FormattingLanguage.Cshtml)]
    public async Task FormattingEngineDispatchesRazorRangeForInlineExpression(FormattingLanguage language)
    {
        const string source = "<span>@(left+right)</span>";
        const string selected = "left+right";
        var start = source.IndexOf(selected, StringComparison.Ordinal);
        var result = await new FormattingEngine().FormatRangeAsync(
            language,
            source,
            new FormattingTextSpan(start, selected.Length));
        var change = Assert.Single(result.Changes);

        Assert.Equal(start, change.Span.Start);
        Assert.Equal(selected.Length, change.Span.Length);
        Assert.Equal("left + right", change.NewText);
    }

    [Fact]
    public async Task FormatsOnlySelectedMethodInsideCodeBlock()
    {
        const string source = "@code {\nint first=1;\nint second=2;\nvoid Run(){Call(first,second);}\n}";
        const string selected = "void Run(){Call(first,second);}";
        var start = source.IndexOf(selected, StringComparison.Ordinal);
        var result = await new FormattingEngine().FormatRangeAsync(
            FormattingLanguage.Razor,
            source,
            new FormattingTextSpan(start, selected.Length),
            new Dictionary<string, string>
            {
                ["csharp_preserve_single_line_blocks"] = "false",
                ["csharp_preserve_single_line_statements"] = "false",
            });
        var formatted = ApplyChanges(source, result.Changes);

        Assert.Contains("int first=1;", formatted, StringComparison.Ordinal);
        Assert.Contains("int second=2;", formatted, StringComparison.Ordinal);
        Assert.Contains("void Run()\n{\n    Call(first, second);\n}", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpWorkbenchSnippet", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatsNestedComponentsAndRemainsIdempotent()
    {
        const string source = "<Parent><Child>Value</Child><MyComponent /></Parent>";
        const string expected = "<Parent>\n    <Child>Value</Child>\n    <MyComponent />\n</Parent>";

        var once = await FormatAsync(source);
        var twice = await FormatAsync(once);

        Assert.Equal(expected, once);
        Assert.Equal(expected, twice);
    }

    [Fact]
    public async Task AppliesDefaultMarkupRulesToMultilineTag()
    {
        var source = string.Join("\n", new[]
        {
            "<div>",
            "<RadzenDataGrid AllowFiltering = \"true\"",
            "                Data=\"@items\">",
            "</RadzenDataGrid>",
            "</div>",
        });

        var formatted = await FormatAsync(source, properties: new Dictionary<string, string>
        {
            ["indent_size"] = "2",
        });

        Assert.Equal(string.Join("\n", new[]
        {
            "<div>",
            "  <RadzenDataGrid AllowFiltering=\"true\" Data=\"@items\">",
            "  </RadzenDataGrid>",
            "</div>",
        }), formatted);
    }

    [Fact]
    public async Task PreservesDirectivesAndFormatsFollowingMarkup()
    {
        const string source = "@page \"/demo\"\n@using Demo.Models\n@inject IService Service\n<div><span>Value</span></div>";
        const string expected = "@page \"/demo\"\n@using Demo.Models\n@inject IService Service\n<div>\n    <span>Value</span>\n</div>";

        Assert.Equal(expected, await FormatAsync(source));
    }

    [Fact]
    public async Task IndentsMarkupInsideRazorControlAndFormatsHeader()
    {
        var source = string.Join("\n", new[]
        {
            "<div>",
            "@if(enabled)",
            "{",
            "<span>Value</span>",
            "}",
            "</div>",
        });

        var formatted = await FormatAsync(source);

        Assert.Equal(string.Join("\n", new[]
        {
            "<div>",
            "    @if (enabled)",
            "    {",
            "        <span>Value</span>",
            "    }",
            "</div>",
        }), formatted);
    }

    [Fact]
    public async Task PreservesCSharpLinesInsideSwitchWhileFormattingMarkup()
    {
        const string source = "@switch (value)\n{\n  case 1:\n<span>One</span>\n    break;\n}";
        var formatted = await FormatAsync(source);

        Assert.Contains("  case 1:", formatted, StringComparison.Ordinal);
        Assert.Contains("    <span>One</span>", formatted, StringComparison.Ordinal);
        Assert.Contains("    break;", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreservesMixedTextContentWithoutTrimmingOrCollapsing()
    {
        const string source = "<div>Prefix  <span>Value</span> suffix</div>";

        Assert.Equal(source, await FormatAsync(source));
    }

    [Theory]
    [InlineData("@code")]
    [InlineData("@functions")]
    public async Task FormatsNamedCodeBlocksWithoutScanningFakeTags(string keyword)
    {
        var source = keyword + " {\nList<string> values = new();\nvar text = \"<div>{ value }</div>\";\nvar raw = \"\"\" </span> { } \"\"\";\n}";
        var formatted = await FormatAsync(source);

        Assert.Contains("    List<string> values = new();", formatted, StringComparison.Ordinal);
        Assert.Contains("\"<div>{ value }</div>\"", formatted, StringComparison.Ordinal);
        Assert.Contains("\"\"\" </span> { } \"\"\"", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IgnoresTagsInsideCommentsAndProtectedElements()
    {
        var source = string.Join("\n", new[]
        {
            "<div>",
            "@* <span><Fake /></span> *@",
            "<!-- <section></section> -->",
            "<script>",
            "if (a < b) { html = \"<span>\"; }",
            "</script>",
            "<style>",
            ".item > span { color: red; }",
            "</style>",
            "</div>",
        });

        var formatted = await FormatAsync(source);

        Assert.Contains("if (a < b) { html = \"<span>\"; }", formatted, StringComparison.Ordinal);
        Assert.Contains(".item > span { color: red; }", formatted, StringComparison.Ordinal);
        Assert.EndsWith("</div>", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandlesNestedQuotesInRazorAttributeExpression()
    {
        const string source = "<div><RadzenIcon Icon=\"@(enabled ? \"check_circle\" : \"cancel\")\" /></div>";
        const string expected = "<div>\n    <RadzenIcon Icon=\"@(enabled ? \"check_circle\" : \"cancel\")\" />\n</div>";

        Assert.Equal(expected, await FormatAsync(source));
    }

    [Theory]
    [InlineData("<div>\n<span>\n</div>")]
    [InlineData("@if (enabled)\n{\n<div>")]
    [InlineData("@code {\nvar value = 1;")]
    public async Task UnreliableDocumentsSkipStructuralFormatting(string source)
    {
        Assert.Equal(source, await FormatAsync(source));
    }

    [Fact]
    public async Task AppliesGenericDocumentOptions()
    {
        const string source = "<div>  \r\n<span>Value</span>\t\r\n</div>\r\n";
        var formatted = await FormatAsync(source, properties: new Dictionary<string, string>
        {
            ["indent_style"] = "tab",
            ["end_of_line"] = "crlf",
            ["trim_trailing_whitespace"] = "true",
            ["insert_final_newline"] = "true",
        });

        Assert.Equal("<div>\r\n\t<span>Value</span>\r\n</div>\r\n", formatted);
    }

    [Fact]
    public async Task PreservesExistingBom()
    {
        const string source = "\uFEFF<div><span>Value</span></div>";

        var formatted = await FormatAsync(source);

        Assert.Equal("\uFEFF<div>\n    <span>Value</span>\n</div>", formatted);
    }

    private static async Task<string> FormatAsync(
        string source,
        FormattingLanguage language = FormattingLanguage.Razor,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        var result = await new FormattingEngine().FormatAsync(
            new FormattingRequest(language, source, properties));
        if (result.Changes.Count == 0)
            return source;

        var change = Assert.Single(result.Changes);
        Assert.Equal(0, change.Span.Start);
        Assert.Equal(source.Length, change.Span.Length);
        return change.NewText;
    }

    private static string ApplyChanges(string source, IReadOnlyList<FormattingTextChange> changes)
    {
        foreach (var change in changes.OrderByDescending(change => change.Span.Start))
        {
            source = source.Remove(change.Span.Start, change.Span.Length)
                .Insert(change.Span.Start, change.NewText);
        }

        return source;
    }
}
