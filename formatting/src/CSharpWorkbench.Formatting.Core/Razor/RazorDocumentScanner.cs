namespace CSharpWorkbench.Formatting.Core.Razor;

internal sealed class RazorDocumentScanner
{
    private static readonly HashSet<string> VoidElementNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta",
        "param", "source", "track", "wbr",
    };

    private static readonly HashSet<string> ControlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "for", "foreach", "while", "switch", "try", "using", "lock", "do",
    };

    private static readonly HashSet<string> DirectiveKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "page", "model", "using", "inject", "inherits", "implements", "layout", "namespace",
        "attribute", "typeparam", "rendermode", "addTagHelper", "removeTagHelper", "tagHelperPrefix",
    };

    public RazorDocumentModel Scan(string source, RazorDocumentKind kind, CancellationToken cancellationToken)
    {
        _ = kind;
        var regions = new List<RazorRegion>();
        var elementStack = new Stack<string>();
        var cursor = 0;
        var controlDepth = 0;
        var pendingControlBrace = false;

        while (cursor < source.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (source.AsSpan(cursor).StartsWith("@*".AsSpan(), StringComparison.Ordinal))
            {
                var end = source.IndexOf("*@", cursor + 2, StringComparison.Ordinal);
                if (end < 0)
                    return Unreliable(regions);

                Add(regions, RazorRegionKind.RazorComment, cursor, end + 2 - cursor);
                cursor = end + 2;
                continue;
            }

            if (source.AsSpan(cursor).StartsWith("<!--".AsSpan(), StringComparison.Ordinal))
            {
                var end = source.IndexOf("-->", cursor + 4, StringComparison.Ordinal);
                if (end < 0)
                    return Unreliable(regions);

                Add(regions, RazorRegionKind.HtmlComment, cursor, end + 3 - cursor);
                cursor = end + 3;
                continue;
            }

            if (source[cursor] == '<')
            {
                if (!TryScanTag(source, cursor, out var tag))
                    return Unreliable(regions);

                if (tag.IsDeclaration)
                {
                    Add(regions, RazorRegionKind.Protected, cursor, tag.End - cursor);
                    cursor = tag.End;
                    continue;
                }

                if (tag.IsClosing)
                {
                    if (elementStack.Count == 0 ||
                        !string.Equals(elementStack.Peek(), tag.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return Unreliable(regions);
                    }

                    elementStack.Pop();
                    Add(regions, RazorRegionKind.EndTag, cursor, tag.End - cursor, tag.Name);
                    cursor = tag.End;
                    continue;
                }

                var isVoid = VoidElementNames.Contains(tag.Name);
                Add(
                    regions,
                    tag.IsSelfClosing || isVoid ? RazorRegionKind.SelfClosingTag : RazorRegionKind.StartTag,
                    cursor,
                    tag.End - cursor,
                    tag.Name);
                cursor = tag.End;

                if (!tag.IsSelfClosing && !isVoid)
                {
                    elementStack.Push(tag.Name);
                    if (string.Equals(tag.Name, "script", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(tag.Name, "style", StringComparison.OrdinalIgnoreCase))
                    {
                        var closingStart = IndexOfClosingTag(source, cursor, tag.Name);
                        if (closingStart < 0)
                            return Unreliable(regions);

                        if (closingStart > cursor)
                            Add(regions, RazorRegionKind.Protected, cursor, closingStart - cursor);
                        cursor = closingStart;
                    }
                }

                continue;
            }

            if (IsLineContentStart(source, cursor))
            {
                var contentStart = cursor;
                while (contentStart < source.Length && (source[contentStart] == ' ' || source[contentStart] == '\t'))
                    contentStart++;
                var lineEnd = FindLineEnd(source, cursor);
                var contentEnd = TrimLineEndingStart(source, cursor, lineEnd);
                var trimmedEnd = TrimEnd(source, cursor, contentEnd);

                if (contentStart < trimmedEnd && source[contentStart] == '@')
                {
                    if (TryScanCodeBlock(source, contentStart, out var codeBlockEnd))
                    {
                        Add(regions, RazorRegionKind.CodeBlock, contentStart, codeBlockEnd - contentStart);
                        cursor = codeBlockEnd;
                        continue;
                    }

                    var keyword = ReadRazorKeyword(source, contentStart + 1);
                    if (ControlKeywords.Contains(keyword) &&
                        !(string.Equals(keyword, "using", StringComparison.OrdinalIgnoreCase) &&
                            !HasUsingControlSyntax(source, contentStart + 1 + keyword.Length, contentEnd)))
                    {
                        Add(regions, RazorRegionKind.ControlHeader, contentStart, trimmedEnd - contentStart);
                        if (ContainsUnquotedBrace(source, contentStart, trimmedEnd, '{'))
                            controlDepth++;
                        else
                            pendingControlBrace = true;
                        cursor = trimmedEnd;
                        continue;
                    }

                    if (DirectiveKeywords.Contains(keyword) || keyword.Length > 0)
                    {
                        Add(regions, RazorRegionKind.Directive, contentStart, trimmedEnd - contentStart);
                        cursor = trimmedEnd;
                        continue;
                    }
                }

                if (pendingControlBrace && contentStart < trimmedEnd && source[contentStart] == '{')
                {
                    Add(regions, RazorRegionKind.ControlOpenBrace, contentStart, trimmedEnd - contentStart);
                    controlDepth = Math.Max(1, controlDepth + 1);
                    pendingControlBrace = false;
                    cursor = trimmedEnd;
                    continue;
                }

                if (controlDepth > 0 && contentStart < trimmedEnd && source[contentStart] == '}')
                {
                    Add(regions, RazorRegionKind.ControlCloseBrace, contentStart, trimmedEnd - contentStart);
                    controlDepth--;
                    cursor = trimmedEnd;
                    continue;
                }

                if (IsControlContinuation(source, contentStart, trimmedEnd))
                {
                    Add(regions, RazorRegionKind.ControlHeader, contentStart, trimmedEnd - contentStart);
                    if (ContainsUnquotedBrace(source, contentStart, trimmedEnd, '{'))
                        controlDepth++;
                    else if (!IsDoWhileContinuation(source, contentStart, trimmedEnd))
                        pendingControlBrace = true;
                    cursor = trimmedEnd;
                    continue;
                }

                if (controlDepth > 0 && contentStart < trimmedEnd && source[contentStart] != '<')
                {
                    Add(regions, RazorRegionKind.Protected, cursor, trimmedEnd - cursor);
                    cursor = trimmedEnd;
                    continue;
                }

                if (controlDepth > 0 && contentStart < trimmedEnd && source[contentStart] == '<' && contentStart > cursor)
                {
                    Add(regions, RazorRegionKind.Text, cursor, contentStart - cursor);
                    cursor = contentStart;
                    continue;
                }
            }

            if (source[cursor] == '@' && TryScanRazorExpression(source, cursor, out var expressionEnd))
            {
                Add(regions, RazorRegionKind.Protected, cursor, expressionEnd - cursor);
                cursor = expressionEnd;
                continue;
            }

            var textStart = cursor++;
            while (cursor < source.Length && source[cursor] != '<' && source[cursor] != '@' &&
                !IsControlLineStart(source, cursor, controlDepth, pendingControlBrace))
            {
                cursor++;
            }
            Add(regions, RazorRegionKind.Text, textStart, cursor - textStart);
        }

        return elementStack.Count == 0 && controlDepth == 0 && !pendingControlBrace
            ? new RazorDocumentModel(true, regions)
            : Unreliable(regions);
    }

    private static bool TryScanTag(string source, int start, out TagScanResult result)
    {
        result = default;
        var cursor = start + 1;
        var isClosing = cursor < source.Length && source[cursor] == '/';
        if (isClosing)
            cursor++;

        if (cursor < source.Length && (source[cursor] == '!' || source[cursor] == '?'))
        {
            var declarationEnd = source.IndexOf('>', cursor + 1);
            if (declarationEnd < 0)
                return false;
            result = new TagScanResult(string.Empty, declarationEnd + 1, false, false, true);
            return true;
        }

        var nameStart = cursor;
        if (cursor >= source.Length || !IsTagNameStart(source[cursor]))
            return false;
        cursor++;
        while (cursor < source.Length && IsTagNamePart(source[cursor]))
            cursor++;
        var name = source.Substring(nameStart, cursor - nameStart);

        while (cursor < source.Length)
        {
            var current = source[cursor];
            if (current == '>')
            {
                var slash = cursor - 1;
                while (slash >= start && char.IsWhiteSpace(source[slash]))
                    slash--;
                result = new TagScanResult(name, cursor + 1, isClosing, slash >= start && source[slash] == '/', false);
                return true;
            }

            if (current == '\'' || current == '"')
            {
                var quote = current;
                cursor++;
                while (cursor < source.Length)
                {
                    if (source[cursor] == '@' && TryScanRazorExpression(source, cursor, out var expressionEnd))
                    {
                        cursor = expressionEnd;
                        continue;
                    }
                    if (source[cursor] == quote)
                    {
                        cursor++;
                        break;
                    }
                    cursor++;
                }
                if (cursor > source.Length || (cursor == source.Length && source[source.Length - 1] != quote))
                    return false;
                continue;
            }

            cursor++;
        }

        return false;
    }

    private static bool TryScanCodeBlock(string source, int start, out int end)
    {
        end = 0;
        var keyword = ReadRazorKeyword(source, start + 1);
        var isNamedBlock = string.Equals(keyword, "code", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyword, "functions", StringComparison.OrdinalIgnoreCase);
        var cursor = isNamedBlock ? start + 1 + keyword.Length : start + 1;
        while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
            cursor++;
        if (cursor >= source.Length || source[cursor] != '{')
            return false;

        return TryScanBalancedCSharp(source, cursor, '{', '}', out end);
    }

    private static bool TryScanRazorExpression(string source, int start, out int end)
    {
        end = 0;
        if (start + 1 >= source.Length)
            return false;

        var cursor = start + 1;
        if (source[cursor] == '(')
            return TryScanBalancedCSharp(source, cursor, '(', ')', out end);

        if (!IsIdentifierStart(source[cursor]))
            return false;
        cursor++;
        while (cursor < source.Length && (IsIdentifierPart(source[cursor]) || source[cursor] == '.'))
            cursor++;
        if (cursor < source.Length && source[cursor] == '(')
            return TryScanBalancedCSharp(source, cursor, '(', ')', out end);

        end = cursor;
        return true;
    }

    private static bool TryScanBalancedCSharp(string source, int start, char open, char close, out int end)
    {
        end = 0;
        var depth = 0;
        var cursor = start;
        while (cursor < source.Length)
        {
            var current = source[cursor];
            if (current == '/' && cursor + 1 < source.Length && source[cursor + 1] == '/')
            {
                cursor = FindLineEnd(source, cursor + 2);
                continue;
            }
            if (current == '/' && cursor + 1 < source.Length && source[cursor + 1] == '*')
            {
                var commentEnd = source.IndexOf("*/", cursor + 2, StringComparison.Ordinal);
                if (commentEnd < 0)
                    return false;
                cursor = commentEnd + 2;
                continue;
            }
            if (current == '@' && cursor + 1 < source.Length && source[cursor + 1] == '"')
            {
                if (!SkipQuoted(source, cursor + 1, '"', true, out cursor))
                    return false;
                continue;
            }
            if (current == '"')
            {
                var quoteCount = CountRepeated(source, cursor, '"');
                if (quoteCount >= 3)
                {
                    var rawEnd = source.IndexOf(new string('"', quoteCount), cursor + quoteCount, StringComparison.Ordinal);
                    if (rawEnd < 0)
                        return false;
                    cursor = rawEnd + quoteCount;
                    continue;
                }
                if (!SkipQuoted(source, cursor, '"', false, out cursor))
                    return false;
                continue;
            }
            if (current == '\'')
            {
                if (!SkipQuoted(source, cursor, '\'', false, out cursor))
                    return false;
                continue;
            }
            if (current == open)
                depth++;
            else if (current == close && --depth == 0)
            {
                end = cursor + 1;
                return true;
            }
            cursor++;
        }
        return false;
    }

    private static bool SkipQuoted(string source, int quoteStart, char quote, bool verbatim, out int cursor)
    {
        cursor = quoteStart + 1;
        while (cursor < source.Length)
        {
            if (source[cursor] == quote)
            {
                if (verbatim && cursor + 1 < source.Length && source[cursor + 1] == quote)
                {
                    cursor += 2;
                    continue;
                }
                cursor++;
                return true;
            }
            if (!verbatim && source[cursor] == '\\')
                cursor += 2;
            else
                cursor++;
        }
        return false;
    }

    private static int IndexOfClosingTag(string source, int start, string name)
    {
        return source.IndexOf("</" + name, start, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadRazorKeyword(string source, int start)
    {
        var cursor = start;
        while (cursor < source.Length && IsIdentifierPart(source[cursor]))
            cursor++;
        return source.Substring(start, cursor - start);
    }

    private static bool HasUsingControlSyntax(string source, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(source[start]))
            start++;
        return start < end && source[start] == '(';
    }

    private static bool IsControlContinuation(string source, int start, int end)
    {
        var keyword = ReadRazorKeyword(source, start);
        return string.Equals(keyword, "else", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyword, "catch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyword, "finally", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyword, "while", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDoWhileContinuation(string source, int start, int end)
    {
        var text = source.Substring(start, end - start).TrimEnd();
        return text.StartsWith("while", StringComparison.OrdinalIgnoreCase) && text.EndsWith(";", StringComparison.Ordinal);
    }

    private static bool ContainsUnquotedBrace(string source, int start, int end, char brace)
    {
        for (var cursor = start; cursor < end; cursor++)
        {
            if (source[cursor] == '"' || source[cursor] == '\'')
            {
                if (!SkipQuoted(source, cursor, source[cursor], false, out cursor))
                    return false;
                cursor--;
                continue;
            }
            if (source[cursor] == brace)
                return true;
        }
        return false;
    }

    private static bool IsLineContentStart(string source, int position)
    {
        for (var cursor = position - 1; cursor >= 0 && source[cursor] != '\n' && source[cursor] != '\r'; cursor--)
        {
            if (source[cursor] != ' ' && source[cursor] != '\t')
                return false;
        }
        return true;
    }

    private static bool IsControlLineStart(
        string source,
        int position,
        int controlDepth,
        bool pendingControlBrace)
    {
        if ((controlDepth == 0 && !pendingControlBrace) || !IsLineContentStart(source, position))
            return false;

        if (position > 0 && source[position - 1] != '\n' && source[position - 1] != '\r')
            return false;

        var cursor = position;
        while (cursor < source.Length && (source[cursor] == ' ' || source[cursor] == '\t'))
            cursor++;
        return cursor < source.Length && source[cursor] != '\n' && source[cursor] != '\r';
    }

    private static int FindLineEnd(string source, int start)
    {
        var cursor = start;
        while (cursor < source.Length && source[cursor] != '\n' && source[cursor] != '\r')
            cursor++;
        while (cursor < source.Length && (source[cursor] == '\n' || source[cursor] == '\r'))
            cursor++;
        return cursor;
    }

    private static int TrimLineEndingStart(string source, int start, int end)
    {
        while (end > start && (source[end - 1] == '\n' || source[end - 1] == '\r'))
            end--;
        return end;
    }

    private static int TrimEnd(string source, int start, int end)
    {
        while (end > start && (source[end - 1] == ' ' || source[end - 1] == '\t'))
            end--;
        return end;
    }

    private static int CountRepeated(string source, int start, char value)
    {
        var cursor = start;
        while (cursor < source.Length && source[cursor] == value)
            cursor++;
        return cursor - start;
    }

    private static bool IsTagNameStart(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsTagNamePart(char value) =>
        IsTagNameStart(value) || char.IsDigit(value) || value is '_' or ':' or '.' or '-';

    private static bool IsIdentifierStart(char value) => IsTagNameStart(value) || value == '_';

    private static bool IsIdentifierPart(char value) => IsIdentifierStart(value) || char.IsDigit(value);

    private static void Add(
        ICollection<RazorRegion> regions,
        RazorRegionKind kind,
        int start,
        int length,
        string? name = null)
    {
        if (length > 0)
            regions.Add(new RazorRegion(kind, new RazorSourceSpan(start, length), name));
    }

    private static RazorDocumentModel Unreliable(IReadOnlyList<RazorRegion> regions) => new(false, regions);

    private readonly struct TagScanResult(
        string name,
        int end,
        bool isClosing,
        bool isSelfClosing,
        bool isDeclaration)
    {
        public string Name { get; } = name;
        public int End { get; } = end;
        public bool IsClosing { get; } = isClosing;
        public bool IsSelfClosing { get; } = isSelfClosing;
        public bool IsDeclaration { get; } = isDeclaration;
    }
}
