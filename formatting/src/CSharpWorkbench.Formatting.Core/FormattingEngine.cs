using CSharpWorkbench.Formatting.Core.Contracts;
using CSharpWorkbench.Formatting.Core.CSharp.Options;
using CSharpWorkbench.Formatting.Core.CSharp.Roslyn;
using CSharpWorkbench.Formatting.Core.Errors;
using CSharpWorkbench.Formatting.Core.Razor;

namespace CSharpWorkbench.Formatting.Core;

public sealed class FormattingEngine
{
    private readonly CSharpRoslynFormatter _csharpFormatter;
    private readonly RazorFormatter _razorFormatter;

    public FormattingEngine() : this(new CSharpRoslynFormatter()) { }

    internal FormattingEngine(CSharpRoslynFormatter csharpFormatter)
    {
        _csharpFormatter = csharpFormatter;
        _razorFormatter = new RazorFormatter(csharpFormatter);
    }

    public async Task<FormattingResult> FormatAsync(
        FormattingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        cancellationToken.ThrowIfCancellationRequested();

        if (request.Range is not null)
        {
            return await FormatRangeAsync(request, cancellationToken).ConfigureAwait(false);
        }

        switch (request.Language)
        {
            case FormattingLanguage.CSharp:
                var options = CSharpFormattingOptionsResolver.Resolve(
                    request.ResolvedEditorConfig,
                    request.EditorFallback);
                var result = await _csharpFormatter.FormatAsync(
                    new CSharpFormattingRequest(request.Source, CSharpFormattingKind.Document, options),
                    cancellationToken).ConfigureAwait(false);
                return new FormattingResult(result.Changes.Select(change =>
                        new FormattingTextChange(
                            new FormattingTextSpan(change.Span.Start, change.Span.Length),
                            change.NewText)).ToArray());

            case FormattingLanguage.Razor:
            case FormattingLanguage.Cshtml:
                var razorOptions = RazorFormattingOptionsResolver.Resolve(
                    request.ResolvedEditorConfig,
                    request.EditorFallback);
                var razorCSharpOptions = CSharpFormattingOptionsResolver.Resolve(
                    request.ResolvedEditorConfig,
                    request.EditorFallback);
                return await _razorFormatter.FormatAsync(
                    request.Source,
                    request.Language == FormattingLanguage.Razor
                        ? RazorDocumentKind.Component
                        : RazorDocumentKind.Cshtml,
                    razorOptions,
                    razorCSharpOptions,
                    cancellationToken).ConfigureAwait(false);

            default:
                throw new FormattingException(
                    FormattingErrorCode.UnsupportedLanguage,
                    $"Unsupported formatting language: {request.Language}.");
        }
    }

    public Task<FormattingResult> FormatRangeAsync(
        FormattingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.Range is null)
            throw new ArgumentException("Range formatting requires a valid span.", nameof(request));

        var span = request.Range.Value;
        return FormatRangeAsync(request.Language, request.Source, span, request.ResolvedEditorConfig, request.EditorFallback, cancellationToken);
    }

    public async Task<FormattingResult> FormatRangeAsync(
        FormattingLanguage language,
        string source,
        FormattingTextSpan range,
        IReadOnlyDictionary<string, string>? resolvedEditorConfig = null,
        EditorFallback? editorFallback = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        switch (language)
        {
            case FormattingLanguage.CSharp:
                {
                    var options = CSharpFormattingOptionsResolver.Resolve(
                        resolvedEditorConfig ?? new Dictionary<string, string>(),
                        editorFallback);
                    var result = await _csharpFormatter.FormatAsync(
                        new CSharpFormattingRequest(
                            source,
                            CSharpFormattingKind.Range,
                            options,
                            new CSharpTextSpan(range.Start, range.Length)),
                        cancellationToken).ConfigureAwait(false);
                    return new FormattingResult(result.Changes.Select(change =>
                            new FormattingTextChange(
                                new FormattingTextSpan(change.Span.Start, change.Span.Length),
                                change.NewText)).ToArray());
                }

            default:
                throw new FormattingException(
                    FormattingErrorCode.UnsupportedLanguage,
                    $"Range formatting is only supported for C# in this implementation: {language}.");
        }
    }
}
