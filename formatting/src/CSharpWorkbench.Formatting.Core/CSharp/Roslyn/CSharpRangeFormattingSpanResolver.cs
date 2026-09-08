using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CSharpWorkbench.Formatting.Core.CSharp.Roslyn;

internal static class CSharpRangeFormattingSpanResolver
{
    public static bool TryResolve(
        SyntaxNode root,
        string source,
        TextSpan requestedSpan,
        out TextSpan effectiveSpan)
    {
        effectiveSpan = default;
        if (requestedSpan.IsEmpty || IsWhitespaceOnly(source, requestedSpan))
        {
            return false;
        }

        var intersectingTokens = root
            .DescendantTokens(descendIntoTrivia: false)
            .Where(token => token.Span.IntersectsWith(requestedSpan))
            .ToArray();
        if (intersectingTokens.Length == 0)
        {
            return false;
        }

        var firstToken = intersectingTokens[0];
        var lastToken = intersectingTokens[intersectingTokens.Length - 1];
        var tokenSpan = TextSpan.FromBounds(firstToken.SpanStart, lastToken.Span.End);
        if (firstToken == lastToken)
        {
            effectiveSpan = tokenSpan;
            return true;
        }

        var commonNode = FindSmallestCommonNode(firstToken.Parent, lastToken.Parent);
        if (commonNode is not null && !IsBroadContainer(commonNode))
        {
            effectiveSpan = commonNode.Span;
            return true;
        }

        var firstStatement = firstToken.Parent?.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        var lastStatement = lastToken.Parent?.AncestorsAndSelf().OfType<StatementSyntax>().FirstOrDefault();
        if (firstStatement is not null &&
            lastStatement is not null &&
            ReferenceEquals(firstStatement.Parent, lastStatement.Parent))
        {
            effectiveSpan = TextSpan.FromBounds(firstStatement.SpanStart, lastStatement.Span.End);
            return true;
        }

        effectiveSpan = tokenSpan;
        return true;
    }

    private static bool IsWhitespaceOnly(string source, TextSpan span)
    {
        for (var index = span.Start; index < span.End; index++)
        {
            if (!char.IsWhiteSpace(source[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static SyntaxNode? FindSmallestCommonNode(SyntaxNode? first, SyntaxNode? last)
    {
        if (first is null || last is null)
        {
            return null;
        }

        var lastAncestors = new HashSet<SyntaxNode>(last.AncestorsAndSelf());
        return first.AncestorsAndSelf().FirstOrDefault(lastAncestors.Contains);
    }

    private static bool IsBroadContainer(SyntaxNode node)
    {
        return node is CompilationUnitSyntax or
            BaseNamespaceDeclarationSyntax or
            BaseTypeDeclarationSyntax or
            MemberDeclarationSyntax or
            BlockSyntax;
    }
}
