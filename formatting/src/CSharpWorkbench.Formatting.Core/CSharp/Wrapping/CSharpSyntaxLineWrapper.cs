using CSharpWorkbench.Formatting.Core.CSharp.Options;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpWorkbench.Formatting.Core.CSharp.Wrapping;

internal static class CSharpSyntaxLineWrapper
{
    public static string Wrap(
        string source,
        CSharpFormattingOptions options,
        CancellationToken cancellationToken)
    {
        if (options.MaxLineLength is not int maxLineLength)
        {
            return source;
        }

        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse, SourceCodeKind.Regular),
            cancellationToken: cancellationToken);
        var root = tree.GetRoot(cancellationToken);
        var candidates = CollectCandidates(root, source, options, cancellationToken);
        var changes = PlanChanges(source, candidates, maxLineLength, options, cancellationToken);
        return changes.Count == 0
            ? source
            : SourceText.From(source).WithChanges(changes).ToString();
    }

    private static IReadOnlyList<BreakCandidate> CollectCandidates(
        SyntaxNode root,
        string source,
        CSharpFormattingOptions options,
        CancellationToken cancellationToken)
    {
        var candidates = new List<BreakCandidate>();
        var seen = new HashSet<(int Start, int Length)>();

        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (node)
            {
                case ArgumentListSyntax argumentList:
                    AddSeparators(argumentList.Arguments.GetSeparators(), source, null, null, null, candidates, seen);
                    break;
                case BracketedArgumentListSyntax bracketedArgumentList:
                    AddSeparators(bracketedArgumentList.Arguments.GetSeparators(), source, null, null, null, candidates, seen);
                    break;
                case ParameterListSyntax parameterList:
                    AddSeparators(parameterList.Parameters.GetSeparators(), source, null, null, null, candidates, seen);
                    break;
                case BracketedParameterListSyntax bracketedParameterList:
                    AddSeparators(bracketedParameterList.Parameters.GetSeparators(), source, null, null, null, candidates, seen);
                    break;
                case InitializerExpressionSyntax initializer:
                    AddSeparators(
                        initializer.Expressions.GetSeparators(),
                        source,
                        GetInitializerBraceWhitespace(initializer, source, options.CSharpNewLines),
                        GetInitializerContentWhitespace(initializer, source, options.CSharpNewLines),
                        GetInitializerClosingWhitespace(initializer, source, options.CSharpNewLines),
                        candidates,
                        seen);
                    break;
                case BinaryExpressionSyntax binaryExpression:
                    AddBinaryCandidate(
                        binaryExpression.OperatorToken,
                        source,
                        options.CSharpWrapping.OperatorPlacement,
                        candidates,
                        seen);
                    break;
            }
        }

        return candidates;
    }

    private static void AddSeparators(
        IEnumerable<SyntaxToken> separators,
        string source,
        TextSpan? initializerBraceWhitespace,
        TextSpan? initializerContentWhitespace,
        TextSpan? initializerClosingWhitespace,
        ICollection<BreakCandidate> candidates,
        ISet<(int Start, int Length)> seen)
    {
        foreach (var separator in separators)
        {
            var nextToken = separator.GetNextToken();
            if (nextToken.RawKind == 0 || !TryGetHorizontalWhitespace(source, separator.Span.End, nextToken.SpanStart, out _))
            {
                continue;
            }

            AddCandidate(
                separator.Span.End,
                nextToken.SpanStart,
                nextToken.SpanStart,
                initializerBraceWhitespace,
                initializerContentWhitespace,
                initializerClosingWhitespace,
                candidates,
                seen);
        }
    }

    private static void AddBinaryCandidate(
        SyntaxToken operatorToken,
        string source,
        CSharpWrappedOperatorPlacement operatorPlacement,
        ICollection<BreakCandidate> candidates,
        ISet<(int Start, int Length)> seen)
    {
        var previousToken = operatorToken.GetPreviousToken();
        var nextToken = operatorToken.GetNextToken();
        if (previousToken.RawKind == 0 || nextToken.RawKind == 0)
        {
            return;
        }

        if (operatorPlacement == CSharpWrappedOperatorPlacement.BeginningOfLine)
        {
            if (TryGetHorizontalWhitespace(source, previousToken.Span.End, operatorToken.SpanStart, out _))
            {
                AddCandidate(
                    previousToken.Span.End,
                    operatorToken.SpanStart,
                    operatorToken.SpanStart,
                    null,
                    null,
                    null,
                    candidates,
                    seen);
            }

            return;
        }

        if (TryGetHorizontalWhitespace(source, operatorToken.Span.End, nextToken.SpanStart, out _))
        {
            AddCandidate(
                operatorToken.Span.End,
                nextToken.SpanStart,
                nextToken.SpanStart,
                null,
                null,
                null,
                candidates,
                seen);
        }
    }

    private static void AddCandidate(
        int whitespaceStart,
        int whitespaceEnd,
        int splitPosition,
        TextSpan? initializerBraceWhitespace,
        TextSpan? initializerContentWhitespace,
        TextSpan? initializerClosingWhitespace,
        ICollection<BreakCandidate> candidates,
        ISet<(int Start, int Length)> seen)
    {
        var key = (whitespaceStart, whitespaceEnd - whitespaceStart);
        if (seen.Add(key))
        {
            candidates.Add(new BreakCandidate(
                new TextSpan(whitespaceStart, whitespaceEnd - whitespaceStart),
                splitPosition,
                initializerBraceWhitespace,
                initializerContentWhitespace,
                initializerClosingWhitespace));
        }
    }

    private static IReadOnlyList<TextChange> PlanChanges(
        string source,
        IReadOnlyList<BreakCandidate> candidates,
        int maxLineLength,
        CSharpFormattingOptions options,
        CancellationToken cancellationToken)
    {
        var changes = new List<TextChange>();
        var lineStart = 0;
        while (lineStart <= source.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lineEnd = FindLineEnd(source, lineStart);
            var lineCandidates = candidates
                .Where(candidate => candidate.SplitPosition > lineStart && candidate.SplitPosition < lineEnd)
                .OrderBy(candidate => candidate.SplitPosition)
                .ToArray();
            var segmentStart = lineStart;
            var continuationIndent = GetLineIndentation(source, lineStart) + GetIndentUnit(options);
            var continuationWidth = VisualWidth(
                continuationIndent,
                0,
                continuationIndent.Length,
                options.Indentation.TabWidth);
            var prefixWidth = 0;

            while (segmentStart < lineEnd &&
                prefixWidth + VisualWidth(source, segmentStart, lineEnd, options.Indentation.TabWidth) > maxLineLength)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var available = lineCandidates
                    .Where(candidate => candidate.SplitPosition > segmentStart &&
                        !changes.Any(change => change.Span.Start == candidate.WhitespaceSpan.Start))
                    .ToArray();
                if (available.Length == 0)
                {
                    break;
                }

                var beforeLimit = available
                    .Where(candidate => prefixWidth + VisualWidth(
                        source,
                        segmentStart,
                        candidate.SplitPosition,
                        options.Indentation.TabWidth) <= maxLineLength)
                    .Select(candidate => (BreakCandidate?)candidate)
                    .LastOrDefault();
                var selected = beforeLimit.HasValue ? beforeLimit.Value : available[0];
                changes.Add(new TextChange(
                    selected.WhitespaceSpan,
                    options.LineEnding + continuationIndent));
                if (selected.InitializerBraceWhitespace is TextSpan braceWhitespace &&
                    !changes.Any(change => change.Span == braceWhitespace))
                {
                    changes.Add(new TextChange(
                        braceWhitespace,
                        options.LineEnding + GetLineIndentation(source, lineStart)));
                }

                if (selected.InitializerContentWhitespace is TextSpan contentWhitespace &&
                    !changes.Any(change => change.Span == contentWhitespace))
                {
                    changes.Add(new TextChange(
                        contentWhitespace,
                        options.LineEnding + continuationIndent));
                }

                if (selected.InitializerClosingWhitespace is TextSpan closingWhitespace &&
                    !changes.Any(change => change.Span == closingWhitespace))
                {
                    changes.Add(new TextChange(
                        closingWhitespace,
                        options.LineEnding + GetLineIndentation(source, lineStart)));
                }

                segmentStart = selected.SplitPosition;
                prefixWidth = continuationWidth;
            }

            if (lineEnd == source.Length)
            {
                break;
            }

            lineStart = lineEnd + GetLineEndingLength(source, lineEnd);
        }

        return changes.OrderBy(change => change.Span.Start).ToArray();
    }

    private static bool TryGetHorizontalWhitespace(string source, int start, int end, out string whitespace)
    {
        whitespace = start < end ? source.Substring(start, end - start) : string.Empty;
        return whitespace.All(character => character is ' ' or '\t');
    }

    private static TextSpan? GetInitializerBraceWhitespace(
        InitializerExpressionSyntax initializer,
        string source,
        CSharpNewLineOptions options)
    {
        var usesNewLine = options.BeforeOpenBrace == CSharpOpenBraceMode.All ||
            options.BeforeOpenBrace == CSharpOpenBraceMode.Selected &&
            options.OpenBraceContexts.Contains(CSharpOpenBraceContext.ObjectCollectionArrayInitializers);
        if (!usesNewLine)
        {
            return null;
        }

        var brace = initializer.OpenBraceToken;
        var previous = brace.GetPreviousToken();
        return previous.RawKind != 0 &&
            TryGetHorizontalWhitespace(source, previous.Span.End, brace.SpanStart, out _)
            ? new TextSpan(previous.Span.End, brace.SpanStart - previous.Span.End)
            : null;
    }

    private static TextSpan? GetInitializerContentWhitespace(
        InitializerExpressionSyntax initializer,
        string source,
        CSharpNewLineOptions options)
    {
        if (!options.BeforeMembersInObjectInitializers ||
            !initializer.IsKind(SyntaxKind.ObjectInitializerExpression) ||
            initializer.Expressions.Count == 0)
        {
            return null;
        }

        var brace = initializer.OpenBraceToken;
        var firstToken = initializer.Expressions[0].GetFirstToken();
        return TryGetHorizontalWhitespace(source, brace.Span.End, firstToken.SpanStart, out _)
            ? new TextSpan(brace.Span.End, firstToken.SpanStart - brace.Span.End)
            : null;
    }

    private static TextSpan? GetInitializerClosingWhitespace(
        InitializerExpressionSyntax initializer,
        string source,
        CSharpNewLineOptions options)
    {
        if (!options.BeforeMembersInObjectInitializers ||
            !initializer.IsKind(SyntaxKind.ObjectInitializerExpression) ||
            initializer.Expressions.Count == 0)
        {
            return null;
        }

        var lastToken = initializer.Expressions[initializer.Expressions.Count - 1].GetLastToken();
        var brace = initializer.CloseBraceToken;
        return TryGetHorizontalWhitespace(source, lastToken.Span.End, brace.SpanStart, out _)
            ? new TextSpan(lastToken.Span.End, brace.SpanStart - lastToken.Span.End)
            : null;
    }

    private static int FindLineEnd(string source, int lineStart)
    {
        var index = source.IndexOfAny(new[] { '\r', '\n' }, lineStart);
        return index < 0 ? source.Length : index;
    }

    private static int GetLineEndingLength(string source, int lineEnd)
    {
        return source[lineEnd] == '\r' && lineEnd + 1 < source.Length && source[lineEnd + 1] == '\n' ? 2 : 1;
    }

    private static int VisualWidth(string source, int start, int end, int tabWidth)
    {
        var column = 0;
        for (var index = start; index < end; index++)
        {
            column = source[index] == '\t'
                ? column + tabWidth - column % tabWidth
                : column + 1;
        }

        return column;
    }

    private static string GetLineIndentation(string source, int lineStart)
    {
        var index = lineStart;
        while (index < source.Length && source[index] is ' ' or '\t')
        {
            index++;
        }

        return source.Substring(lineStart, index - lineStart);
    }

    private static string GetIndentUnit(CSharpFormattingOptions options)
    {
        return options.Indentation.Style == CSharpIndentationStyle.Tab
            ? "\t"
            : new string(' ', options.Indentation.Size);
    }

    private readonly struct BreakCandidate
    {
        public BreakCandidate(
            TextSpan whitespaceSpan,
            int splitPosition,
            TextSpan? initializerBraceWhitespace,
            TextSpan? initializerContentWhitespace,
            TextSpan? initializerClosingWhitespace)
        {
            WhitespaceSpan = whitespaceSpan;
            SplitPosition = splitPosition;
            InitializerBraceWhitespace = initializerBraceWhitespace;
            InitializerContentWhitespace = initializerContentWhitespace;
            InitializerClosingWhitespace = initializerClosingWhitespace;
        }

        public TextSpan WhitespaceSpan { get; }

        public int SplitPosition { get; }

        public TextSpan? InitializerBraceWhitespace { get; }

        public TextSpan? InitializerContentWhitespace { get; }

        public TextSpan? InitializerClosingWhitespace { get; }
    }
}
