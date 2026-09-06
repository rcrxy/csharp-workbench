using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;
using CSharpWorkbench.Formatting.Core.Contracts;
using CSharpWorkbench.Formatting.Core.CSharp.Options;
using CSharpWorkbench.Formatting.Core.CSharp.Roslyn;
using CSharpWorkbench.Formatting.Core.CSharp.Rules;
using CSharpWorkbench.Formatting.Core.Errors;

namespace CSharpWorkbench.Formatting.Core.Tests;

public sealed class CSharpRoslynFormatterTests
{
    [Fact]
    public async Task FormattingEngineDispatchesCSharpDocumentFormatting()
    {
        var request = new FormattingRequest(
            FormattingLanguage.CSharp,
            "class Demo{void Run(){}}",
            new Dictionary<string, string>
            { ["csharp_new_line_before_open_brace"] = "none" });

        var result = await new FormattingEngine().FormatAsync(request);
        var change = Assert.Single(result.Changes);

        Assert.Contains("class Demo {", change.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormattingEngineRejectsUnsupportedLanguage()
    {
        var exception = await Assert.ThrowsAsync<FormattingException>(() =>
            new FormattingEngine().FormatAsync(
                new FormattingRequest((FormattingLanguage)999, "class Demo { }")));

        Assert.Equal(FormattingErrorCode.UnsupportedLanguage, exception.Code);
    }

    [Fact]
    public void RawEditorConfigOverridesEditorFallback()
    {
        var options = CSharpFormattingOptionsResolver.Resolve(
            new Dictionary<string, string>
            {
                ["indent_style"] = "space",
                ["indent_size"] = "2",
            },
            new EditorFallback
            { InsertSpaces = false,
                TabSize = 8 });

        Assert.Equal(CSharpIndentationStyle.Space, options.Indentation.Style);
        Assert.Equal(2, options.Indentation.Size);
        Assert.Equal(2, options.Indentation.TabWidth);
    }

    [Fact]
    public void MissingEditorConfigUsesEditorFallback()
    {
        var options = CSharpFormattingOptionsResolver.Resolve(
            new Dictionary<string, string>(),
            new EditorFallback
            {
                InsertSpaces = false,
                TabSize = 3,
                LineEnding = "\r\n",
                InsertFinalNewline = true,
                TrimTrailingWhitespace = true,
        });

        Assert.Equal(CSharpIndentationStyle.Tab, options.Indentation.Style);
        Assert.Equal(3, options.Indentation.Size);
        Assert.Equal(3, options.Indentation.TabWidth);
        Assert.Equal("\r\n", options.LineEnding);
        Assert.True(options.InsertFinalNewline);
        Assert.True(options.TrimTrailingWhitespace);
    }

    [Fact]
    public void UnknownEditorConfigKeysAreIgnored()
    {
        var options = CSharpFormattingOptionsResolver.Resolve(
            new Dictionary<string, string>
            { ["future_formatting_option"] = "enabled" });

        Assert.Equal(4, options.Indentation.Size);
        Assert.True(options.CSharpSpacing.AfterComma);
    }

    [Fact]
    public async Task RawCSharpEditorConfigAffectsRoslynOutput()
    {
        var properties = new Dictionary<string, string>
        {
            ["csharp_new_line_before_open_brace"] = "none",
            ["csharp_space_after_comma"] = "false",
        };
        var options = CSharpFormattingOptionsResolver.Resolve(properties);
        var formatted = await FormatAsync(
            new CSharpFormattingRequest("class Demo{void Run(int left,int right){}}", CSharpFormattingKind.Document, options));

        Assert.Contains("class Demo {", formatted, StringComparison.Ordinal);
        Assert.Contains("Run(int left,int right)", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatDocumentUsesRoslynSyntaxFormatting()
    {
        var source = "class Demo{void Run(){if(true){Console.WriteLine(\"x\");}}}";
        var options = CreateOptions();
        options.CSharpWrapping.PreserveSingleLineBlocks = false;
        options.CSharpWrapping.PreserveSingleLineStatements = false;
        var formatted = await FormatAsync(new CSharpFormattingRequest(source, CSharpFormattingKind.Document, options));

        Assert.Equal(
            "class Demo\n{\n    void Run()\n    {\n        if (true)\n        {\n            Console.WriteLine(\"x\");\n        }\n    }\n}",
            formatted);
    }

    [Fact]
    public async Task FormatDocumentHandlesModernComplexSyntaxAndIsIdempotent()
    {
        var source = """"
        namespace Demo;
        sealed class Store<T>{public required T Value{get;init;} public int[] Values=>[1,2,3]; public string Text=>"""
        value
        """;}
        """";
        var options = CreateOptions();
        var once = await FormatAsync(new CSharpFormattingRequest(source, CSharpFormattingKind.Document, options));
        var twiceResult = await new CSharpRoslynFormatter().FormatAsync(
            new CSharpFormattingRequest(once, CSharpFormattingKind.Document, options));

        Assert.Contains("sealed class Store<T>", once, StringComparison.Ordinal);
        Assert.Contains("public required T Value", once, StringComparison.Ordinal);
        Assert.Contains("public int[] Values => [1, 2, 3];", once, StringComparison.Ordinal);
        Assert.Empty(twiceResult.Changes);
    }

    [Fact]
    public async Task FormatRangeReturnsOnlyTheRequestedSourceSpan()
    {
        var source = "class Demo\n{\n    void Run()\n    {\nif(true){Work();}\n    }\n}";
        const string selectedSource = "if(true){Work();}";
        var start = source.IndexOf(selectedSource, StringComparison.Ordinal);
        var request = new CSharpFormattingRequest(
            source,
            CSharpFormattingKind.Range,
            CreateOptions(),
            new CSharpTextSpan(start, selectedSource.Length));

        var result = await new CSharpRoslynFormatter().FormatAsync(request);
        var change = Assert.Single(result.Changes);

        Assert.Equal(start, change.Span.Start);
        Assert.Equal(selectedSource.Length, change.Span.Length);
        Assert.DoesNotContain("class Demo", change.NewText, StringComparison.Ordinal);
        Assert.Contains("if (true)", change.NewText, StringComparison.Ordinal);
        Assert.Equal(change.NewText, ApplyChanges(selectedSource, new[]
                { new CSharpTextChange(new CSharpTextSpan(0, selectedSource.Length), change.NewText) }));
    }

    [Fact]
    public async Task FormatRangeUsesUtf16Offsets()
    {
        var source = "// 😀\nclass Demo{\n}";
        const string selectedSource = "class Demo{";
        var start = source.IndexOf(selectedSource, StringComparison.Ordinal);
        var request = new CSharpFormattingRequest(
            source,
            CSharpFormattingKind.Range,
            CreateOptions(),
            new CSharpTextSpan(start, selectedSource.Length));

        var result = await new CSharpRoslynFormatter().FormatAsync(request);
        var change = Assert.Single(result.Changes);

        Assert.Equal(start, change.Span.Start);
        Assert.Equal(6, start);
    }

    [Fact]
    public async Task FormatTypeMembersSnippetDoesNotLeakWrapperOrWrapperIndentation()
    {
        var source = "public int Add(int left,int right){return left+right;}";
        var options = CreateOptions();
        options.CSharpWrapping.PreserveSingleLineBlocks = false;
        var request = new CSharpFormattingRequest(
            source,
            CSharpFormattingKind.Snippet,
            options,
            snippetKind: CSharpSnippetKind.TypeMembers);
        var formatted = await FormatAsync(request);

        Assert.Equal("public int Add(int left, int right)\n{\n    return left + right;\n}", formatted);
        Assert.DoesNotContain("CSharpWorkbenchSnippet", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatStatementsSnippetDoesNotLeakSyntheticMethodIndentation()
    {
        var source = "if(true){Console.WriteLine(\"x\");}";
        var options = CreateOptions();
        options.CSharpWrapping.PreserveSingleLineBlocks = false;
        options.CSharpWrapping.PreserveSingleLineStatements = false;
        var request = new CSharpFormattingRequest(
            source,
            CSharpFormattingKind.Snippet,
            options,
            snippetKind: CSharpSnippetKind.Statements);
        var formatted = await FormatAsync(request);

        Assert.Equal("if (true)\n{\n    Console.WriteLine(\"x\");\n}", formatted);
        Assert.DoesNotContain("CSharpWorkbenchMethod", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatDocumentAppliesFileLevelTextOptions()
    {
        var options = CreateOptions();
        options.LineEnding = "\r\n";
        options.InsertFinalNewline = true;
        options.TrimTrailingWhitespace = true;
        options.Charset = CSharpCharset.Utf8Bom;
        var source = "\uFEFFclass Demo { }  \n\n";

        var formatted = await FormatAsync(new CSharpFormattingRequest(source, CSharpFormattingKind.Document, options));

        Assert.Equal("\uFEFFclass Demo { }\r\n", formatted);
    }

    [Fact]
    public async Task FormatDocumentMapsRoslynFormattingOptions()
    {
        var options = CreateOptions();
        options.CSharpNewLines.BeforeOpenBrace = CSharpOpenBraceMode.None;
        options.CSharpSpacing.AroundBinaryOperators = CSharpBinaryOperatorSpacing.None;
        options.CSharpSpacing.AfterControlFlowKeyword = false;
        var source = "class Demo\n{\nvoid Run()\n{\nif (true)\n{\nvar value = left + right;\n}\n}\n}";

        var formatted = await FormatAsync(new CSharpFormattingRequest(source, CSharpFormattingKind.Document, options));

        Assert.Contains("class Demo {", formatted, StringComparison.Ordinal);
        Assert.Contains("if(true) {", formatted, StringComparison.Ordinal);
        Assert.Contains("left+right", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FormatDocumentReturnsWholeSourceReplacementForRecoverableSyntaxErrors()
    {
        var source = "class Demo{void Run( {";
        var result = await new CSharpRoslynFormatter().FormatAsync(
            new CSharpFormattingRequest(source, CSharpFormattingKind.Document, CreateOptions()));
        var change = Assert.Single(result.Changes);

        Assert.Equal(0, change.Span.Start);
        Assert.Equal(source.Length, change.Span.Length);
    }

    [Fact]
    public async Task FormatAsyncHonorsPreCancelledRequests()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CSharpRoslynFormatter().FormatAsync(
                new CSharpFormattingRequest("class Demo { }", CSharpFormattingKind.Document, CreateOptions()),
                cancellation.Token));
    }

    [Fact]
    public async Task FormatAsyncRejectsInvalidSpanAndConfiguration()
    {
        var invalidSpan = await Assert.ThrowsAsync<FormattingException>(() =>
            new CSharpRoslynFormatter().FormatAsync(
                new CSharpFormattingRequest(
                    "class Demo { }",
                    CSharpFormattingKind.Range,
                    CreateOptions(),
                    new CSharpTextSpan(20, 1))));
        var invalidOptions = CreateOptions();
        invalidOptions.Indentation.Size = 0;
        var invalidConfiguration = await Assert.ThrowsAsync<FormattingException>(() =>
            new CSharpRoslynFormatter().FormatAsync(
                new CSharpFormattingRequest("class Demo { }", CSharpFormattingKind.Document, invalidOptions)));

        Assert.Equal(FormattingErrorCode.InvalidSpan, invalidSpan.Code);
        Assert.Equal(FormattingErrorCode.InvalidConfiguration, invalidConfiguration.Code);
    }

    [Fact]
    public async Task FormatAsyncRejectsUnsupportedSnippetKind()
    {
        var exception = await Assert.ThrowsAsync<FormattingException>(() =>
            new CSharpRoslynFormatter().FormatAsync(
                new CSharpFormattingRequest(
                    "Work();",
                    CSharpFormattingKind.Snippet,
                    CreateOptions(),
                    snippetKind: (CSharpSnippetKind)999)));

        Assert.Equal(FormattingErrorCode.InvalidRequest, exception.Code);
    }

    [Fact]
    public async Task WorkbenchRulesCanTransformSyntaxTokensBeforeStandardFormatting()
    {
        var source = "class Demo{ }";
        var formatter = new CSharpRoslynFormatter(new[]
            { new RenameDemoRule() });
        var result = await formatter.FormatAsync(
            new CSharpFormattingRequest(source, CSharpFormattingKind.Document, CreateOptions()));
        var formatted = ApplyChanges(source, result.Changes);

        Assert.Equal("class Changed { }", formatted);
    }

    [Fact]
    public async Task FormatterSupportsConcurrentIndependentRequests()
    {
        var formatter = new CSharpRoslynFormatter();
        var tasks = Enumerable.Range(0, 8).Select(index =>
            {
                var source = $"class Demo{index}{{void Run(){{}}}}";
                return FormatAsync(
                    new CSharpFormattingRequest(source, CSharpFormattingKind.Document, CreateOptions()),
                    formatter);
        });

        var results = await Task.WhenAll(tasks);

        Assert.Equal(8, results.Length);
        Assert.All(results, result => Assert.Contains("void Run()", result, StringComparison.Ordinal));
    }

    [Fact]
    public void CoreInfoReportsVersionAndCapabilityBoundaries()
    {
        var info = FormattingCoreInfo.Current;

        Assert.Equal("formatting-core", info.BackendName);
        Assert.NotEqual("unknown", info.CoreVersion);
        Assert.NotEqual("unknown", info.RoslynVersion);
        Assert.True(info.Capabilities.FormatDocument);
        Assert.True(info.Capabilities.FormatRange);
        Assert.True(info.Capabilities.FormatSnippet);
        Assert.True(info.Capabilities.SupportsMaxLineLength);
        Assert.True(info.Capabilities.SupportsIndependentEventIndexerAndLocalFunctionBraceContexts);
        Assert.True(info.Capabilities.PreservesSourceOnFailure);
    }

    private static CSharpFormattingOptions CreateOptions()
    {
        return new CSharpFormattingOptions
        {
            Indentation = new IndentationOptions
            {
                Style = CSharpIndentationStyle.Space,
                Size = 4,
                TabWidth = 4,
            },
            CSharpIndentation = new CSharpIndentationOptions
            {
                IndentBlockContents = true,
                IndentBraces = false,
                IndentCaseContents = true,
                IndentSwitchLabels = true,
                IndentCaseContentsWhenBlock = true,
                IndentLabels = CSharpLabelIndentation.OneLessThanCurrent,
            },
            CSharpNewLines = new CSharpNewLineOptions
            {
                BeforeOpenBrace = CSharpOpenBraceMode.All,
                BeforeElse = true,
                BeforeCatch = true,
                BeforeFinally = true,
                BeforeMembersInObjectInitializers = true,
                BeforeMembersInAnonymousTypes = true,
                BetweenQueryExpressionClauses = true,
            },
            CSharpSpacing = new CSharpSpacingOptions
            {
                AfterControlFlowKeyword = true,
                AroundBinaryOperators = CSharpBinaryOperatorSpacing.BeforeAndAfter,
                AfterComma = true,
                BeforeComma = false,
                AfterForSemicolon = true,
                BeforeForSemicolon = false,
                AfterCast = false,
                BeforeInheritanceColon = true,
                AfterInheritanceColon = true,
            },
            CSharpWrapping = new CSharpWrappingOptions
            {
                PreserveSingleLineStatements = true,
                PreserveSingleLineBlocks = true,
            },
            LineEnding = "\n",
            InsertFinalNewline = false,
            TrimTrailingWhitespace = true,
            Charset = CSharpCharset.Utf8,
        };
    }

    private static async Task<string> FormatAsync(
        CSharpFormattingRequest request,
        CSharpRoslynFormatter? formatter = null)
    {
        var result = await (formatter ?? new CSharpRoslynFormatter()).FormatAsync(request);
        return ApplyChanges(request.Source, result.Changes);
    }

    private static string ApplyChanges(string source, IReadOnlyList<CSharpTextChange> changes)
    {
        var result = new StringBuilder(source);
        foreach (var change in changes.OrderByDescending(change => change.Span.Start))
        {
            result.Remove(change.Span.Start, change.Span.Length);
            result.Insert(change.Span.Start, change.NewText);
        }

        return result.ToString();
    }

    private sealed class RenameDemoRule : ICSharpWorkbenchFormattingRule
    {
        public SyntaxNode Apply(
            SyntaxNode root,
            TextSpan formattingSpan,
            CSharpFormattingOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = root.DescendantTokens(formattingSpan).First(candidate => candidate.ValueText == "Demo");
            var replacement = SyntaxFactory.Identifier(token.LeadingTrivia, "Changed", token.TrailingTrivia);
            return root.ReplaceToken(token, replacement);
        }
    }
}
