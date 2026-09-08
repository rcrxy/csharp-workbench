namespace CSharpWorkbench.Formatting.Core.Razor;

internal static class RazorEmbeddedCSharpSpanResolver
{
    public static bool TryGetAttributeCSharpSpan(
        string source,
        RazorAttributeMetadata attribute,
        out RazorSourceSpan span)
    {
        span = default;
        if (attribute.ValueSpan is not RazorSourceSpan valueSpan || valueSpan.Length == 0)
        {
            return false;
        }

        var name = source.Substring(attribute.NameSpan.Start, attribute.NameSpan.Length);
        var valueStart = valueSpan.Start;
        var valueEnd = valueSpan.End;
        if (source[valueStart] is '"' or '\'')
        {
            valueStart++;
            valueEnd--;
        }
        if (valueEnd <= valueStart)
        {
            return false;
        }

        if (source[valueStart] == '@')
        {
            if (valueStart + 1 < valueEnd && source[valueStart + 1] == '(' && source[valueEnd - 1] == ')')
            {
                span = new RazorSourceSpan(valueStart + 2, valueEnd - valueStart - 3);
            }
            else
            {
                span = new RazorSourceSpan(valueStart + 1, valueEnd - valueStart - 1);
            }

            return span.Length > 0;
        }

        if (name.StartsWith("@", StringComparison.Ordinal))
        {
            span = new RazorSourceSpan(valueStart, valueEnd - valueStart);
            return true;
        }

        return false;
    }
}
