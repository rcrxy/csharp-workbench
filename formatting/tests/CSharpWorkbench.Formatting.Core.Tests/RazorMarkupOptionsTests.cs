using CSharpWorkbench.Formatting.Core.Contracts;
using CSharpWorkbench.Formatting.Core.Razor;
using Xunit;

namespace CSharpWorkbench.Formatting.Core.Tests;

public sealed class RazorMarkupOptionsTests
{
    [Fact]
    public void HtmlIndentationOverridesAliasesGenericAndFallback()
    {
        var options = RazorFormattingOptionsResolver.Resolve(
            new Dictionary<string, string>
            {
                ["html_indent_style"] = "tab",
                ["html_indent_size"] = "3",
                ["html_tab_width"] = "6",
                ["resharper_html_indent_style"] = "space",
                ["indent_style"] = "space",
                ["indent_size"] = "2",
            },
            new EditorFallback { InsertSpaces = true, TabSize = 8 });

        Assert.True(options.UseTabs);
        Assert.Equal(3, options.IndentSize);
        Assert.Equal(6, options.TabWidth);
    }

    [Fact]
    public void ResharperHtmlIndentationOverridesGenericIndentation()
    {
        var options = RazorFormattingOptionsResolver.Resolve(new Dictionary<string, string>
        {
            ["resharper_html_indent_style"] = "space",
            ["resharper_html_indent_size"] = "5",
            ["indent_style"] = "tab",
            ["indent_size"] = "2",
        });

        Assert.False(options.UseTabs);
        Assert.Equal(5, options.IndentSize);
    }

    [Fact]
    public void NeutralInvalidValueDoesNotFallBackToResharperAlias()
    {
        var options = RazorFormattingOptionsResolver.Resolve(new Dictionary<string, string>
        {
            ["html_attribute_style"] = "future_style",
            ["resharper_html_attribute_style"] = "on_different_lines",
        });

        Assert.Equal(RazorAttributeStyle.OnSingleLine, options.Markup.AttributeStyle);
    }

    [Fact]
    public void AttributeWrapUsesNeutralThenIjAlias()
    {
        var alias = RazorFormattingOptionsResolver.Resolve(new Dictionary<string, string>
        {
            ["ij_html_attribute_wrap"] = "normal",
        });
        var neutral = RazorFormattingOptionsResolver.Resolve(new Dictionary<string, string>
        {
            ["html_attribute_wrap"] = "split_into_lines",
            ["ij_html_attribute_wrap"] = "normal",
        });

        Assert.Equal(RazorAttributeWrapPolicy.Normal, alias.Markup.AttributeWrap);
        Assert.Equal(RazorAttributeWrapPolicy.SplitIntoLines, neutral.Markup.AttributeWrap);
    }

    [Fact]
    public void MaxLineLengthUsesOffFallbackAndRazorDefault()
    {
        Assert.Null(RazorFormattingOptionsResolver.Resolve(new Dictionary<string, string>
        {
            ["max_line_length"] = "off",
        }, new EditorFallback { MaxLineLength = 90 }).MaxLineLength);
        Assert.Equal(90, RazorFormattingOptionsResolver.Resolve(
            new Dictionary<string, string>(),
            new EditorFallback { MaxLineLength = 90 }).MaxLineLength);
        Assert.Equal(120, RazorFormattingOptionsResolver.Resolve(new Dictionary<string, string>()).MaxLineLength);
    }

    [Fact]
    public void ElementSetsAreTrimmedAndCaseInsensitive()
    {
        var options = RazorFormattingOptionsResolver.Resolve(new Dictionary<string, string>
        {
            ["html_no_indent_inside_elements"] = " pre, TEXTAREA, custom, ",
        });

        Assert.Contains("Pre", options.Markup.NoIndentInsideElements);
        Assert.Contains("textarea", options.Markup.NoIndentInsideElements);
        Assert.Contains("CUSTOM", options.Markup.NoIndentInsideElements);
    }

    [Theory]
    [InlineData("leave_tabs", "LeaveTabs")]
    [InlineData("leave_multiple", "LeaveMultiple")]
    [InlineData("leave_all", "LeaveAll")]
    public void ExtraSpacesCompatibilityValuesAreParsed(string value, string expected)
    {
        var options = RazorFormattingOptionsResolver.Resolve(new Dictionary<string, string>
        {
            ["html_extra_spaces"] = value,
        });

        Assert.Equal(expected, options.Markup.ExtraSpaces.ToString());
    }

    [Fact]
    public void RazorStatementLineBreakUsesNeutralThenResharperAlias()
    {
        var alias = RazorFormattingOptionsResolver.Resolve(new Dictionary<string, string>
        {
            ["resharper_html_linebreaks_around_razor_statements"] = "true",
        });
        var neutral = RazorFormattingOptionsResolver.Resolve(new Dictionary<string, string>
        {
            ["html_linebreaks_around_razor_statements"] = "false",
            ["resharper_html_linebreaks_around_razor_statements"] = "true",
        });

        Assert.True(alias.LineBreaksAroundRazorStatements);
        Assert.False(neutral.LineBreaksAroundRazorStatements);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("invalid")]
    public void InvalidRazorStatementLineBreakValueUsesFalseDefault(string value)
    {
        var options = RazorFormattingOptionsResolver.Resolve(new Dictionary<string, string>
        {
            ["html_linebreaks_around_razor_statements"] = value,
        });

        Assert.False(options.LineBreaksAroundRazorStatements);
    }
}
