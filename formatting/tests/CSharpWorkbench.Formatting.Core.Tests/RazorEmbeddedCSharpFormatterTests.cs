using CSharpWorkbench.Formatting.Core.Contracts;
using Xunit;

namespace CSharpWorkbench.Formatting.Core.Tests;

public sealed class RazorEmbeddedCSharpFormatterTests
{
    [Fact]
    public async Task FormatsCodeAndFunctionsBodiesAsTypeMembers()
    {
        const string source = "@code {\nint x=1;\nvoid M(){Console.WriteLine(x);}\n}\n@functions {\nstring Name=>\"Demo\";\n}";
        var formatted = await FormatAsync(source, new Dictionary<string, string>
        {
            ["csharp_preserve_single_line_blocks"] = "false",
            ["csharp_preserve_single_line_statements"] = "false",
        });

        Assert.Contains("int x = 1;", formatted, StringComparison.Ordinal);
        Assert.Contains("void M()", formatted, StringComparison.Ordinal);
        Assert.Contains("Console.WriteLine(x);", formatted, StringComparison.Ordinal);
        Assert.Contains("string Name => \"Demo\";", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("CSharpWorkbenchSnippet", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatsExplicitCodeBlockAsStatements()
    {
        const string source = "@{\nvar value=1;\nDo(value);\n}";
        var formatted = await FormatAsync(source);

        Assert.Contains("var value = 1;", formatted, StringComparison.Ordinal);
        Assert.Contains("Do(value);", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RebasesCodeBlockBodyInsideMarkupDepth()
    {
        const string source = "<div>\n@{\nvar value=1;\nif(value>0){Do(value);}\n}\n</div>";
        var formatted = await FormatAsync(source, new Dictionary<string, string>
        {
            ["csharp_preserve_single_line_blocks"] = "false",
            ["csharp_preserve_single_line_statements"] = "false",
        });

        Assert.Contains("    @{\n        var value = 1;", formatted, StringComparison.Ordinal);
        Assert.Contains("        if (value > 0)", formatted, StringComparison.Ordinal);
        Assert.Contains("    }\n</div>", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatsInlineRazorExpressionWithoutChangingLineShape()
    {
        Assert.Equal("<span>@(left + right)</span>", await FormatAsync("<span>@(left+right)</span>"));
    }

    [Fact]
    public async Task FormatsComponentAttributeExpressionsButNotPlainLiteralValues()
    {
        const string source = "<Widget Click=\"@(() => Save(item))\" Visible=\"@(left+right)\" @onclick=\"Handle\" Text=\"left+right\" />";
        var formatted = await FormatAsync(source);

        Assert.Contains("Click=\"@(() => Save(item))\"", formatted, StringComparison.Ordinal);
        Assert.Contains("Visible=\"@(left + right)\"", formatted, StringComparison.Ordinal);
        Assert.Contains("@onclick=\"Handle\"", formatted, StringComparison.Ordinal);
        Assert.Contains("Text=\"left+right\"", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultipleEmbeddedRegionsUseOriginalSourceOffsets()
    {
        const string source = "<span>@(left+right)</span>\n@{\nvar value=1;\n}\n<Widget Value=\"@(value+1)\" />";
        var formatted = await FormatAsync(source);

        Assert.Contains("@(left + right)", formatted, StringComparison.Ordinal);
        Assert.Contains("var value = 1;", formatted, StringComparison.Ordinal);
        Assert.Contains("@(value + 1)", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidEmbeddedRegionDoesNotBlockOtherRegions()
    {
        const string source = "<span>@(left+)</span><Widget Value=\"@(right+1)\" />";
        var formatted = await FormatAsync(source);

        Assert.Contains("@(left+)", formatted, StringComparison.Ordinal);
        Assert.Contains("@(right + 1)", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmbeddedCSharpAndMarkupAreIdempotent()
    {
        const string source = "<div><span>@(left+right)</span><Widget Value=\"@(left+right)\" /></div>\n@code {\nint value=1;\n}";
        var once = await FormatAsync(source);
        var twice = await FormatAsync(once);

        Assert.Equal(once, twice);
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
