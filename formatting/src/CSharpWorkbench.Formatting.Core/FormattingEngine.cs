using CSharpWorkbench.Formatting.Core.Contracts;
using CSharpWorkbench.Formatting.Core.CSharp.Options;
using CSharpWorkbench.Formatting.Core.CSharp.Roslyn;
using CSharpWorkbench.Formatting.Core.Errors;

namespace CSharpWorkbench.Formatting.Core;

public sealed class FormattingEngine
{
    private readonly CSharpRoslynFormatter _csharpFormatter;

    public FormattingEngine() : this(new CSharpRoslynFormatter()) { }

    internal FormattingEngine(CSharpRoslynFormatter csharpFormatter)
    {
        _csharpFormatter = csharpFormatter;
    }

    public async Task<FormattingResult> FormatAsync(
        FormattingRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        cancellationToken.ThrowIfCancellationRequested();

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

            default:
                throw new FormattingException(
                    FormattingErrorCode.UnsupportedLanguage,
                    $"Unsupported formatting language: {request.Language}.");
        }
    }
}
