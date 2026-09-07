using CSharpWorkbench.Formatting.Core.Contracts;
using Xunit;

namespace CSharpWorkbench.Formatting.Core.Tests;

public sealed class RazorMarkupLayoutTests
{
    [Fact]
    public async Task LimitsBlankLinesBetweenTags()
    {
        const string source = "<div></div>\n\n\n\n<span></span>";

        Assert.Equal("<div></div>\n\n<span></span>", await FormatAsync(source));
        Assert.Equal("<div></div>\n<span></span>", await FormatAsync(source, new Dictionary<string, string>
        {
            ["html_max_blank_lines_between_tags"] = "0",
        }));
    }

    [Fact]
    public async Task ChildElementRuleCanBeDisabledWithoutFlatteningExistingMultilineLayout()
    {
        var properties = new Dictionary<string, string>
        {
            ["html_linebreaks_inside_tags_for_elements_with_child_elements"] = "false",
        };

        Assert.Equal(
            "<div><span>Value</span><Widget /></div>",
            await FormatAsync("<div><span>Value</span><Widget /></div>", properties));
        Assert.Equal(
            "<div>\n    <span>Value</span>\n</div>",
            await FormatAsync("<div>\n<span>Value</span>\n</div>", properties));
    }

    [Fact]
    public async Task MultilineElementInsideRuleCanBeDisabledWithoutRemovingExistingBreaks()
    {
        const string source = "<div\n    class=\"root\"\n>\nText\n</div>";
        var formatted = await FormatAsync(source, new Dictionary<string, string>
        {
            ["html_attribute_style"] = "do_not_touch",
            ["html_linebreaks_inside_tags_for_multiline_elements"] = "false",
            ["html_linebreaks_inside_tags_for_elements_with_child_elements"] = "false",
        });

        Assert.Contains("<div\n    class=\"root\"\n>", formatted, StringComparison.Ordinal);
        Assert.Contains("\nText\n", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllLineBreakRulesDisabledPreservesInlineStructure()
    {
        var properties = AllLineBreakRules(false);
        const string source = "<div><span A = \"1\">Value</span><Widget/></div>";

        Assert.Equal(
            "<div><span A=\"1\">Value</span><Widget /></div>",
            await FormatAsync(source, properties));
    }

    [Fact]
    public async Task LineBreakBeforeAllElementsSeparatesElementFromText()
    {
        Assert.Equal(
            "Text\n<span>Value</span>",
            await FormatAsync("Text<span>Value</span>", new Dictionary<string, string>
            {
                ["html_linebreak_before_all_elements"] = "true",
            }));
    }

    [Fact]
    public async Task LineBreakBeforeMultilineElementUsesFormattedOpeningTag()
    {
        const string source = "<div>Prefix <Widget A=\"1\" B=\"2\" /></div>";
        var formatted = await FormatAsync(source, new Dictionary<string, string>
        {
            ["html_attribute_style"] = "on_different_lines",
            ["html_linebreak_before_multiline_elements"] = "true",
        });

        Assert.Equal(
            "<div>Prefix\n    <Widget\n        A=\"1\"\n        B=\"2\" /></div>",
            formatted);
    }

    [Fact]
    public async Task DirectTextWithChildIsNotSplitByChildElementRule()
    {
        const string source = "<div>Prefix  <span A = \"1\">Value</span> suffix</div>";

        Assert.Equal(
            "<div>Prefix  <span A=\"1\">Value</span> suffix</div>",
            await FormatAsync(source));
    }

    [Fact]
    public async Task DefaultPreserveElementsKeepInnerSourceUntouched()
    {
        const string source = "<div><pre>  first   value\n    <span A = \"1\">second</span> </pre><textarea> third   value </textarea></div>";
        var formatted = await FormatAsync(source);

        Assert.Contains("<pre>  first   value\n    <span A = \"1\">second</span> </pre>", formatted, StringComparison.Ordinal);
        Assert.Contains("<textarea> third   value </textarea>", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomNoIndentStillFormatsNestedTagsWhilePreserveDoesNot()
    {
        const string source = "<custom>\n  <span A = \"1\">Value</span>\n</custom>";
        var noIndent = await FormatAsync(source, new Dictionary<string, string>
        {
            ["html_no_indent_inside_elements"] = "custom",
            ["html_preserve_spaces_inside_tags"] = string.Empty,
        });
        var preserve = await FormatAsync(source, new Dictionary<string, string>
        {
            ["html_preserve_spaces_inside_tags"] = "custom",
        });

        Assert.Contains("<span A=\"1\">", noIndent, StringComparison.Ordinal);
        Assert.Contains("<span A = \"1\">", preserve, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LayoutRulesRemainIdempotent()
    {
        const string source = "<div><section><span>Value</span><Widget A = \"1\" /></section></div>";
        var once = await FormatAsync(source);
        var twice = await FormatAsync(once);

        Assert.Equal(once, twice);
    }

    private static Dictionary<string, string> AllLineBreakRules(bool value)
    {
        var text = value ? "true" : "false";
        return new Dictionary<string, string>
        {
            ["html_linebreak_before_all_elements"] = text,
            ["html_linebreak_before_multiline_elements"] = text,
            ["html_linebreaks_inside_tags_for_multiline_elements"] = text,
            ["html_linebreaks_inside_tags_for_elements_with_child_elements"] = text,
        };
    }

    private static async Task<string> FormatAsync(
        string source,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        var result = await new FormattingEngine().FormatAsync(
            new FormattingRequest(FormattingLanguage.Razor, source, properties));
        return result.Changes.Count == 0 ? source : Assert.Single(result.Changes).NewText;
    }
}
