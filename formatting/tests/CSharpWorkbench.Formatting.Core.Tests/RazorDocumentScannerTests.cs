using CSharpWorkbench.Formatting.Core.Razor;
using Xunit;

namespace CSharpWorkbench.Formatting.Core.Tests;

public sealed class RazorDocumentScannerTests
{
    [Fact]
    public void CapturesBooleanUnquotedAndDirectiveAttributesWithSourceSpans()
    {
        const string source = "<input disabled value=test @onclick=\"Handle\" @bind-Value=\"Value\" @attributes=\"Attributes\">";
        var model = new RazorDocumentScanner().Scan(source, RazorDocumentKind.Component, default);
        var tag = Assert.Single(model.Regions).Tag!;

        Assert.True(model.IsReliable);
        Assert.True(tag.AttributesReliable);
        Assert.Equal("input", Slice(source, tag.NameSpan));
        Assert.Equal(
            new[] { "disabled", "value", "@onclick", "@bind-Value", "@attributes" },
            tag.Attributes.Select(attribute => Slice(source, attribute.NameSpan)));
        Assert.Null(tag.Attributes[0].EqualsSpan);
        Assert.Null(tag.Attributes[0].ValueSpan);
        Assert.Equal("test", Slice(source, tag.Attributes[1].ValueSpan!.Value));
        Assert.Equal("\"Handle\"", Slice(source, tag.Attributes[2].ValueSpan!.Value));
    }

    [Fact]
    public void CapturesNestedRazorExpressionValuesWithoutChangingTheirSpans()
    {
        const string source = "<Widget Click=\"@(() => Save(item))\" Visible=\"@(item is { Enabled: true })\" Value=\"@(new Item { Name = \"A\" })\" />";
        var model = new RazorDocumentScanner().Scan(source, RazorDocumentKind.Component, default);
        var tag = Assert.Single(model.Regions).Tag!;

        Assert.True(model.IsReliable);
        Assert.True(tag.AttributesReliable);
        Assert.Equal(3, tag.Attributes.Count);
        Assert.Equal("\"@(() => Save(item))\"", Slice(source, tag.Attributes[0].ValueSpan!.Value));
        Assert.Equal("\"@(item is { Enabled: true })\"", Slice(source, tag.Attributes[1].ValueSpan!.Value));
        Assert.Equal("\"@(new Item { Name = \"A\" })\"", Slice(source, tag.Attributes[2].ValueSpan!.Value));
    }

    [Fact]
    public void MarksOnlyTheTagAttributesUnreliableWhenValueIsMissing()
    {
        const string source = "<div><Widget Value=></Widget><span>After</span></div>";
        var model = new RazorDocumentScanner().Scan(source, RazorDocumentKind.Component, default);
        var widget = model.Regions.Single(region =>
            region.Kind == RazorRegionKind.StartTag && region.Name == "Widget");

        Assert.True(model.IsReliable);
        Assert.False(widget.Tag!.AttributesReliable);
    }

    [Fact]
    public void CapturesCodeBlockBodyAndBraceSpans()
    {
        const string source = "@code {\nint value=1;\n}\n@{\nDo();\n}";
        var model = new RazorDocumentScanner().Scan(source, RazorDocumentKind.Component, default);
        var blocks = model.Regions.Where(region => region.CodeBlock is not null).ToArray();

        Assert.Equal(2, blocks.Length);
        Assert.Equal(RazorCodeBlockKind.Code, blocks[0].CodeBlock!.Kind);
        Assert.Equal("\nint value=1;\n", Slice(source, blocks[0].CodeBlock!.BodySpan));
        Assert.Equal("{", Slice(source, blocks[0].CodeBlock!.OpenBraceSpan));
        Assert.Equal("}", Slice(source, blocks[0].CodeBlock!.CloseBraceSpan));
        Assert.Equal(RazorCodeBlockKind.Explicit, blocks[1].CodeBlock!.Kind);
    }

    [Fact]
    public void DistinguishesControlExpressionStatementAndRawProtectedRegions()
    {
        const string source = "@if(enabled) { count++; }\n<script>left+right</script>\n<span>@(left+right)</span>";
        var model = new RazorDocumentScanner().Scan(source, RazorDocumentKind.Component, default);
        var control = model.Regions.Single(region => region.Control is not null).Control!;
        var protectedKinds = model.Regions
            .Where(region => region.Protected is not null)
            .Select(region => region.Protected!.Kind)
            .ToArray();

        Assert.Equal(RazorControlKind.If, control.Kind);
        Assert.True(control.IsInlineComplete);
        Assert.Equal("if(enabled) ", Slice(source, control.CSharpHeaderSpan!.Value));
        Assert.Contains(RazorProtectedKind.ScriptStyle, protectedKinds);
        Assert.Contains(RazorProtectedKind.RazorExpression, protectedKinds);
    }

