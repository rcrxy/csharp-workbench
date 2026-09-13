using CSharpWorkbench.Formatting.Core.Contracts;
using Xunit;

namespace CSharpWorkbench.Formatting.Core.Tests;

public sealed class RazorMarkupFormatterTests
{
    [Fact]
    public async Task FormatsAttributeEqualsAndClosingDelimiterSpaces()
    {
        Assert.Equal(
            "<Widget A=\"1\" B=\"2\" />",
            await FormatAsync("<Widget A = \"1\" B= \"2\"/>"));
        Assert.Equal(
            "<Widget A = \"1\"/>",
            await FormatAsync("<Widget A=\"1\" />", new Dictionary<string, string>
            {
                ["html_spaces_around_eq_in_attribute"] = "true",
                ["html_space_before_self_closing"] = "false",
            }));
        Assert.Equal(
            "<div class=\"root\" >",
            await FormatAsync("<div class=\"root\"></div>", new Dictionary<string, string>
            {
                ["html_space_after_last_attribute"] = "true",
            }, extractOpeningTag: true));
    }

    [Fact]
    public async Task PreservesBooleanAndUnquotedAttributes()
    {
        Assert.Equal("<input disabled value=test>", await FormatAsync("<input disabled value=test>"));
        Assert.Equal("<Widget/>", await FormatAsync("<Widget />", new Dictionary<string, string>
        {
            ["html_space_before_self_closing"] = "false",
        }));
    }

    [Theory]
    [InlineData("on_single_line", "<Widget A=\"1\" B=\"2\" />")]
    [InlineData("first_attribute_on_single_line", "<Widget A=\"1\"\n    B=\"2\" />")]
    [InlineData("on_different_lines", "<Widget\n    A=\"1\"\n    B=\"2\" />")]
    public async Task SupportsAttributeStyles(string style, string expected)
    {
        var formatted = await FormatAsync("<Widget A=\"1\" B=\"2\" />", new Dictionary<string, string>
        {
            ["html_attribute_style"] = style,
        });

        Assert.Equal(expected, formatted);
    }

    [Fact]
    public async Task DoNotTouchPreservesAttributeLineStructure()
    {
        const string source = "<Widget\n    A = \"1\"\n        B = \"2\"\n/>";
        const string expected = "<Widget\n    A=\"1\"\n        B=\"2\"\n />";

        Assert.Equal(expected, await FormatAsync(source, new Dictionary<string, string>
        {
            ["html_attribute_style"] = "do_not_touch",
            ["html_extra_spaces"] = "leave_all",
        }));
    }

    [Theory]
    [InlineData("single_indent", "<Widget\n    A=\"1\"\n    B=\"2\" />")]
    [InlineData("double_indent", "<Widget\n        A=\"1\"\n        B=\"2\" />")]
    [InlineData("align_by_first_attribute", "<Widget\n        A=\"1\"\n        B=\"2\" />")]
    public async Task SupportsAttributeIndentModes(string indent, string expected)
    {
        var formatted = await FormatAsync("<Widget A=\"1\" B=\"2\" />", new Dictionary<string, string>
        {
            ["html_attribute_style"] = "on_different_lines",
            ["html_attribute_indent"] = indent,
        });

        Assert.Equal(expected, formatted);
    }

    [Fact]
    public async Task NormalWrapUsesStrictVisualWidthThreshold()
    {
        var fits = await FormatAsync("<Widget A=\"1\" B=\"2\" />", new Dictionary<string, string>
        {
            ["html_attribute_wrap"] = "normal",
            ["max_line_length"] = "22",
        });
        var wraps = await FormatAsync("<Widget A=\"1\" B=\"2\" />", new Dictionary<string, string>
        {
            ["html_attribute_wrap"] = "normal",
            ["max_line_length"] = "21",
        });

        Assert.Equal("<Widget A=\"1\" B=\"2\" />", fits);
        Assert.Equal("<Widget\n    A=\"1\" B=\"2\" />", wraps);
    }

    [Fact]
    public async Task NormalWrapAccountsForParentIndentationAndTabs()
    {
        const string source = "<div><Widget A=\"1\" B=\"2\" /></div>";
        var formatted = await FormatAsync(source, new Dictionary<string, string>
        {
            ["html_indent_style"] = "tab",
            ["html_tab_width"] = "4",
            ["html_attribute_wrap"] = "normal",
            ["html_attribute_style"] = "on_different_lines",
            ["max_line_length"] = "25",
        });

        Assert.Equal("<div>\n\t<Widget\n\t\tA=\"1\"\n\t\tB=\"2\" />\n</div>", formatted);
    }

    [Fact]
    public async Task NormalWrapRecoversMultilineTagAndOffDisablesLengthWrapping()
    {
        const string multiline = "<Widget\n    A=\"1\"\n    B=\"2\" />";
        Assert.Equal("<Widget A=\"1\" B=\"2\" />", await FormatAsync(multiline, new Dictionary<string, string>
        {
            ["html_attribute_wrap"] = "normal",
            ["max_line_length"] = "80",
        }));

        const string longTag = "<Widget A=\"a-very-long-value\" B=\"another-long-value\" />";
        Assert.Equal(longTag, await FormatAsync(longTag, new Dictionary<string, string>
        {
            ["html_attribute_wrap"] = "normal",
            ["max_line_length"] = "off",
        }));
    }

