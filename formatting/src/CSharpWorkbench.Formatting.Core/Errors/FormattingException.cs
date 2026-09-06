namespace CSharpWorkbench.Formatting.Core.Errors;

public enum FormattingErrorCode
{
    InvalidRequest,
    InvalidSpan,
    InvalidConfiguration,
    ParseFailure,
    FormattingFailure,
    UnsupportedLanguage,
}

public sealed class FormattingException : Exception
{
    public FormattingException(FormattingErrorCode code, string message)
    : base(message)
    {
        Code = code;
    }

    public FormattingException(FormattingErrorCode code, string message, Exception innerException)
    : base(message, innerException)
    {
        Code = code;
    }

    public FormattingErrorCode Code { get; }
}
