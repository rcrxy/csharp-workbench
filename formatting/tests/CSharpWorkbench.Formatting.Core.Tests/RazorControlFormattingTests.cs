using CSharpWorkbench.Formatting.Core.Contracts;
using CSharpWorkbench.Formatting.Core.Razor;
using Xunit;

namespace CSharpWorkbench.Formatting.Core.Tests;

public sealed class RazorControlFormattingTests
{
    [Fact]
    public async Task FormatsIfHeaderAndJoinsBraceAndElseWhenConfigured()
    {
        const string source = "<div>\n@if(enabled)\n{\n<span>Yes</span>\n}\nelse\n{\n<span>No</span>\n}\n</div>";
        var formatted = await FormatAsync(source, new Dictionary<string, string>
        {
            ["csharp_new_line_before_open_brace"] = "none",
            ["csharp_new_line_before_else"] = "false",
        });

        Assert.Contains("    @if (enabled) {", formatted, StringComparison.Ordinal);
        Assert.Contains("    } else {", formatted, StringComparison.Ordinal);
        Assert.Equal(formatted, await FormatAsync(formatted, new Dictionary<string, string>
        {
            ["csharp_new_line_before_open_brace"] = "none",
            ["csharp_new_line_before_else"] = "false",
        }));
    }

    [Fact]
    public async Task KeepsElseCatchAndFinallyOnNewLinesWhenConfigured()
    {
        const string source = "@if(first) {\nWork();\n} else {\nOther();\n}\n@try {\nRun();\n} catch(Exception) {\nRecover();\n} finally {\nCleanup();\n}";
        var formatted = await FormatAsync(source, new Dictionary<string, string>
        {
            ["csharp_new_line_before_open_brace"] = "control_blocks",
            ["csharp_new_line_before_else"] = "true",
            ["csharp_new_line_before_catch"] = "true",
            ["csharp_new_line_before_finally"] = "true",
        });

        Assert.Contains("@if (first)\n{", formatted, StringComparison.Ordinal);
        Assert.Contains("}\nelse\n{", formatted, StringComparison.Ordinal);
        Assert.Contains("}\ncatch (Exception)\n{", formatted, StringComparison.Ordinal);
        Assert.Contains("}\nfinally\n{", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DoWhileContinuationStaysJoined()
    {
        const string source = "@do\n{\nAdvance();\n}\nwhile(pending);";
        var formatted = await FormatAsync(source);

        Assert.Contains("} while (pending);", formatted, StringComparison.Ordinal);
        Assert.Equal(formatted, await FormatAsync(formatted));
    }

    [Fact]
    public async Task IndentBlockContentsFalseKeepsMarkupAndCSharpAtHeaderDepth()
    {
        const string source = "<div>\n@if(enabled)\n{\nvar value=1;\n<span>Value</span>\n}\n</div>";
        var formatted = await FormatAsync(source, new Dictionary<string, string>
        {
            ["csharp_indent_block_contents"] = "false",
        });

        Assert.Contains("    var value = 1;", formatted, StringComparison.Ordinal);
        Assert.Contains("    <span>Value</span>", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteSingleLineControlRemainsReliableAndInline()
    {
        const string source = "<div>\n@if(enabled) { count++; }\n<span>After</span>\n</div>";
        var once = await FormatAsync(source);
        var twice = await FormatAsync(once);
        var model = new RazorDocumentScanner().Scan(once, RazorDocumentKind.Component, default);

        Assert.True(model.IsReliable);
        Assert.Contains("@if (enabled) { count++; }", once, StringComparison.Ordinal);
        Assert.Equal(once, twice);
    }

    [Fact]
    public async Task ExplicitFunctionsBlankLinesApplyOnlyWhenConfigured()
    {
        const string source = "<div></div>\n@functions {\nstring Name=>\"Demo\";\n}\n<span></span>";
        var formatted = await FormatAsync(source, new Dictionary<string, string>
        {
            ["html_blank_lines_around_razor_functions"] = "1",
        });

        Assert.Contains("</div>\n\n@functions", formatted, StringComparison.Ordinal);
        Assert.Contains("}\n\n<span>", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddsLineBreaksAroundInlineRazorStatementWhenEnabled()
    {
        const string source = "something @if(a==1) { @: SomeText } something";
        var properties = new Dictionary<string, string>
        {
            ["html_linebreaks_around_razor_statements"] = "true",
        };
        const string expected = "something\n@if (a == 1) { @: SomeText }\nsomething";

        var once = await FormatAsync(source, properties);
        var twice = await FormatAsync(once, properties);

        Assert.Equal(expected, once);
        Assert.Equal(expected, twice);
    }

    [Fact]
    public async Task DisabledInlineRazorStatementRuleOnlyFormatsHeader()
    {
        const string source = "something @if(a==1) { @:   SomeText } something";

        Assert.Equal(
            "something @if (a == 1) { @:   SomeText } something",
            await FormatAsync(source));
    }

    [Fact]
    public async Task InlineRazorStatementUsesScannerHeaderBoundaryWhenConditionContainsBrace()
    {
        const string source = "before @if(value==\"{\") { @: x } after";

        Assert.Equal(
            "before\n@if (value == \"{\") { @: x }\nafter",
            await FormatAsync(source, new Dictionary<string, string>
            {
                ["html_linebreaks_around_razor_statements"] = "true",
            }));
    }

    [Fact]
    public async Task InlineRazorStatementUsesContainingElementIndent()
    {
        const string source = "<div>before @if(a) { @: x } after</div>";

        Assert.Equal(
            "<div>before\n    @if (a) { @: x }\n    after</div>",
            await FormatAsync(source, new Dictionary<string, string>
            {
                ["html_linebreaks_around_razor_statements"] = "true",
            }));
    }

    [Fact]
    public async Task InlineRazorStatementBoundarySurvivesDirectTextChildLayoutPath()
    {
        const string source = "<div>before @if(a) { @: x } after<Widget /></div>";

        Assert.Equal(
            "<div>before\n    @if (a) { @: x }\n    after\n    <Widget /></div>",
            await FormatAsync(source, new Dictionary<string, string>
            {
                ["html_linebreak_before_all_elements"] = "true",
                ["html_linebreaks_around_razor_statements"] = "true",
            }));
    }

    [Fact]
    public async Task InlineRazorStatementRuleDoesNotEnableHtmlStructuralLineBreaks()
    {
        const string source = "<div>before @if(a) { @: x } after</div><span>tail</span>";
        var properties = new Dictionary<string, string>
        {
            ["html_linebreak_before_all_elements"] = "false",
            ["html_linebreak_before_multiline_elements"] = "false",
            ["html_linebreaks_inside_tags_for_multiline_elements"] = "false",
            ["html_linebreaks_inside_tags_for_elements_with_child_elements"] = "false",
            ["html_linebreaks_around_razor_statements"] = "true",
        };

        Assert.Equal(
            "<div>before\n    @if (a) { @: x }\n    after</div><span>tail</span>",
            await FormatAsync(source, properties));
    }

    [Fact]
    public async Task InlineRazorStatementUsesConfiguredCrlf()
    {
        const string source = "before @if(a) { @: x } after";

        Assert.Equal(
            "before\r\n@if (a) { @: x }\r\nafter",
            await FormatAsync(source, new Dictionary<string, string>
            {
                ["end_of_line"] = "crlf",
                ["html_linebreaks_around_razor_statements"] = "true",
            }));
    }

    [Fact]
    public async Task InlineRazorStatementRuleAlsoAppliesToCshtml()
    {
        const string source = "before @if(a) { @: x } after";

        Assert.Equal(
            "before\n@if (a) { @: x }\nafter",
            await FormatAsync(source, new Dictionary<string, string>
            {
                ["html_linebreaks_around_razor_statements"] = "true",
            }, FormattingLanguage.Cshtml));
    }

    [Fact]
    public async Task HeaderRangeFormatsCSharpWithoutChangingStatementBoundaries()
    {
        const string source = "before @if(a==1) { @: x } after";
        var result = await new FormattingEngine().FormatRangeAsync(
            FormattingLanguage.Razor,
            source,
            SpanOf(source, "a==1"),
            new Dictionary<string, string>
            {
                ["html_linebreaks_around_razor_statements"] = "true",
            });

        Assert.Equal("before @if (a == 1) { @: x } after", ApplyChanges(source, result.Changes));
    }

    [Fact]
    public async Task WholeInlineControlRangeDoesNotModifyAdjacentTextBoundaries()
    {
        const string source = "before @if(a==1) { @: x } after";
        var result = await new FormattingEngine().FormatRangeAsync(
            FormattingLanguage.Razor,
            source,
            SpanOf(source, "@if(a==1) { @: x }"),
            new Dictionary<string, string>
            {
                ["html_linebreaks_around_razor_statements"] = "true",
            });

        Assert.Equal("before @if (a == 1) { @: x } after", ApplyChanges(source, result.Changes));
    }

    [Fact]
    public async Task ContainingElementRangeAppliesInlineBoundariesIdempotently()
    {
        const string source = "<div>before @if(a==1) { @: x } after</div>";
        var properties = new Dictionary<string, string>
        {
            ["html_linebreaks_around_razor_statements"] = "true",
        };
        var onceResult = await new FormattingEngine().FormatRangeAsync(
            FormattingLanguage.Razor,
            source,
            new FormattingTextSpan(0, source.Length),
            properties);
        var once = ApplyChanges(source, onceResult.Changes);
        var twiceResult = await new FormattingEngine().FormatRangeAsync(
            FormattingLanguage.Razor,
            once,
            new FormattingTextSpan(0, once.Length),
            properties);

        Assert.Equal("<div>before\n    @if (a == 1) { @: x }\n    after</div>", once);
        Assert.Empty(twiceResult.Changes);
    }

    private static async Task<string> FormatAsync(
        string source,
        IReadOnlyDictionary<string, string>? properties = null,
        FormattingLanguage language = FormattingLanguage.Razor)
    {
        var result = await new FormattingEngine().FormatAsync(
            new FormattingRequest(language, source, properties));
        return result.Changes.Count == 0 ? source : Assert.Single(result.Changes).NewText;
    }

    private static FormattingTextSpan SpanOf(string source, string value)
    {
        return new FormattingTextSpan(source.IndexOf(value, StringComparison.Ordinal), value.Length);
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
