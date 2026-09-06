using CSharpWorkbench.Formatting.Core;
using CSharpWorkbench.Formatting.Core.Contracts;
using CSharpWorkbench.Formatting.Core.CSharp.Options;
using CSharpWorkbench.Formatting.Core.CSharp.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace CSharpWorkbench.Formatting.Core.Tests;

public sealed class CSharpSyntaxLineWrapperTests
{
    [Fact]
    public async Task WrapsArgumentsParametersAndInitializersAtSyntaxSeparators()
    {
        var source = "class Demo{void Calculate(int firstArgument,int secondArgument,int thirdArgument){var result=Calculate(firstArgument,secondArgument,thirdArgument);var item=new Item{First=firstArgument,Second=secondArgument,Third=thirdArgument};}}";
        var formatted = await FormatDocumentAsync(source, new Dictionary<string, string>
        {
            ["max_line_length"] = "55",
            ["csharp_preserve_single_line_statements"] = "false",
            ["csharp_preserve_single_line_blocks"] = "false",
        });

        Assert.Contains("void Calculate(int firstArgument,\n", formatted, StringComparison.Ordinal);
        Assert.Contains("var result = Calculate(firstArgument,\n", formatted, StringComparison.Ordinal);
        Assert.Matches(@"First = firstArgument,\r?\n\s*Second = secondArgument", formatted);
    }

    [Fact]
    public async Task WrapsBinaryExpressionsAccordingToOperatorPlacement()
    {
        var beginning = await FormatDocumentAsync(
            "class Demo{void Run(){var result=firstValue+secondValue+thirdValue+fourthValue;}}",
            new Dictionary<string, string>
            {
                ["max_line_length"] = "35",
                ["dotnet_style_operator_placement_when_wrapping"] = "beginning_of_line",
            });
        var ending = await FormatDocumentAsync(
            "class Demo{void Run(){var result=firstValue+secondValue+thirdValue+fourthValue;}}",
            new Dictionary<string, string>
            {
                ["max_line_length"] = "35",
                ["dotnet_style_operator_placement_when_wrapping"] = "end_of_line",
            });

        Assert.Matches(@"firstValue\r?\n\s*\+ secondValue|secondValue\r?\n\s*\+ thirdValue", beginning);
        Assert.Matches(@"firstValue \+\r?\n\s*secondValue|secondValue \+\r?\n\s*thirdValue", ending);
    }

    [Fact]
    public async Task OffAndMissingLengthFollowResolverContract()
    {
        const string source = "class Demo{void Run(){var result=firstValue+secondValue+thirdValue+fourthValue;}}";
        var off = await FormatDocumentAsync(source, new Dictionary<string, string>
        {
            ["max_line_length"] = "off",
        });
        var defaultLength = await FormatDocumentAsync(source, new Dictionary<string, string>());

        Assert.DoesNotContain("\n", off, StringComparison.Ordinal);
        Assert.Matches(@"\r?\n\s*\+", defaultLength);
    }

