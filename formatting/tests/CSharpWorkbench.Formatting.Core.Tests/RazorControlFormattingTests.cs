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

    private static async Task<string> FormatAsync(
        string source,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        var result = await new FormattingEngine().FormatAsync(
            new FormattingRequest(FormattingLanguage.Razor, source, properties));
        return result.Changes.Count == 0 ? source : Assert.Single(result.Changes).NewText;
    }
}
