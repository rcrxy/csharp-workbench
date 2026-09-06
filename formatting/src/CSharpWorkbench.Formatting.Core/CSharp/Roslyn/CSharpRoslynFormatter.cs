using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using CSharpWorkbench.Formatting.Core.CSharp.Options;
using CSharpWorkbench.Formatting.Core.CSharp.Rules;
using CSharpWorkbench.Formatting.Core.Errors;

namespace CSharpWorkbench.Formatting.Core.CSharp.Roslyn;

public sealed class CSharpRoslynFormatter(IEnumerable<ICSharpWorkbenchFormattingRule>? workbenchRules = null)
{
    private readonly IReadOnlyList<ICSharpWorkbenchFormattingRule> _workbenchRules = workbenchRules?.ToArray() ?? [];

    public async Task<CSharpFormattingResult> FormatAsync(
        CSharpFormattingRequest request,
        CancellationToken cancellationToken = default)
    {
        CSharpFormattingRequestValidator.Validate(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await FormatCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FormattingException)
        {
            throw;
        }
        catch (Exception exception)when (exception is not OutOfMemoryException)
        {
            throw new FormattingException(
                FormattingErrorCode.FormattingFailure,
                "Roslyn failed to format the C# source.",
                exception);
        }
    }

    private async Task<CSharpFormattingResult> FormatCoreAsync(
        CSharpFormattingRequest request,
        CancellationToken cancellationToken)
    {
        var snippetContext = request.Kind == CSharpFormattingKind.Snippet
        ? SnippetFormattingContext.Create(request)
        : null;
        var parserSource = snippetContext?.ParserSource ?? request.Source;
        var formattingSpan = request.Kind switch
        {
            CSharpFormattingKind.Document => new TextSpan(0, parserSource.Length),
            CSharpFormattingKind.Range => ToRoslynSpan(request.Span!.Value),
            CSharpFormattingKind.Snippet => snippetContext!.FormattingSpan,
            _ => throw new FormattingException(
                FormattingErrorCode.InvalidRequest,
                $"Unsupported formatting kind: {request.Kind}."),
        };

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse, SourceCodeKind.Regular);
        var syntaxTree = Parse(parserSource, parseOptions, cancellationToken);
        var root = await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);

        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(
            ProjectInfo.Create(
                ProjectId.CreateNewId(),
                VersionStamp.Create(),
                "CSharpWorkbench.Formatting.Core",
                "CSharpWorkbench.Formatting.Core",
                LanguageNames.CSharp,
                parseOptions: parseOptions));
        var originalDocument = workspace.AddDocument(
            project.Id,
            "CSharpWorkbenchDocument.cs",
            SourceText.From(parserSource));
        var ruleRoot = ApplyWorkbenchRules(root, formattingSpan, request.Options, cancellationToken);
        var ruleDocument = originalDocument.WithSyntaxRoot(ruleRoot);
        var optionSet = RoslynFormattingOptionsMapper.Apply(workspace.Options, request.Options);
        var formattedDocument = request.Kind == CSharpFormattingKind.Range
        ? await Formatter.FormatAsync(ruleDocument, formattingSpan, optionSet, cancellationToken).ConfigureAwait(false)
        : await Formatter.FormatAsync(ruleDocument, optionSet, cancellationToken).ConfigureAwait(false);
        formattedDocument = ApplyIndependentOpenBraceCorrection(formattedDocument, request.Options, cancellationToken);
        var formattedText = await formattedDocument.GetTextAsync(cancellationToken).ConfigureAwait(false);

        return request.Kind switch
        {
            CSharpFormattingKind.Document => CreateReplacementResult(
                request.Source,
                DocumentTextNormalizer.NormalizeDocument(formattedText.ToString(), request.Options),
                new CSharpTextSpan(0, request.Source.Length)),
            CSharpFormattingKind.Range => await CreateRangeResultAsync(
                request,
                originalDocument,
                formattedDocument,
                cancellationToken).ConfigureAwait(false),
            CSharpFormattingKind.Snippet => CreateReplacementResult(
                request.Source,
                snippetContext!.Extract(formattedText.ToString(), request.Options.Indentation),
                new CSharpTextSpan(0, request.Source.Length)),
            _ => CSharpFormattingResult.Unchanged,
        };
    }

    private static SyntaxTree Parse(
        string source,
        CSharpParseOptions parseOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            return CSharpSyntaxTree.ParseText(source, parseOptions, cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)when (exception is not OutOfMemoryException)
        {
            throw new FormattingException(
                FormattingErrorCode.ParseFailure,
                "Roslyn failed to parse the C# source.",
                exception);
        }
    }

    private SyntaxNode ApplyWorkbenchRules(
        SyntaxNode root,
        TextSpan formattingSpan,
        CSharpFormattingOptions options,
        CancellationToken cancellationToken)
    {
        var currentRoot = root;
        foreach (var rule in _workbenchRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            currentRoot = rule.Apply(currentRoot, formattingSpan, options, cancellationToken)
            ?? throw new FormattingException(
                FormattingErrorCode.FormattingFailure,
                $"Workbench formatting rule {rule.GetType().FullName} returned no syntax root.");
        }

        if (formattingSpan.End > currentRoot.FullSpan.End)
        {
            throw new FormattingException(
                FormattingErrorCode.FormattingFailure,
            "A Workbench formatting rule produced a syntax tree shorter than the requested formatting span.");
        }

        return currentRoot;
    }

    private static async Task<CSharpFormattingResult> CreateRangeResultAsync(
        CSharpFormattingRequest request,
        Document originalDocument,
        Document formattedDocument,
        CancellationToken cancellationToken)
    {
        var targetSpan = request.Span!.Value;
        var targetEnd = targetSpan.End;
        var relativeChanges = new List<TextChange>();
        var textChanges = await formattedDocument
        .GetTextChangesAsync(originalDocument, cancellationToken)
        .ConfigureAwait(false);

        foreach (var change in textChanges)
        {
            var changeEnd = change.Span.End;
            if (change.Span.Start >= targetSpan.Start && changeEnd <= targetEnd)
            {
                relativeChanges.Add(
                    new TextChange(
                        new TextSpan(change.Span.Start - targetSpan.Start, change.Span.Length),
                        change.NewText ?? string.Empty));
                continue;
            }

            if (changeEnd <= targetSpan.Start || change.Span.Start >= targetEnd)
            {
                continue;
            }

            throw new FormattingException(
                FormattingErrorCode.FormattingFailure,
            "Roslyn returned a text change that crosses the requested range boundary.");
        }

        var originalSelection = request.Source.Substring(targetSpan.Start, targetSpan.Length);
        var formattedSelection = SourceText.From(originalSelection).WithChanges(relativeChanges).ToString();
        formattedSelection = DocumentTextNormalizer.NormalizeRange(formattedSelection, request.Options);

        return CreateReplacementResult(originalSelection, formattedSelection, targetSpan);
    }

    private static CSharpFormattingResult CreateReplacementResult(
        string originalText,
        string formattedText,
        CSharpTextSpan replacementSpan)
    {
        return string.Equals(originalText, formattedText, StringComparison.Ordinal)
        ? CSharpFormattingResult.Unchanged
        : new CSharpFormattingResult(new[]
            { new CSharpTextChange(replacementSpan, formattedText) });
    }

    private static TextSpan ToRoslynSpan(CSharpTextSpan span)
    {
        return new TextSpan(span.Start, span.Length);
    }

    private static Document ApplyIndependentOpenBraceCorrection(
        Document document,
        CSharpFormattingOptions options,
        CancellationToken cancellationToken)
    {
        if (options.CSharpNewLines.BeforeOpenBrace != CSharpOpenBraceMode.Selected)
        {
            return document;
        }

        var selected = options.CSharpNewLines.OpenBraceContexts;
        var needsLocalFunctionBrace = selected.Contains(CSharpOpenBraceContext.LocalFunctions);
        var needsEventBrace = selected.Contains(CSharpOpenBraceContext.Events);
        var needsIndexerBrace = selected.Contains(CSharpOpenBraceContext.Indexers);
        if (!needsLocalFunctionBrace && !needsEventBrace && !needsIndexerBrace)
        {
            return document;
        }

        var root = document.GetSyntaxRootAsync(cancellationToken).GetAwaiter().GetResult()
            ?? throw new FormattingException(
                FormattingErrorCode.FormattingFailure,
                "Roslyn returned no syntax root for the formatted document.");
        var source = root.ToFullString();
        var tokens = new List<SyntaxToken>();
        foreach (var node in root.DescendantNodes())
        {
            var brace = node switch
            {
                LocalFunctionStatementSyntax localFunction when needsLocalFunctionBrace =>
                    localFunction.Body?.OpenBraceToken,
                EventDeclarationSyntax eventDeclaration when needsEventBrace =>
                    eventDeclaration.AccessorList?.OpenBraceToken,
                IndexerDeclarationSyntax indexer when needsIndexerBrace =>
                    indexer.AccessorList?.OpenBraceToken,
                _ => null,
            };

            if (brace is SyntaxToken token && token.RawKind != 0 &&
                !token.LeadingTrivia.Any(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia)))
            {
                tokens.Add(token);
            }
        }

        if (tokens.Count == 0)
        {
            return document;
        }

        var correctedRoot = root.ReplaceTokens(tokens, (original, _) =>
            AddLineBreakBeforeBrace(original, source, options.LineEnding));
        return document.WithSyntaxRoot(correctedRoot);
    }

    private static SyntaxToken AddLineBreakBeforeBrace(
        SyntaxToken token,
        string source,
        string lineEnding)
    {
        var lineStart = source.LastIndexOf('\n', Math.Max(0, token.SpanStart - 1));
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        var indentationLength = 0;
        while (lineStart + indentationLength < source.Length &&
            (source[lineStart + indentationLength] == ' ' || source[lineStart + indentationLength] == '\t'))
        {
            indentationLength++;
        }

        var indentation = source.Substring(lineStart, indentationLength);
        var leadingTrivia = token.LeadingTrivia;
        while (leadingTrivia.Count > 0 && leadingTrivia[leadingTrivia.Count - 1].IsKind(SyntaxKind.WhitespaceTrivia))
        {
            leadingTrivia = leadingTrivia.RemoveAt(leadingTrivia.Count - 1);
        }

        leadingTrivia = leadingTrivia
            .Add(SyntaxFactory.EndOfLine(lineEnding))
            .Add(SyntaxFactory.Whitespace(indentation));
        return token.WithLeadingTrivia(leadingTrivia);
    }
}
