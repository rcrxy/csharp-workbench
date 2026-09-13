using CSharpWorkbench.Formatting.Core.Razor;
using Xunit;

namespace CSharpWorkbench.Formatting.Core.Tests;

public sealed class RazorRangeFormattingTargetResolverTests
{
    [Fact]
    public void EmbeddedAttributeCSharpTakesPriorityOverWholeTag()
    {
        const string source = "<Widget Value=\"@(left+right)\" Text=\"left+right\" />";
        var requested = SpanOf(source, "left+right", occurrence: 0);

        Assert.True(TryResolve(source, requested, out var target));
        Assert.Equal(RazorRangeFormattingTargetKind.EmbeddedCSharp, target.Kind);
        Assert.Equal("left+right", Slice(source, target.EffectiveSpan));
    }

    [Fact]
    public void PlainAttributeSelectionExpandsToWholeTag()
    {
        const string source = "<Widget Value=\"1\" Text=\"value\" />";
        var requested = SpanOf(source, "Text");

        Assert.True(TryResolve(source, requested, out var target));
        Assert.Equal(RazorRangeFormattingTargetKind.Tag, target.Kind);
        Assert.Equal(source, Slice(source, target.EffectiveSpan));
    }

    [Fact]
    public void TextOnlySelectionIsUnchanged()
    {
        const string source = "<span>Value</span>";

        Assert.False(TryResolve(source, SpanOf(source, "Value"), out _));
    }

    [Fact]
    public void CompleteElementSelectionResolvesMarkupStructure()
    {
        const string source = "<div><span>Value</span></div>";
        var requested = SpanOf(source, "<span>Value</span>");

        Assert.True(TryResolve(source, requested, out var target));
        Assert.Equal(RazorRangeFormattingTargetKind.MarkupStructure, target.Kind);
        Assert.Equal("<span>Value</span>", Slice(source, target.EffectiveSpan));
        Assert.Equal(1, target.InitialMarkupDepth);
    }

    [Fact]
    public void MultipleSiblingSelectionDoesNotExpandToParent()
    {
        const string source = "<Parent><A /><B /><C /></Parent>";
        var start = source.IndexOf("<A />", StringComparison.Ordinal);
        var end = source.IndexOf("<C />", StringComparison.Ordinal);
        var requested = new RazorSourceSpan(start, end - start);

        Assert.True(TryResolve(source, requested, out var target));
        Assert.Equal("<A /><B />", Slice(source, target.EffectiveSpan));
        Assert.Equal(1, target.InitialMarkupDepth);
    }

    [Fact]
    public void HardProtectedAndMalformedDocumentsAreUnresolved()
    {
        const string script = "<script>const value = \"<Tag>\";</script>";
        const string malformed = "<div><span></div>";

        Assert.False(TryResolve(script, SpanOf(script, "value"), out _));
        Assert.False(TryResolve(malformed, SpanOf(malformed, "<span>"), out _));
    }

    [Fact]
    public void InlineControlHeaderSelectionResolvesEmbeddedCSharp()
    {
        const string source = "before @if(a==1) { @: x } after";

        Assert.True(TryResolve(source, SpanOf(source, "a==1"), out var target));
        Assert.Equal(RazorRangeFormattingTargetKind.EmbeddedCSharp, target.Kind);
        Assert.Equal("if(a==1) ", Slice(source, target.EffectiveSpan));
    }

    [Fact]
    public void CompleteInlineControlSelectionResolvesStructuralLeafWithoutAdjacentText()
    {
        const string source = "before @if(a==1) { @: x } after";

        Assert.True(TryResolve(source, SpanOf(source, "@if(a==1) { @: x }"), out var target));
        Assert.Equal(RazorRangeFormattingTargetKind.StructuralLeaf, target.Kind);
        Assert.Equal("@if(a==1) { @: x }", Slice(source, target.EffectiveSpan));
    }

    [Fact]
    public void ContainingElementSelectionIncludesInlineControlAndAdjacentText()
    {
        const string source = "<div>before @if(a==1) { @: x } after</div>";

        Assert.True(TryResolve(source, new RazorSourceSpan(0, source.Length), out var target));
        Assert.Equal(RazorRangeFormattingTargetKind.MarkupStructure, target.Kind);
        Assert.Equal(source, Slice(source, target.EffectiveSpan));
    }

    private static bool TryResolve(
        string source,
        RazorSourceSpan requested,
        out RazorRangeFormattingTarget target)
    {
        var document = new RazorDocumentScanner().Scan(
            source,
            RazorDocumentKind.Component,
            CancellationToken.None);
        return RazorRangeFormattingTargetResolver.TryResolve(source, document, requested, out target);
    }

    private static RazorSourceSpan SpanOf(string source, string value, int occurrence = 0)
    {
        var start = -1;
        for (var index = 0; index <= occurrence; index++)
        {
            start = source.IndexOf(value, start + 1, StringComparison.Ordinal);
        }

        return new RazorSourceSpan(start, value.Length);
    }

    private static string Slice(string source, RazorSourceSpan span)
    {
        return source.Substring(span.Start, span.Length);
    }
}