    [Theory]
    [InlineData("on_every_item")]
    [InlineData("split_into_lines")]
    public async Task CompatibilityWrapPoliciesDoNotAddLengthBasedWrapping(string policy)
    {
        const string source = "<Widget A=\"1\" B=\"2\" />";

        Assert.Equal(source, await FormatAsync(source, new Dictionary<string, string>
        {
            ["html_attribute_wrap"] = policy,
            ["max_line_length"] = "1",
        }));
        Assert.Equal("<Widget\n    A=\"1\"\n    B=\"2\" />", await FormatAsync(source, new Dictionary<string, string>
        {
            ["html_attribute_wrap"] = policy,
            ["html_attribute_style"] = "on_different_lines",
            ["max_line_length"] = "1",
        }));
    }

    [Fact]
    public async Task SingleLongAttributeIsNeverSplitInternally()
    {
        Assert.Equal(
            "<Widget\n    A=\"a-very-long-value\" />",
            await FormatAsync("<Widget A=\"a-very-long-value\" />", new Dictionary<string, string>
            {
                ["html_attribute_wrap"] = "normal",
                ["max_line_length"] = "5",
            }));
    }

    [Fact]
    public async Task FormatsThreeSpaceRadzenScenarioAndIsIdempotent()
    {
        const string source = "<RadzenDataGrid Data=\"@stores\" AllowFiltering=\"true\" AllowColumnResize=\"true\" AllowSorting=\"true\" PageSize=\"5\" AllowPaging=\"true\">";
        var properties = new Dictionary<string, string>
        {
            ["indent_size"] = "3",
            ["html_attribute_wrap"] = "normal",
            ["html_attribute_style"] = "first_attribute_on_single_line",
            ["html_attribute_indent"] = "align_by_first_attribute",
            ["html_space_after_last_attribute"] = "true",
            ["max_line_length"] = "120",
        };
        var expected = "<RadzenDataGrid Data=\"@stores\"\n" +
            "                AllowFiltering=\"true\"\n" +
            "                AllowColumnResize=\"true\"\n" +
            "                AllowSorting=\"true\"\n" +
            "                PageSize=\"5\"\n" +
            "                AllowPaging=\"true\" >";

        var once = await FormatAsync(source + "</RadzenDataGrid>", properties, extractOpeningTag: true);
        var twice = await FormatAsync(once + "</RadzenDataGrid>", properties, extractOpeningTag: true);

        Assert.Equal(expected, once);
        Assert.Equal(expected, twice);
    }

    [Fact]
    public async Task PreservesComplexRazorExpressionValuesWhileWrapping()
    {
        const string source = "<RadzenIcon Icon=\"@(store.IsEnabled ? \"check_circle\" : \"cancel\")\" Style=\"@(store.IsEnabled ? \"color: var(--rz-success);\" : \"color: var(--rz-danger);\")\" />";
        var properties = new Dictionary<string, string>
        {
            ["indent_size"] = "3",
            ["html_attribute_wrap"] = "normal",
            ["html_attribute_style"] = "on_different_lines",
            ["html_attribute_indent"] = "double_indent",
            ["max_line_length"] = "80",
        };
        var expected = "<RadzenIcon\n" +
            "      Icon=\"@(store.IsEnabled ? \"check_circle\" : \"cancel\")\"\n" +
            "      Style=\"@(store.IsEnabled ? \"color: var(--rz-success);\" : \"color: var(--rz-danger);\")\" />";

        var once = await FormatAsync(source, properties);
        var twice = await FormatAsync(once, properties);

        Assert.Equal(expected, once);
        Assert.Equal(expected, twice);
    }

    [Fact]
    public async Task MovesModernComponentAttributesWithoutFormattingEmbeddedCSharp()
    {
        const string source = "<Widget Click=\"@(() => Save(item))\" Visible=\"@(item is { Enabled: true })\" Value=\"@(new Item { Name = \"A\" })\" @attributes=\"Attributes\" />";
        var formatted = await FormatAsync(source, new Dictionary<string, string>
        {
            ["html_attribute_style"] = "on_different_lines",
        });

        Assert.Contains("Click=\"@(() => Save(item))\"", formatted, StringComparison.Ordinal);
        Assert.Contains("Visible=\"@(item is { Enabled: true })\"", formatted, StringComparison.Ordinal);
        Assert.Contains("Value=\"@(new Item { Name = \"A\" })\"", formatted, StringComparison.Ordinal);
        Assert.Contains("@attributes=\"Attributes\"", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnreliableTagAttributesStayUntouchedWhileOtherTagsFormat()
    {
        const string source = "<div><Widget Value=></Widget><span A = \"1\">After</span></div>";
        var formatted = await FormatAsync(source);

        Assert.Contains("<Widget Value=>", formatted, StringComparison.Ordinal);
        Assert.Contains("<span A=\"1\">After</span>", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsesConfiguredCrLfForAttributeWrappingAndSupportsCshtml()
    {
        var formatted = await FormatAsync("<Widget A=\"1\" B=\"2\" />", new Dictionary<string, string>
        {
            ["html_attribute_style"] = "on_different_lines",
            ["end_of_line"] = "crlf",
        }, FormattingLanguage.Cshtml);

        Assert.Equal("<Widget\r\n    A=\"1\"\r\n    B=\"2\" />", formatted);
    }

    private static async Task<string> FormatAsync(
        string source,
        IReadOnlyDictionary<string, string>? properties = null,
        FormattingLanguage language = FormattingLanguage.Razor,
        bool extractOpeningTag = false)
    {
        var result = await new FormattingEngine().FormatAsync(new FormattingRequest(language, source, properties));
        var formatted = result.Changes.Count == 0 ? source : Assert.Single(result.Changes).NewText;
        if (!extractOpeningTag)
            return formatted;

        var end = formatted.IndexOf('>') + 1;
        return formatted.Substring(0, end);
    }
}
