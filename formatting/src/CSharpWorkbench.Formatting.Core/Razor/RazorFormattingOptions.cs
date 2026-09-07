namespace CSharpWorkbench.Formatting.Core.Razor;

internal enum RazorAttributeStyle
{
    OnSingleLine,
    FirstAttributeOnSingleLine,
    OnDifferentLines,
    DoNotTouch,
}

internal enum RazorAttributeWrapPolicy
{
    Off,
    Normal,
    OnEveryItem,
    SplitIntoLines,
}

internal enum RazorAttributeIndent
{
    SingleIndent,
    DoubleIndent,
    AlignByFirstAttribute,
}

internal enum RazorExtraSpaces
{
    RemoveAll,
    LeaveTabs,
    LeaveMultiple,
    LeaveAll,
}

internal sealed class RazorMarkupFormattingOptions
{
    public bool SpacesAroundAttributeEquals { get; set; }

    public bool SpaceAfterLastAttribute { get; set; }

    public bool SpaceBeforeSelfClosing { get; set; } = true;

    public RazorAttributeStyle AttributeStyle { get; set; } = RazorAttributeStyle.OnSingleLine;

    public RazorAttributeWrapPolicy AttributeWrap { get; set; } = RazorAttributeWrapPolicy.Off;

    public RazorAttributeIndent AttributeIndent { get; set; } = RazorAttributeIndent.SingleIndent;

    public int MaxBlankLinesBetweenTags { get; set; } = 1;

    public bool LineBreakBeforeAllElements { get; set; }

    public bool LineBreakBeforeMultilineElements { get; set; } = true;

    public bool LineBreaksInsideMultilineElements { get; set; } = true;

    public bool LineBreaksInsideElementsWithChildElements { get; set; } = true;

    public HashSet<string> NoIndentInsideElements { get; set; } =
        new HashSet<string>(new[] { "pre", "textarea" }, StringComparer.OrdinalIgnoreCase);

    public HashSet<string> PreserveSpacesInsideTags { get; set; } =
        new HashSet<string>(new[] { "pre", "textarea" }, StringComparer.OrdinalIgnoreCase);

    public RazorExtraSpaces ExtraSpaces { get; set; } = RazorExtraSpaces.RemoveAll;
}

internal sealed class RazorFormattingOptions
{
    public bool UseTabs { get; set; }

    public int IndentSize { get; set; } = 4;

    public int TabWidth { get; set; } = 4;

    public int? MaxLineLength { get; set; } = 120;

    public string LineEnding { get; set; } = "\n";

    public bool InsertFinalNewline { get; set; }

    public bool TrimTrailingWhitespace { get; set; }

    public RazorMarkupFormattingOptions Markup { get; } = new();
}
