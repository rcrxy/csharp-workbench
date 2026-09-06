using CSharpWorkbench.Formatting.Core;
using CSharpWorkbench.Formatting.Core.Contracts;
using Xunit;

namespace CSharpWorkbench.Formatting.Core.Tests;

public sealed class LegacyCSharpFormattingParityTests
{
    [Fact]
    public async Task FormatsIndentationWithThreeSpaceKAndRProfile()
    {
        var source = "class Demo{void Run(){if(true){Work();}switch(value){case 1:{Work();break;}default:Work();}}}";
        var formatted = await FormatAsync(
            source,
            new Dictionary<string, string>
            {
                ["indent_style"] = "space",
                ["indent_size"] = "3",
                ["tab_width"] = "3",
                ["csharp_new_line_before_open_brace"] = "none",
                ["csharp_preserve_single_line_statements"] = "false",
                ["csharp_preserve_single_line_blocks"] = "false",
            });

        Assert.Contains("class Demo {", formatted, StringComparison.Ordinal);
        Assert.Contains("   void Run() {", formatted, StringComparison.Ordinal);
        Assert.Contains("      if (true) {", formatted, StringComparison.Ordinal);
        Assert.Contains("         Work();", formatted, StringComparison.Ordinal);
        Assert.Contains("      switch (value) {", formatted, StringComparison.Ordinal);
        Assert.Contains("         case 1:", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatsTabsAndConfiguredCrLf()
    {
        var formatted = await FormatAsync(
            "class Demo{void Run(){Work();}}",
            new Dictionary<string, string>
            {
                ["indent_style"] = "tab",
                ["indent_size"] = "4",
                ["tab_width"] = "4",
                ["end_of_line"] = "crlf",
                ["csharp_new_line_before_open_brace"] = "all",
                ["csharp_preserve_single_line_statements"] = "false",
                ["csharp_preserve_single_line_blocks"] = "false",
            });

        Assert.DoesNotContain("\n", formatted.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("class Demo\r\n{", formatted, StringComparison.Ordinal);
        Assert.Contains("\r\n\tvoid Run()\r\n\t{", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatsNewLineAndSpacingRulesThroughRawEditorConfig()
    {
        var formatted = await FormatAsync(
            "class Demo:IThing,IOther{void Run(){if(true){for(int i=0;i<2;i++){Use((Item)value,a,b);}}}else{}}",
            new Dictionary<string, string>
            {
                ["csharp_new_line_before_open_brace"] = "none",
                ["csharp_new_line_before_else"] = "false",
                ["csharp_space_after_keywords_in_control_flow_statements"] = "false",
                ["csharp_space_around_binary_operators"] = "none",
                ["csharp_space_after_comma"] = "false",
                ["csharp_space_before_comma"] = "true",
                ["csharp_space_after_semicolon_in_for_statement"] = "false",
                ["csharp_space_before_semicolon_in_for_statement"] = "true",
                ["csharp_space_after_cast"] = "true",
                ["csharp_space_before_colon_in_inheritance_clause"] = "false",
                ["csharp_space_after_colon_in_inheritance_clause"] = "false",
                ["csharp_preserve_single_line_statements"] = "false",
                ["csharp_preserve_single_line_blocks"] = "false",
            });

        Assert.Contains("class Demo:IThing, IOther {", formatted, StringComparison.Ordinal);
        Assert.Contains("if(true) {", formatted, StringComparison.Ordinal);
        Assert.Matches(@"for\s*\(int i\s*=\s*0\s*;\s*i\s*<\s*2\s*;\s*i\+\+\)", formatted);
        Assert.Contains("Use((Item) value ,a ,b)", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreservesOrExpandsSingleLineStatementsAndBlocks()
    {
        const string source = "class Demo{void Run(){if(true){Work();}Work();}}";
        var preserved = await FormatAsync(
            source,
            new Dictionary<string, string>
            {
                ["csharp_new_line_before_open_brace"] = "none",
                ["csharp_preserve_single_line_statements"] = "true",
                ["csharp_preserve_single_line_blocks"] = "true",
            });
        var expanded = await FormatAsync(
            source,
            new Dictionary<string, string>
            {
                ["csharp_preserve_single_line_statements"] = "false",
                ["csharp_preserve_single_line_blocks"] = "false",
            });

        Assert.Contains("if (true) { Work(); }", preserved, StringComparison.Ordinal);
        Assert.Contains("if (true)\n", expanded, StringComparison.Ordinal);
        Assert.Contains("Work();\n", expanded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsSyntaxSensitiveTextStableWhileFormatting()
    {
        var source = "class Demo{void Run(){List<string> values=GetRequiredService<HomeViewModel>();string text=\"if(a+b){x;}\";string raw=\"\"\"if(a+b){x;}\"\"\";int value=left+right;value+=next;}}";
        var formatted = await FormatAsync(source, new Dictionary<string, string>
        {
            ["csharp_new_line_before_open_brace"] = "none",
            ["csharp_preserve_single_line_statements"] = "false",
            ["csharp_preserve_single_line_blocks"] = "false",
        });

        Assert.Contains("List<string> values", formatted, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<HomeViewModel>()", formatted, StringComparison.Ordinal);
        Assert.Contains("\"if(a+b){x;}\"", formatted, StringComparison.Ordinal);
        Assert.Contains("\"\"\"if(a+b){x;}\"\"\"", formatted, StringComparison.Ordinal);
        Assert.Contains("left + right", formatted, StringComparison.Ordinal);
        Assert.Contains("value += next;", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatsRepresentativeOutputIdempotently()
    {
        const string source = "class Demo{void Run(){if(true){Work();}}}";
        var properties = new Dictionary<string, string>
        {
            ["indent_style"] = "space",
            ["indent_size"] = "3",
            ["csharp_new_line_before_open_brace"] = "none",
            ["csharp_space_around_binary_operators"] = "before_and_after",
        };

        var once = await FormatAsync(source, properties);
        var twice = await FormatAsync(once, properties);

        Assert.Equal(once, twice);
    }

    [Fact]
    public async Task SelectedMethodsDoNotClaimLocalFunctionContext()
    {
        var formatted = await FormatAsync(
            "class Demo{void Run(){void Local(){Work();}}}",
            new Dictionary<string, string>
            {
                ["csharp_new_line_before_open_brace"] = "methods",
                ["csharp_preserve_single_line_statements"] = "false",
                ["csharp_preserve_single_line_blocks"] = "false",
            });

        Assert.Contains("void Run()\n    {", formatted, StringComparison.Ordinal);
        Assert.Contains("void Local()\n        {", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectedPropertiesDoNotClaimEventsAndIndexers()
    {
        var formatted = await FormatAsync(
            "class Demo{int Value{get{return 1;}}event EventHandler Changed{add{}remove{}}int this[int index]{get{return index;}}}",
            new Dictionary<string, string>
            {
                ["csharp_new_line_before_open_brace"] = "properties",
                ["csharp_preserve_single_line_statements"] = "false",
                ["csharp_preserve_single_line_blocks"] = "false",
            });

        Assert.Contains("int Value\n    {", formatted, StringComparison.Ordinal);
        Assert.Contains("event EventHandler Changed {", formatted, StringComparison.Ordinal);
        Assert.Contains("this[int index] {", formatted, StringComparison.Ordinal);
    }

    private static async Task<string> FormatAsync(
        string source,
        IReadOnlyDictionary<string, string> properties)
    {
        var result = await new FormattingEngine().FormatAsync(
            new FormattingRequest(FormattingLanguage.CSharp, source, properties));
        return ApplyChanges(source, result.Changes);
    }

    private static string ApplyChanges(string source, IReadOnlyList<FormattingTextChange> changes)
    {
        var result = source;
        foreach (var change in changes.OrderByDescending(change => change.Span.Start))
        {
            result = result.Remove(change.Span.Start, change.Span.Length)
                .Insert(change.Span.Start, change.NewText);
        }

        return result;
    }
}
