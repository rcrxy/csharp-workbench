using System.Text;
using Microsoft.CodeAnalysis.Text;
using CSharpWorkbench.Formatting.Core.CSharp.Options;
using CSharpWorkbench.Formatting.Core.Errors;

namespace CSharpWorkbench.Formatting.Core.CSharp.Roslyn;

internal sealed class SnippetFormattingContext
{
    private const string MarkerPrefix = "__CSharpWorkbenchSnippet";

    private SnippetFormattingContext(
        string parserSource,
        TextSpan formattingSpan,
        string startMarker,
        string endMarker,
        int wrapperDepth)
    {
        ParserSource = parserSource;
        FormattingSpan = formattingSpan;
        StartMarker = startMarker;
        EndMarker = endMarker;
        WrapperDepth = wrapperDepth;
    }

    public string ParserSource { get; }

    public TextSpan FormattingSpan { get; }

    private string StartMarker { get; }

    private string EndMarker { get; }

    private int WrapperDepth { get; }

    public static SnippetFormattingContext Create(CSharpFormattingRequest request)
    {
        var markerSuffix = 0;
        string startMarker;
        string endMarker;
        do
        {
            startMarker = $"/*{MarkerPrefix}Start{markerSuffix}*/";
            endMarker = $"/*{MarkerPrefix}End{markerSuffix}*/";
            markerSuffix++;
        }
        while (request.Source.Contains(startMarker, StringComparison.Ordinal) ||
            request.Source.Contains(endMarker, StringComparison.Ordinal));

        var lineEnding = request.Options.LineEnding;
        var wrapperDepth = request.SnippetKind == CSharpSnippetKind.TypeMembers ? 1 : 2;
        var prefix = request.SnippetKind == CSharpSnippetKind.TypeMembers
        ? $"internal sealed class __CSharpWorkbenchSnippet{lineEnding}{{{lineEnding}{startMarker}{lineEnding}"
        : $"internal sealed class __CSharpWorkbenchSnippet{lineEnding}{{{lineEnding}" +
        $"void __CSharpWorkbenchMethod(){lineEnding}{{{lineEnding}{startMarker}{lineEnding}";
        var suffix = request.SnippetKind == CSharpSnippetKind.TypeMembers
        ? $"{lineEnding}{endMarker}{lineEnding}}}{lineEnding}"
        : $"{lineEnding}{endMarker}{lineEnding}}}{lineEnding}}}{lineEnding}";
        var parserSource = prefix + request.Source + suffix;

        return new SnippetFormattingContext(
            parserSource,
            new TextSpan(prefix.Length, request.Source.Length),
            startMarker,
            endMarker,
            wrapperDepth);
    }

    public string Extract(string formattedParserSource, IndentationOptions indentation)
    {
        var startMarkerIndex = formattedParserSource.IndexOf(StartMarker, StringComparison.Ordinal);
        var endMarkerIndex = formattedParserSource.IndexOf(EndMarker, StringComparison.Ordinal);
        if (startMarkerIndex < 0 || endMarkerIndex < startMarkerIndex)
        {
            throw new FormattingException(
                FormattingErrorCode.FormattingFailure,
            "Roslyn formatting removed or reordered the snippet boundary markers.");
        }

        var bodyStart = startMarkerIndex + StartMarker.Length;
        var body = formattedParserSource.Substring(bodyStart, endMarkerIndex - bodyStart);
        body = RemoveLeadingSeparatorLineEnding(body);
        body = RemoveTrailingSeparatorLineEnding(body);

        return RemoveWrapperIndentation(body, indentation, WrapperDepth);
    }

    private static string RemoveLeadingSeparatorLineEnding(string source)
    {
        if (source.StartsWith("\r\n", StringComparison.Ordinal))
        {
            return source.Substring(2);
        }

        return source.StartsWith("\n", StringComparison.Ordinal) || source.StartsWith("\r", StringComparison.Ordinal)
        ? source.Substring(1)
        : source;
    }

    private static string RemoveTrailingSeparatorLineEnding(string source)
    {
        var end = source.Length;
        while (end > 0 && (source[end - 1] == ' ' || source[end - 1] == '\t'))
        {
            end--;
        }

        if (end >= 2 && source[end - 2] == '\r' && source[end - 1] == '\n')
        {
            end -= 2;
        }
        else if (end > 0 && (source[end - 1] == '\n' || source[end - 1] == '\r'))
        {
            end--;
        }

        return source.Substring(0, end);
    }

    private static string RemoveWrapperIndentation(string source, IndentationOptions indentation, int wrapperDepth)
    {
        var result = new StringBuilder(source.Length);
        var lineStart = 0;

        while (lineStart < source.Length)
        {
            var lineEnd = lineStart;
            while (lineEnd < source.Length && source[lineEnd] is not '\r' and not '\n')
            {
                lineEnd++;
            }

            AppendUnindentedLine(result, source, lineStart, lineEnd, indentation, wrapperDepth);

            if (lineEnd < source.Length)
            {
                if (source[lineEnd] == '\r' && lineEnd + 1 < source.Length && source[lineEnd + 1] == '\n')
                {
                    result.Append("\r\n");
                    lineStart = lineEnd + 2;
                }
                else
                {
                    result.Append(source[lineEnd]);
                    lineStart = lineEnd + 1;
                }
            }
            else
            {
                lineStart = lineEnd;
            }
        }

        return result.ToString();
    }

    private static void AppendUnindentedLine(
        StringBuilder result,
        string source,
        int lineStart,
        int lineEnd,
        IndentationOptions indentation,
        int wrapperDepth)
    {
        var contentStart = lineStart;
        if (indentation.Style == CSharpIndentationStyle.Tab)
        {
            var tabsToRemove = wrapperDepth;
            while (contentStart < lineEnd && tabsToRemove > 0 && source[contentStart] == '\t')
            {
                contentStart++;
                tabsToRemove--;
            }
        }
        else
        {
            var spacesToRemove = indentation.Size * wrapperDepth;
            while (contentStart < lineEnd && spacesToRemove > 0 && source[contentStart] == ' ')
            {
                contentStart++;
                spacesToRemove--;
            }
        }

        result.Append(source, contentStart, lineEnd - contentStart);
    }
}
