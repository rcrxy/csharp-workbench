using CSharpWorkbench.Formatting.Core.CSharp.Options;
using CSharpWorkbench.Formatting.Core.Errors;

namespace CSharpWorkbench.Formatting.Core.CSharp.Roslyn;

internal static class CSharpFormattingRequestValidator
{
    public static void Validate(CSharpFormattingRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        ValidateRequestShape(request);
        ValidateOptions(request.Options);
    }

    private static void ValidateRequestShape(CSharpFormattingRequest request)
    {
        switch (request.Kind)
        {
            case CSharpFormattingKind.Document:
                if (request.Span is not null || request.SnippetKind is not null)
                    throw InvalidRequest("Document formatting cannot specify a span or snippet kind.");

                break;

            case CSharpFormattingKind.Range:
                if (request.Span is null || request.SnippetKind is not null)
                    throw InvalidRequest("Range formatting requires a span and cannot specify a snippet kind.");

                ValidateSpan(request.Source, request.Span.Value);
                break;

            case CSharpFormattingKind.Snippet:
                if (request.Span is not null || request.SnippetKind is null)
                    throw InvalidRequest("Snippet formatting requires a snippet kind and cannot specify a span.");

                if (!Enum.IsDefined(typeof(CSharpSnippetKind), request.SnippetKind.Value))
                    throw InvalidRequest("Snippet formatting requires a supported snippet kind.");

                break;

            default:
                throw InvalidRequest($"Unsupported formatting kind: {request.Kind}.");
        }
    }

    private static void ValidateOptions(CSharpFormattingOptions options)
    {
        if (options.Indentation is null ||
            options.CSharpIndentation is null ||
            options.CSharpNewLines is null ||
            options.CSharpSpacing is null ||
            options.CSharpWrapping is null)
        {
            throw InvalidConfiguration("Formatting option groups cannot be null.");
        }

        if (options.Indentation.Size <= 0 || options.Indentation.TabWidth <= 0)
        {
            throw InvalidConfiguration("Indentation size and tab width must be greater than zero.");
        }

        if (options.MaxLineLength is <= 0)
        {
            throw InvalidConfiguration("Maximum line length must be greater than zero when specified.");
        }

        if (options.LineEnding is not "\n" and not "\r\n")
        {
            throw InvalidConfiguration("Line ending must be either LF or CRLF.");
        }

        if (options.CSharpNewLines.OpenBraceContexts is null || options.CSharpSpacing.BetweenParentheses is null)
        {
            throw InvalidConfiguration("Formatting option collections cannot be null.");
        }

        ValidateEnum(options.Indentation.Style, nameof(options.Indentation.Style));
        ValidateEnum(options.CSharpIndentation.IndentLabels, nameof(options.CSharpIndentation.IndentLabels));
        ValidateEnum(options.CSharpNewLines.BeforeOpenBrace, nameof(options.CSharpNewLines.BeforeOpenBrace));
        ValidateEnum(options.CSharpSpacing.AroundBinaryOperators, nameof(options.CSharpSpacing.AroundBinaryOperators));
        ValidateEnum(options.Charset, nameof(options.Charset));

        foreach (var context in options.CSharpNewLines.OpenBraceContexts)
        {
            ValidateEnum(context, nameof(options.CSharpNewLines.OpenBraceContexts));
        }

        foreach (var context in options.CSharpSpacing.BetweenParentheses)
        {
            ValidateEnum(context, nameof(options.CSharpSpacing.BetweenParentheses));
        }
    }

    private static void ValidateSpan(string source, CSharpTextSpan span)
    {
        if (span.Start < 0 || span.Length < 0 || span.Start > source.Length - span.Length)
        {
            throw new FormattingException(
                FormattingErrorCode.InvalidSpan,
            "The formatting span must be contained within the source text.");
        }
    }

    private static void ValidateEnum<TEnum>(TEnum value, string optionName)
    where TEnum : struct
    {
        if (!Enum.IsDefined(typeof(TEnum), value))
        {
            throw InvalidConfiguration($"Formatting option {optionName} has an unsupported value.");
        }
    }

    private static FormattingException InvalidRequest(string message)
    {
        return new FormattingException(FormattingErrorCode.InvalidRequest, message);
    }

    private static FormattingException InvalidConfiguration(string message)
    {
        return new FormattingException(FormattingErrorCode.InvalidConfiguration, message);
    }
}
