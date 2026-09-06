using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using CSharpWorkbench.Formatting.Core.CSharp.Options;

namespace CSharpWorkbench.Formatting.Core;

public sealed class FormattingCoreCapabilities
{
    internal FormattingCoreCapabilities() { }

    public bool FormatDocument => true;

    public bool FormatRange => true;

    public bool FormatSnippet => true;

    public IReadOnlyList<CSharpSnippetKind> SnippetKinds { get; } = [CSharpSnippetKind.TypeMembers, CSharpSnippetKind.Statements];

    public bool SupportsMaxLineLength => false;

    public bool SupportsEncodingConversion => false;

    public bool SupportsIndependentEventIndexerAndLocalFunctionBraceContexts => true;

    public bool PreservesSourceOnFailure => true;
}

public sealed class FormattingCoreInfo
{
    private FormattingCoreInfo() { }

    public static FormattingCoreInfo Current { get; } = new();

    public string BackendName => "formatting-core";

    public string CoreVersion => typeof(FormattingCoreInfo).Assembly.GetName().Version?.ToString() ?? "unknown";

    public string RoslynVersion => typeof(CSharpSyntaxTree).Assembly.GetName().Version?.ToString() ?? "unknown";

    public FormattingCoreCapabilities Capabilities { get; } = new();
}
