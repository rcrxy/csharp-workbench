using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using CSharpWorkbench.Formatting.Core.CSharp.Options;

namespace CSharpWorkbench.Formatting.Core.CSharp.Rules;

public interface ICSharpWorkbenchFormattingRule
{
    SyntaxNode Apply(
        SyntaxNode root,
        TextSpan formattingSpan,
        CSharpFormattingOptions options,
        CancellationToken cancellationToken);
}
