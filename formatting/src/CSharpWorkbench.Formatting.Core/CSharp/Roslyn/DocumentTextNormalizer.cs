using System.Text.RegularExpressions;
using CSharpWorkbench.Formatting.Core.CSharp.Options;

namespace CSharpWorkbench.Formatting.Core.CSharp.Roslyn;

internal static class DocumentTextNormalizer
{
    private static readonly Regex LineEndingPattern = new("\r\n|\n|\r", RegexOptions.Compiled);
    private static readonly Regex TrailingWhitespacePattern = new("[ \\t]+(?=\r\n|\n|\r|$)", RegexOptions.Compiled);
    private static readonly Regex FinalLineEndingsPattern = new("(?:\r\n|\n|\r)+$", RegexOptions.Compiled);

    public static string NormalizeDocument(string source, CSharpFormattingOptions options)
    {
        var withoutBom = source.Length > 0 && source[0] == '\uFEFF' ? source.Substring(1) : source;
        var normalized = LineEndingPattern.Replace(withoutBom, options.LineEnding);
        var withoutTrailingWhitespace = options.TrimTrailingWhitespace ? TrimTrailingWhitespace(normalized) : normalized;
        var withoutFinalLineEndings = FinalLineEndingsPattern.Replace(withoutTrailingWhitespace, string.Empty);
        var withConfiguredFinalNewline = options.InsertFinalNewline
        ? withoutFinalLineEndings + options.LineEnding
        : withoutFinalLineEndings;

        return options.Charset == CSharpCharset.Utf8Bom
        ? "\uFEFF" + withConfiguredFinalNewline
        : withConfiguredFinalNewline;
    }

    public static string NormalizeRange(string source, CSharpFormattingOptions options)
    {
        return options.TrimTrailingWhitespace ? TrimTrailingWhitespace(source) : source;
    }

    private static string TrimTrailingWhitespace(string source)
    {
        return TrailingWhitespacePattern.Replace(source, string.Empty);
    }
}