    [Fact]
    public void CapturesCompleteInlineControlAsDedicatedRegion()
    {
        const string source = "something @if(a == 1) { @: SomeText } something";
        var model = new RazorDocumentScanner().Scan(source, RazorDocumentKind.Component, default);

        Assert.True(model.IsReliable);
        Assert.Equal(
            new[] { RazorRegionKind.Text, RazorRegionKind.InlineControl, RazorRegionKind.Text },
            model.Regions.Select(region => region.Kind));
        var controlRegion = model.Regions[1];
        Assert.Equal("@if(a == 1) { @: SomeText }", Slice(source, controlRegion.Span));
        Assert.Equal(RazorControlKind.If, controlRegion.Control!.Kind);
        Assert.Equal("if(a == 1) ", Slice(source, controlRegion.Control.CSharpHeaderSpan!.Value));
        Assert.DoesNotContain(
            model.Regions,
            region => region.Protected?.Kind == RazorProtectedKind.RazorExpression);
    }

    [Theory]
    [InlineData("for", "var i = 0; i < 1; i++")]
    [InlineData("foreach", "var item in items")]
    [InlineData("while", "ready")]
    [InlineData("switch", "value")]
    [InlineData("using", "resource")]
    [InlineData("lock", "gate")]
    public void CapturesSupportedInlineControls(string keyword, string header)
    {
        var source = $"before @{keyword} ({header}) {{ @: body }} after";
        var model = new RazorDocumentScanner().Scan(source, RazorDocumentKind.Component, default);

        Assert.True(model.IsReliable);
        Assert.Contains(model.Regions, region => region.Kind == RazorRegionKind.InlineControl);
    }

    [Theory]
    [InlineData("hello @Model.Name")]
    [InlineData("hello @GetValue()")]
    [InlineData("hello @(left+right)")]
    [InlineData("hello @ifModel")]
    [InlineData("hello @foreachItem")]
    [InlineData("hello @If(value)")]
    public void OrdinaryExpressionsAndIdentifierPrefixesAreNotInlineControls(string source)
    {
        var model = new RazorDocumentScanner().Scan(source, RazorDocumentKind.Component, default);

        Assert.True(model.IsReliable);
        Assert.DoesNotContain(model.Regions, region => region.Kind == RazorRegionKind.InlineControl);
        Assert.Contains(
            model.Regions,
            region => region.Protected?.Kind == RazorProtectedKind.RazorExpression);
    }

    [Fact]
    public void InlineControlBlockIgnoresQuotedAndNestedMarkupBraces()
    {
        const string source = "before @if (ok) { var a = \"{ }\"; var b = @\"{ }\"; var c = \"\"\"{ }\"\"\"; <span data-value=\"@(() => new Item { Name = \"A\" })\">{literal}</span> } after";
        var model = new RazorDocumentScanner().Scan(source, RazorDocumentKind.Component, default);

        Assert.True(model.IsReliable);
        Assert.Equal(RazorRegionKind.InlineControl, model.Regions[1].Kind);
        Assert.EndsWith("</span> }", Slice(source, model.Regions[1].Span), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("before @if (ok) { @: yes } else { @: no } after")]
    [InlineData("before @if (ok) {\n@: yes\n} after")]
    [InlineData("before @try { @: yes } after")]
    [InlineData("before @do { @: yes } while (ok); after")]
    public void UnsafeInlineControlCandidatesMakeDocumentUnreliable(string source)
    {
        var model = new RazorDocumentScanner().Scan(source, RazorDocumentKind.Component, default);

        Assert.False(model.IsReliable);
        Assert.DoesNotContain(
            model.Regions,
            region => region.Protected?.Kind == RazorProtectedKind.RazorExpression);
    }

    [Fact]
    public void CommentsAndScriptDoNotProduceInlineControls()
    {
        const string source = "@* before @if(a) { } after *@\n<!-- before @if(a) { } after -->\n<script>const text = \"@if(a) { }\";</script>";
        var model = new RazorDocumentScanner().Scan(source, RazorDocumentKind.Component, default);

        Assert.True(model.IsReliable);
        Assert.DoesNotContain(model.Regions, region => region.Kind == RazorRegionKind.InlineControl);
    }

    private static string Slice(string source, RazorSourceSpan span)
    {
        return source.Substring(span.Start, span.Length);
    }
}
