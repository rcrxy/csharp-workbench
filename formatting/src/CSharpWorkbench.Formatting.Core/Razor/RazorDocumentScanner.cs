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

    private static readonly HashSet<string> InlineControlKeywords = new(StringComparer.Ordinal)
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
        var pendingControlContinuation = false;

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
                    Add(regions, RazorRegionKind.Protected, cursor, tag.End - cursor,
                        protectedMetadata: new RazorProtectedMetadata(RazorProtectedKind.Declaration));
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
                    tag.Name,
                    tag.Metadata);
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
                            Add(regions, RazorRegionKind.Protected, cursor, closingStart - cursor,
                                protectedMetadata: new RazorProtectedMetadata(RazorProtectedKind.ScriptStyle));
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
                    if (TryScanCodeBlock(source, contentStart, out var codeBlockEnd, out var codeBlock))
                    {
                        Add(regions, RazorRegionKind.CodeBlock, contentStart, codeBlockEnd - contentStart,
                            codeBlock: codeBlock);
                        cursor = codeBlockEnd;
                        continue;
                    }

                    var keyword = ReadRazorKeyword(source, contentStart + 1);
                    if (ControlKeywords.Contains(keyword) &&
                        !(string.Equals(keyword, "using", StringComparison.OrdinalIgnoreCase) &&
                            !HasUsingControlSyntax(source, contentStart + 1 + keyword.Length, contentEnd)))
                    {
                        var control = CreateControlMetadata(source, contentStart, trimmedEnd, keyword, true);
                        Add(regions, RazorRegionKind.ControlHeader, contentStart, trimmedEnd - contentStart,
                            control: control);
                        var braceDelta = CountBraceDelta(source, contentStart, trimmedEnd);
                        controlDepth = Math.Max(0, controlDepth + braceDelta);
                        if (braceDelta == 0 && !control.IsInlineComplete &&
                            !ContainsUnquotedBrace(source, contentStart, trimmedEnd, '{'))
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
                    Add(regions, RazorRegionKind.ControlCloseBrace, contentStart, 1);
                    controlDepth--;
                    var continuationStart = contentStart + 1;
                    while (continuationStart < trimmedEnd && char.IsWhiteSpace(source[continuationStart]))
                        continuationStart++;
                    if (continuationStart < trimmedEnd &&
                        IsControlContinuation(source, continuationStart, trimmedEnd))
                    {
                        var keyword = ReadRazorKeyword(source, continuationStart);
                        var control = CreateControlMetadata(
                            source,
                            continuationStart,
                            trimmedEnd,
                            keyword,
                            false);
                        Add(
                            regions,
                            RazorRegionKind.ControlHeader,
                            continuationStart,
                            trimmedEnd - continuationStart,
                            control: control);
                        var braceDelta = CountBraceDelta(source, continuationStart, trimmedEnd);
                        controlDepth = Math.Max(0, controlDepth + braceDelta);
                        if (braceDelta == 0 && !control.IsInlineComplete &&
                            !IsDoWhileContinuation(source, continuationStart, trimmedEnd) &&
                            !ContainsUnquotedBrace(source, continuationStart, trimmedEnd, '{'))
                        {
                            pendingControlBrace = true;
                        }
                        pendingControlContinuation = false;
                    }
                    else
                    {
                        pendingControlContinuation = true;
                    }
                    cursor = trimmedEnd;
                    continue;
                }

                if (pendingControlContinuation && IsControlContinuation(source, contentStart, trimmedEnd))
                {
                    var keyword = ReadRazorKeyword(source, contentStart);
                    var control = CreateControlMetadata(source, contentStart, trimmedEnd, keyword, false);
                    Add(regions, RazorRegionKind.ControlHeader, contentStart, trimmedEnd - contentStart,
                        control: control);
                    var braceDelta = CountBraceDelta(source, contentStart, trimmedEnd);
                    controlDepth = Math.Max(0, controlDepth + braceDelta);
                    if (braceDelta == 0 && !control.IsInlineComplete &&
                        !IsDoWhileContinuation(source, contentStart, trimmedEnd) &&
                        !ContainsUnquotedBrace(source, contentStart, trimmedEnd, '{'))
                        pendingControlBrace = true;
                    pendingControlContinuation = false;
                    cursor = trimmedEnd;
                    continue;
                }

                if (controlDepth > 0 && contentStart < trimmedEnd && source[contentStart] != '<')
                {
                    Add(regions, RazorRegionKind.Protected, cursor, trimmedEnd - cursor,
                        protectedMetadata: new RazorProtectedMetadata(
                            RazorProtectedKind.CSharpStatement,
                            new RazorSourceSpan(contentStart, trimmedEnd - contentStart)));
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

            if (source[cursor] == '@')
            {
                var inlineControlStatus = TryScanInlineControl(
                    source,
                    cursor,
                    out var inlineControlEnd,
                    out var inlineControl);
                if (inlineControlStatus == InlineControlScanStatus.UnsafeControlCandidate)
                    return Unreliable(regions);
                if (inlineControlStatus == InlineControlScanStatus.SafeControl)
                {
                    Add(
                        regions,
                        RazorRegionKind.InlineControl,
                        cursor,
                        inlineControlEnd - cursor,
                        control: inlineControl);
                    cursor = inlineControlEnd;
                    continue;
                }
            }

            if (source[cursor] == '@' && TryScanRazorExpression(source, cursor, out var expressionEnd))
            {
                var csharpSpan = GetRazorExpressionCSharpSpan(source, cursor, expressionEnd);
                Add(regions, RazorRegionKind.Protected, cursor, expressionEnd - cursor,
                    protectedMetadata: new RazorProtectedMetadata(RazorProtectedKind.RazorExpression, csharpSpan));
                cursor = expressionEnd;
                continue;
            }

            var textStart = cursor++;
            while (cursor < source.Length && source[cursor] != '<' && source[cursor] != '@' &&
                !IsControlLineStart(
                    source,
                    cursor,
                    controlDepth,
                    pendingControlBrace,
                    pendingControlContinuation))
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
        var nameEnd = cursor;

        while (cursor < source.Length)
        {
            var current = source[cursor];
            if (current == '>')
            {
                var slash = cursor - 1;
                while (slash >= start && char.IsWhiteSpace(source[slash]))
                    slash--;
                var isSelfClosing = slash >= start && source[slash] == '/';
                var metadata = isClosing
                    ? null
                    : ParseTagMetadata(source, nameStart, nameEnd, cursor, isSelfClosing);
                result = new TagScanResult(name, cursor + 1, isClosing, isSelfClosing, false, metadata);
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

    private static RazorTagMetadata ParseTagMetadata(
        string source,
        int nameStart,
        int nameEnd,
        int tagClose,
        bool isSelfClosing)
    {
        var attributes = new List<RazorAttributeMetadata>();
        var cursor = nameEnd;
        var contentEnd = tagClose;
        if (isSelfClosing)
        {
            contentEnd--;
            while (contentEnd > nameEnd && char.IsWhiteSpace(source[contentEnd - 1]))
                contentEnd--;
        }

        while (cursor < contentEnd)
        {
            while (cursor < contentEnd && char.IsWhiteSpace(source[cursor]))
                cursor++;
            if (cursor >= contentEnd)
                break;

            var attributeStart = cursor;
            var attributeNameStart = cursor;
            while (cursor < contentEnd && !char.IsWhiteSpace(source[cursor]) &&
                source[cursor] != '=' && source[cursor] != '>' && source[cursor] != '/')
            {
                cursor++;
            }
            if (cursor == attributeNameStart)
                return new RazorTagMetadata(
                    new RazorSourceSpan(nameStart, nameEnd - nameStart),
                    attributes,
                    false,
                    isSelfClosing);

            var nameSpan = new RazorSourceSpan(attributeNameStart, cursor - attributeNameStart);
            while (cursor < contentEnd && char.IsWhiteSpace(source[cursor]))
                cursor++;

            RazorSourceSpan? equalsSpan = null;
            RazorSourceSpan? valueSpan = null;
            if (cursor < contentEnd && source[cursor] == '=')
            {
                equalsSpan = new RazorSourceSpan(cursor, 1);
                cursor++;
                while (cursor < contentEnd && char.IsWhiteSpace(source[cursor]))
                    cursor++;
                if (cursor >= contentEnd)
                    return new RazorTagMetadata(
                        new RazorSourceSpan(nameStart, nameEnd - nameStart),
                        attributes,
                        false,
                        isSelfClosing);

                var valueStart = cursor;
                if (source[cursor] == '"' || source[cursor] == '\'')
                {
                    var quote = source[cursor++];
                    var closed = false;
                    while (cursor < contentEnd)
                    {
                        if (source[cursor] == '@' && TryScanRazorExpression(source, cursor, out var expressionEnd) &&
                            expressionEnd <= contentEnd)
                        {
                            cursor = expressionEnd;
                            continue;
                        }
                        if (source[cursor] == quote)
                        {
                            cursor++;
                            closed = true;
                            break;
                        }
                        cursor++;
                    }
                    if (!closed)
                        return new RazorTagMetadata(
                            new RazorSourceSpan(nameStart, nameEnd - nameStart),
                            attributes,
                            false,
                            isSelfClosing);
                }
                else
                {
                    while (cursor < contentEnd && !char.IsWhiteSpace(source[cursor]))
                    {
                        if (source[cursor] == '@' && TryScanRazorExpression(source, cursor, out var expressionEnd) &&
                            expressionEnd <= contentEnd)
                        {
                            cursor = expressionEnd;
                            continue;
                        }
                        cursor++;
                    }
                }
                valueSpan = new RazorSourceSpan(valueStart, cursor - valueStart);
            }

            attributes.Add(new RazorAttributeMetadata(
                new RazorSourceSpan(attributeStart, cursor - attributeStart),
                nameSpan,
                equalsSpan,
                valueSpan));
        }

        return new RazorTagMetadata(
            new RazorSourceSpan(nameStart, nameEnd - nameStart),
            attributes,
            true,
            isSelfClosing);
    }

    private static bool TryScanCodeBlock(
        string source,
        int start,
        out int end,
        out RazorCodeBlockMetadata metadata)
    {
        end = 0;
        metadata = null!;
        var keyword = ReadRazorKeyword(source, start + 1);
        var isNamedBlock = string.Equals(keyword, "code", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(keyword, "functions", StringComparison.OrdinalIgnoreCase);
        var cursor = isNamedBlock ? start + 1 + keyword.Length : start + 1;
        while (cursor < source.Length && char.IsWhiteSpace(source[cursor]))
            cursor++;
        if (cursor >= source.Length || source[cursor] != '{')
            return false;

        if (!TryScanBalancedCSharp(source, cursor, '{', '}', out end))
            return false;

        var kind = string.Equals(keyword, "code", StringComparison.OrdinalIgnoreCase)
            ? RazorCodeBlockKind.Code
            : string.Equals(keyword, "functions", StringComparison.OrdinalIgnoreCase)
                ? RazorCodeBlockKind.Functions
                : RazorCodeBlockKind.Explicit;
        metadata = new RazorCodeBlockMetadata(
            kind,
            new RazorSourceSpan(cursor + 1, end - cursor - 2),
            new RazorSourceSpan(cursor, 1),
            new RazorSourceSpan(end - 1, 1));
        return true;
    }

    private static RazorControlMetadata CreateControlMetadata(
        string source,
        int start,
        int end,
        string keyword,
        bool hasTransition)
    {
        var kind = ResolveControlKind(source, start, end, keyword);
        var headerStart = hasTransition ? start + 1 : start;
        var openBrace = FindUnquotedCharacter(source, headerStart, end, '{');
        var closeBrace = FindUnquotedCharacter(source, headerStart, end, '}');
        var headerEnd = openBrace >= 0 ? openBrace : end;
        RazorSourceSpan? csharpHeaderSpan = null;
        if (kind is not RazorControlKind.Else and not RazorControlKind.Try and
            not RazorControlKind.Finally and not RazorControlKind.Do)
        {
            csharpHeaderSpan = new RazorSourceSpan(headerStart, headerEnd - headerStart);
        }
        return new RazorControlMetadata(
            kind,
            new RazorSourceSpan(start, headerEnd - start),
            csharpHeaderSpan,
            openBrace >= 0 && closeBrace > openBrace);
    }

    private static RazorControlKind ResolveControlKind(string source, int start, int end, string keyword)
    {
        if (string.Equals(keyword, "else", StringComparison.OrdinalIgnoreCase))
        {
            var rest = source.Substring(start + keyword.Length, end - start - keyword.Length).TrimStart();
            return rest.StartsWith("if", StringComparison.OrdinalIgnoreCase)
                ? RazorControlKind.ElseIf
                : RazorControlKind.Else;
        }
        return keyword.ToLowerInvariant() switch
        {
            "if" => RazorControlKind.If,
            "for" => RazorControlKind.For,
            "foreach" => RazorControlKind.Foreach,
            "while" => RazorControlKind.While,
            "switch" => RazorControlKind.Switch,
            "try" => RazorControlKind.Try,
            "catch" => RazorControlKind.Catch,
            "finally" => RazorControlKind.Finally,
            "using" => RazorControlKind.Using,
            "lock" => RazorControlKind.Lock,
            "do" => RazorControlKind.Do,
            _ => RazorControlKind.If,
        };
    }

    private static RazorSourceSpan GetRazorExpressionCSharpSpan(string source, int start, int end)
    {
        return source[start + 1] == '('
            ? new RazorSourceSpan(start + 2, end - start - 3)
            : new RazorSourceSpan(start + 1, end - start - 1);
    }

    private static int CountBraceDelta(string source, int start, int end)
    {
        var delta = 0;
        for (var cursor = start; cursor < end; cursor++)
        {
            if (source[cursor] == '"' || source[cursor] == '\'')
            {
                if (!SkipQuoted(source, cursor, source[cursor], false, out cursor))
                    return delta;
                cursor--;
            }
            else if (source[cursor] == '{')
                delta++;
            else if (source[cursor] == '}')
                delta--;
        }
        return delta;
    }

    private static int FindUnquotedCharacter(string source, int start, int end, char value)
    {
        for (var cursor = start; cursor < end; cursor++)
        {
            if (source[cursor] == '"' || source[cursor] == '\'')
            {
                if (!SkipQuoted(source, cursor, source[cursor], false, out cursor))
                    return -1;
                cursor--;
            }
            else if (source[cursor] == value)
                return cursor;
        }
        return -1;
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

    private static InlineControlScanStatus TryScanInlineControl(
        string source,
        int start,
        out int end,
        out RazorControlMetadata metadata)
    {
        end = 0;
        metadata = null!;
        if (start + 1 >= source.Length || !IsIdentifierStart(source[start + 1]))
            return InlineControlScanStatus.NotControl;

        var keyword = ReadRazorKeyword(source, start + 1);
        if (!InlineControlKeywords.Contains(keyword))
            return InlineControlScanStatus.NotControl;
        if (string.Equals(keyword, "try", StringComparison.Ordinal) ||
            string.Equals(keyword, "do", StringComparison.Ordinal))
        {
            return InlineControlScanStatus.UnsafeControlCandidate;
        }

        var lineEnd = TrimLineEndingStart(source, start, FindLineEnd(source, start));
        var cursor = start + 1 + keyword.Length;
        while (cursor < lineEnd && (source[cursor] == ' ' || source[cursor] == '\t'))
            cursor++;
        if (cursor >= lineEnd || source[cursor] != '(' ||
            !TryScanBalancedCSharp(source, cursor, '(', ')', out var headerEnd) ||
            headerEnd > lineEnd)
        {
            return InlineControlScanStatus.UnsafeControlCandidate;
        }

        cursor = headerEnd;
        while (cursor < lineEnd && (source[cursor] == ' ' || source[cursor] == '\t'))
            cursor++;
        if (cursor >= lineEnd || source[cursor] != '{' ||
            !TryScanInlineControlBlock(source, cursor, lineEnd, out end))
        {
            return InlineControlScanStatus.UnsafeControlCandidate;
        }

        var continuationStart = end;
        while (continuationStart < lineEnd &&
            (source[continuationStart] == ' ' || source[continuationStart] == '\t'))
        {
            continuationStart++;
        }
        if (continuationStart < lineEnd && IsIdentifierStart(source[continuationStart]))
        {
            var continuation = ReadRazorKeyword(source, continuationStart);
            if (string.Equals(continuation, "else", StringComparison.Ordinal) ||
                string.Equals(continuation, "catch", StringComparison.Ordinal) ||
                string.Equals(continuation, "finally", StringComparison.Ordinal) ||
                string.Equals(continuation, "while", StringComparison.Ordinal))
            {
                return InlineControlScanStatus.UnsafeControlCandidate;
            }
        }

        metadata = CreateControlMetadata(source, start, end, keyword, true);
        return InlineControlScanStatus.SafeControl;
    }

    private static bool TryScanInlineControlBlock(string source, int start, int lineEnd, out int end)
    {
        end = 0;
        var depth = 0;
        var cursor = start;
        var markupElements = new Stack<string>();
        while (cursor < lineEnd)
        {
            var current = source[cursor];
            if (current == '/' && cursor + 1 < lineEnd && source[cursor + 1] == '/')
                return false;
            if (current == '/' && cursor + 1 < lineEnd && source[cursor + 1] == '*')
            {
                var commentEnd = source.IndexOf("*/", cursor + 2, StringComparison.Ordinal);
                if (commentEnd < 0 || commentEnd + 2 > lineEnd)
                    return false;
                cursor = commentEnd + 2;
                continue;
            }
            if (current == '@' && cursor + 1 < lineEnd && source[cursor + 1] == '*')
            {
                var commentEnd = source.IndexOf("*@", cursor + 2, StringComparison.Ordinal);
                if (commentEnd < 0 || commentEnd + 2 > lineEnd)
                    return false;
                cursor = commentEnd + 2;
                continue;
            }
            if (current == '<')
            {
                if (source.AsSpan(cursor).StartsWith("<!--".AsSpan(), StringComparison.Ordinal))
                {
                    var commentEnd = source.IndexOf("-->", cursor + 4, StringComparison.Ordinal);
                    if (commentEnd < 0 || commentEnd + 3 > lineEnd)
                        return false;
                    cursor = commentEnd + 3;
                    continue;
                }
                if (!TryScanTag(source, cursor, out var tag) || tag.End > lineEnd)
                    return false;
                if (!tag.IsDeclaration)
                {
                    if (tag.IsClosing)
                    {
                        if (markupElements.Count == 0 ||
                            !string.Equals(markupElements.Peek(), tag.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                        markupElements.Pop();
                    }
                    else if (!tag.IsSelfClosing && !VoidElementNames.Contains(tag.Name))
                    {
                        markupElements.Push(tag.Name);
                    }
                }
                cursor = tag.End;
                continue;
            }
            if (current == '@' && cursor + 1 < lineEnd && source[cursor + 1] == '"')
            {
                if (!SkipQuoted(source, cursor + 1, '"', true, out cursor) || cursor > lineEnd)
                    return false;
                continue;
            }
            if (current == '"')
            {
                var quoteCount = CountRepeated(source, cursor, '"');
                if (quoteCount >= 3)
                {
                    var rawEnd = source.IndexOf(new string('"', quoteCount), cursor + quoteCount, StringComparison.Ordinal);
                    if (rawEnd < 0 || rawEnd + quoteCount > lineEnd)
                        return false;
                    cursor = rawEnd + quoteCount;
                    continue;
                }
                if (!SkipQuoted(source, cursor, '"', false, out cursor) || cursor > lineEnd)
                    return false;
                continue;
            }
            if (current == '\'')
            {
                if (!SkipQuoted(source, cursor, '\'', false, out cursor) || cursor > lineEnd)
                    return false;
                continue;
            }
            if (current == '@' && TryScanRazorExpression(source, cursor, out var expressionEnd) &&
                expressionEnd <= lineEnd)
            {
                cursor = expressionEnd;
                continue;
            }
            if (markupElements.Count > 0)
            {
                cursor++;
                continue;
            }
            if (current == '{')
                depth++;
            else if (current == '}' && --depth == 0)
            {
                end = cursor + 1;
                return true;
            }
            cursor++;
        }
        return false;
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
        bool pendingControlBrace,
        bool pendingControlContinuation)
    {
        if ((controlDepth == 0 && !pendingControlBrace && !pendingControlContinuation) ||
            !IsLineContentStart(source, position))
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
        string? name = null,
        RazorTagMetadata? tag = null,
        RazorCodeBlockMetadata? codeBlock = null,
        RazorControlMetadata? control = null,
        RazorProtectedMetadata? protectedMetadata = null)
    {
        if (length > 0)
            regions.Add(new RazorRegion(
                kind,
                new RazorSourceSpan(start, length),
                name,
                tag,
                codeBlock,
                control,
                protectedMetadata));
    }

    private static RazorDocumentModel Unreliable(IReadOnlyList<RazorRegion> regions) => new(false, regions);

    private readonly struct TagScanResult(
        string name,
        int end,
        bool isClosing,
        bool isSelfClosing,
        bool isDeclaration,
        RazorTagMetadata? metadata = null)
    {
        public string Name { get; } = name;
        public int End { get; } = end;
        public bool IsClosing { get; } = isClosing;
        public bool IsSelfClosing { get; } = isSelfClosing;
        public bool IsDeclaration { get; } = isDeclaration;
        public RazorTagMetadata? Metadata { get; } = metadata;
    }

    private enum InlineControlScanStatus
    {
        NotControl,
        SafeControl,
        UnsafeControlCandidate,
    }
}