    [Fact]
    public async Task DoesNotWrapRangeOrSnippet()
    {
        const string source = "void Run(){var result=firstValue+secondValue+thirdValue+fourthValue;}";
        var options = new Dictionary<string, string> { ["max_line_length"] = "20" };
        var resolved = CSharpFormattingOptionsResolver.Resolve(options);
        var formatter = new CSharpRoslynFormatter();
        var rangeRequest = new CSharpFormattingRequest(
            source,
            CSharpFormattingKind.Range,
            resolved,
            new CSharpTextSpan(0, source.Length));
        var snippetRequest = new CSharpFormattingRequest(
            "var result=firstValue+secondValue+thirdValue+fourthValue;",
            CSharpFormattingKind.Snippet,
            resolved,
            snippetKind: CSharpSnippetKind.Statements);
        var rangeResult = await formatter.FormatAsync(rangeRequest);
        var snippetResult = await formatter.FormatAsync(snippetRequest);
        var range = ApplyChanges(source, rangeResult.Changes);
        var snippetSource = snippetRequest.Source;
        var snippet = ApplyChanges(snippetSource, snippetResult.Changes);

        Assert.DoesNotContain("firstValue\n", range, StringComparison.Ordinal);
        Assert.DoesNotContain("firstValue\n", snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreservesUnbreakableTokensStringsAndComments()
    {
        var source = "class Demo{void Run(){var identifier=VeryLongIdentifierWithoutAnySupportedSafeBreakPoint;var text=\"first, second + third, fourth + fifth\";Call(first, /* keep */ second, third);}}";
        var formatted = await FormatDocumentAsync(source, new Dictionary<string, string>
        {
            ["max_line_length"] = "50",
        });

        Assert.Contains("VeryLongIdentifierWithoutAnySupportedSafeBreakPoint", formatted, StringComparison.Ordinal);
        Assert.Contains("\"first, second + third, fourth + fifth\"", formatted, StringComparison.Ordinal);
        Assert.Contains("/* keep */", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvesExplicitFallbackAndDefaultMaxLineLength()
    {
        var explicitValue = CSharpFormattingOptionsResolver.Resolve(
            new Dictionary<string, string> { ["max_line_length"] = "72" },
            new EditorFallback { MaxLineLength = 100 });
        var fallback = CSharpFormattingOptionsResolver.Resolve(
            new Dictionary<string, string>(),
            new EditorFallback { MaxLineLength = 100 });
        var defaultValue = CSharpFormattingOptionsResolver.Resolve(new Dictionary<string, string>());

        Assert.Equal(72, explicitValue.MaxLineLength);
        Assert.Equal(100, fallback.MaxLineLength);
        Assert.Equal(80, defaultValue.MaxLineLength);
    }

    [Fact]
    public async Task PreservesModernSyntaxAndProducesNoNewParseErrors()
    {
        const string source = """"
            #if FEATURE
            using Enabled = System.String;
            #endif
            /// <summary>Modern syntax fixture.</summary>
            sealed class Demo<T> where T : class
            {
                int[] Values = [1, 2, 3, 4];
                void Run(Dictionary<string, List<int>> values)
                {
                    var tuple = (first: 1, second: 2);
                    var (left, right) = tuple;
                    var result = left switch { > 0 and < 10 => right, _ => 0 };
                    Func<int, int> lambda = value => value + result;
                    Func<int> anonymous = delegate { return result; };
                    var generic = Create<Dictionary<string, List<int>>>(values);
                    var item = Values[1];
                    var range = Values[1..^1];
                    var collection = [item, result, lambda(result)];
                    var conditional = generic?.Count;
                    var interpolated = $"{left}, {right + result}";
                    var verbatim = @"first, second + third";
                    var raw = """first, second + third""";
                }

                static TItem Create<TItem>(TItem value) => value;
            }
            """";
        var formatted = await FormatDocumentAsync(source, new Dictionary<string, string>
        {
            ["max_line_length"] = "45",
        });
        var diagnostics = CSharpSyntaxTree.ParseText(
            formatted,
            new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse))
            .GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();

        Assert.Empty(diagnostics);
        Assert.Contains("generic?.Count", formatted, StringComparison.Ordinal);
        Assert.Contains("\"\"\"first, second + third\"\"\"", formatted, StringComparison.Ordinal);
        Assert.Contains("#if FEATURE", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsesTabContinuationAndCrLfAndIsIdempotent()
    {
        var properties = new Dictionary<string, string>
        {
            ["max_line_length"] = "35",
            ["indent_style"] = "tab",
            ["tab_width"] = "4",
            ["end_of_line"] = "crlf",
            ["csharp_preserve_single_line_statements"] = "false",
            ["csharp_preserve_single_line_blocks"] = "false",
        };
        var source = "class Demo\r\n{\r\n\tvoid Run()\r\n\t{\r\n\t\tvar result = Calculate(firstArgument, secondArgument, thirdArgument);\r\n\t}\r\n}";
        var once = await FormatDocumentAsync(source, properties);
        var twice = await FormatDocumentAsync(once, properties);

        Assert.Contains("\r\n", once, StringComparison.Ordinal);
        Assert.Contains("\r\n\t", once, StringComparison.Ordinal);
        Assert.True(once == twice, $"First:\n{once}\nSecond:\n{twice}");
    }

    [Theory]
    [InlineData(
        "class Demo{void Run(){var result=Calculate(firstArgument,secondArgument,thirdArgument,fourthArgument);}}",
        "beginning_of_line")]
    [InlineData(
        "class Demo{void Run(){var item=new Item{First=firstValue,Second=secondValue,Third=thirdValue,Fourth=fourthValue};}}",
        "beginning_of_line")]
    [InlineData(
        "class Demo{void Run(){var result=firstValue+secondValue+thirdValue+fourthValue+fifthValue;}}",
        "beginning_of_line")]
    [InlineData(
        "class Demo{void Run(){var result=firstValue+secondValue+thirdValue+fourthValue+fifthValue;}}",
        "end_of_line")]
    public async Task RepresentativeWrappingIsIdempotent(string source, string operatorPlacement)
    {
        var properties = new Dictionary<string, string>
        {
            ["max_line_length"] = "35",
            ["dotnet_style_operator_placement_when_wrapping"] = operatorPlacement,
            ["csharp_preserve_single_line_statements"] = "false",
            ["csharp_preserve_single_line_blocks"] = "false",
        };

        var once = await FormatDocumentAsync(source, properties);
        var twice = await FormatDocumentAsync(once, properties);

        Assert.Equal(once, twice);
    }

    private static Task<string> FormatDocumentAsync(string source, IReadOnlyDictionary<string, string> properties)
    {
        return FormatDocumentCoreAsync(source, properties);
    }

    private static async Task<string> FormatDocumentCoreAsync(
        string source,
        IReadOnlyDictionary<string, string> properties)
    {
        var result = await new FormattingEngine().FormatAsync(
            new FormattingRequest(FormattingLanguage.CSharp, source, properties));
        return ApplyChanges(source, result.Changes);
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

    private static string ApplyChanges(string source, IReadOnlyList<CSharpTextChange> changes)
    {
        foreach (var change in changes.OrderByDescending(change => change.Span.Start))
        {
            source = source.Remove(change.Span.Start, change.Span.Length)
                .Insert(change.Span.Start, change.NewText);
        }

        return source;
    }
}
