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

    private static string Slice(string source, RazorSourceSpan span)
    {
        return source.Substring(span.Start, span.Length);
    }
}
