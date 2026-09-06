using System.Reflection;
using System.Text.Json;
using CSharpWorkbench.Formatter.Protocol;
using CSharpWorkbench.Formatting.Core;
using CSharpWorkbench.Formatting.Core.Contracts;
using CSharpWorkbench.Formatting.Core.Errors;

namespace CSharpWorkbench.Formatter;

internal sealed class FormatterServer
{
    private const int ProtocolVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly FormattingEngine _formattingEngine = new();
    private readonly WireReader _reader;
    private readonly WireWriter _writer;
    private readonly object _requestLock = new();
    private readonly Dictionary<int, CancellationTokenSource> _activeRequests = new();
    private readonly List<Task> _requestTasks = new();
    private bool _handshakeCompleted;
    private bool _shuttingDown;

    public FormatterServer(Stream input, Stream output)
    {
        _reader = new WireReader(input);
        _writer = new WireWriter(output);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using (_writer)
        {
            while (!_shuttingDown)
            {
                var body = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (body is null)
                {
                    await StopActiveRequestsAsync().ConfigureAwait(false);
                    return;
                }

                WireMessage message;
                try
                {
                    message = JsonSerializer.Deserialize<WireMessage>(body, JsonOptions)
                        ?? throw new JsonException("Message body must contain a JSON object.");
                }
                catch (JsonException exception)
                {
                    await WriteErrorAsync(null, "invalidMessage", exception.Message).ConfigureAwait(false);
                    continue;
                }

                await DispatchAsync(message).ConfigureAwait(false);
            }
        }
    }

    private async Task DispatchAsync(WireMessage message)
    {
        try
        {
            switch (message.Method)
            {
                case "handshake":
                    await HandleHandshakeAsync(message).ConfigureAwait(false);
                    break;
                case "formatDocument":
                    await HandleFormatDocumentAsync(message).ConfigureAwait(false);
                    break;
                case "cancel":
                    HandleCancel(DeserializeParams<CancelParams>(message));
                    break;
                case "shutdown":
                    await HandleShutdownAsync(message).ConfigureAwait(false);
                    break;
                default:
                    await WriteErrorAsync(message.Id, "methodNotFound", $"Unknown method: {message.Method}.")
                        .ConfigureAwait(false);
                    break;
            }
        }
        catch (JsonException exception)
        {
            await WriteErrorAsync(message.Id, "invalidRequest", exception.Message).ConfigureAwait(false);
        }
    }

    private async Task HandleHandshakeAsync(WireMessage message)
    {
        var id = RequireRequestId(message);
        var parameters = DeserializeParams<HandshakeParams>(message);
        if (parameters.ProtocolVersion != ProtocolVersion)
        {
            await WriteErrorAsync(id, "protocolVersionMismatch", "Unsupported protocol version.")
                .ConfigureAwait(false);
            return;
        }

        _handshakeCompleted = true;
        await _writer.WriteAsync(new WireResponse
        {
            Id = id,
            Result = new HandshakeResult
            {
                ProtocolVersion = ProtocolVersion,
                FormatterVersion = GetFormatterVersion(),
                Capabilities = new FormatterCapabilities
                {
                    FormatDocument = true,
                    Languages = new[] { "csharp" },
                },
            },
        }).ConfigureAwait(false);
    }

    private async Task HandleFormatDocumentAsync(WireMessage message)
    {
        var id = RequireRequestId(message);
        if (!_handshakeCompleted)
        {
            await WriteErrorAsync(id, "handshakeRequired", "A successful handshake is required.")
                .ConfigureAwait(false);
            return;
        }

        var parameters = DeserializeParams<FormatDocumentParams>(message);
        var cancellation = new CancellationTokenSource();
        lock (_requestLock)
        {
            if (_activeRequests.ContainsKey(id))
            {
                cancellation.Dispose();
                throw new JsonException("The request id is already active.");
            }

            _activeRequests.Add(id, cancellation);
            var task = Task.Run(() => FormatDocumentAsync(id, parameters, cancellation));
            _requestTasks.Add(task);
        }
    }

    private async Task FormatDocumentAsync(
        int id,
        FormatDocumentParams parameters,
        CancellationTokenSource cancellation)
    {
        try
        {
            var request = new FormattingRequest(
                MapLanguage(parameters.Language),
                parameters.Source,
                parameters.ResolvedEditorConfig,
                MapEditorFallback(parameters.EditorFallback));
            var result = await _formattingEngine.FormatAsync(request, cancellation.Token).ConfigureAwait(false);
            await _writer.WriteAsync(new WireResponse
            {
                Id = id,
                Result = new FormatDocumentResult
                {
                    Changes = result.Changes.Select(change => new WireTextChange
                    {
                        Span = new WireTextSpan
                        {
                            Start = change.Span.Start,
                            Length = change.Span.Length,
                        },
                        NewText = change.NewText,
                    }).ToArray(),
                },
            }).ConfigureAwait(false);
        }
        catch (FormattingException exception)
        {
            await WriteErrorAsync(id, MapErrorCode(exception.Code), exception.Message).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            await WriteErrorAsync(id, "requestCancelled", "Request was cancelled.").ConfigureAwait(false);
        }
        finally
        {
            lock (_requestLock)
            {
                _activeRequests.Remove(id);
            }

            cancellation.Dispose();
        }
    }

    private void HandleCancel(CancelParams parameters)
    {
        lock (_requestLock)
        {
            if (_activeRequests.TryGetValue(parameters.Id, out var cancellation))
            {
                cancellation.Cancel();
            }
        }
    }

    private async Task HandleShutdownAsync(WireMessage message)
    {
        var id = RequireRequestId(message);
        _shuttingDown = true;
        await StopActiveRequestsAsync().ConfigureAwait(false);
        await _writer.WriteAsync(new WireResponse
        {
            Id = id,
            Result = new { },
        }).ConfigureAwait(false);
    }

    private async Task StopActiveRequestsAsync()
    {
        Task[] tasks;
        lock (_requestLock)
        {
            foreach (var cancellation in _activeRequests.Values)
            {
                cancellation.Cancel();
            }

            tasks = _requestTasks.ToArray();
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static int RequireRequestId(WireMessage message)
    {
        if (message.Id is not int id)
        {
            throw new JsonException($"Method '{message.Method}' requires an integer request id.");
        }

        return id;
    }

    private static T DeserializeParams<T>(WireMessage message)
    {
        return message.Params.Deserialize<T>(JsonOptions)
            ?? throw new JsonException($"Method '{message.Method}' requires params.");
    }

    private static FormattingLanguage MapLanguage(string language)
    {
        if (string.Equals(language, "csharp", StringComparison.Ordinal))
        {
            return FormattingLanguage.CSharp;
        }

        throw new FormattingException(
            FormattingErrorCode.UnsupportedLanguage,
            $"Unsupported formatting language: {language}.");
    }

    private static EditorFallback? MapEditorFallback(WireEditorFallback? fallback)
    {
        if (fallback is null)
        {
            return null;
        }

        return new EditorFallback
        {
            InsertSpaces = fallback.InsertSpaces,
            TabSize = fallback.TabSize,
            MaxLineLength = fallback.MaxLineLength,
            LineEnding = fallback.LineEnding,
            InsertFinalNewline = fallback.InsertFinalNewline,
            TrimTrailingWhitespace = fallback.TrimTrailingWhitespace,
            Charset = fallback.Charset,
        };
    }

    private static string MapErrorCode(FormattingErrorCode code)
    {
        return code switch
        {
            FormattingErrorCode.InvalidRequest => "invalidRequest",
            FormattingErrorCode.InvalidSpan => "invalidSpan",
            FormattingErrorCode.InvalidConfiguration => "invalidConfiguration",
            FormattingErrorCode.ParseFailure => "parseFailure",
            FormattingErrorCode.FormattingFailure => "formattingFailure",
            FormattingErrorCode.UnsupportedLanguage => "unsupportedLanguage",
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
        };
    }

    private static string GetFormatterVersion()
    {
        return typeof(FormatterServer).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "unknown";
    }

    private Task WriteErrorAsync(int? id, string code, string message)
    {
        return _writer.WriteAsync(new WireResponse
        {
            Id = id,
            Error = new WireError
            {
                Code = code,
                Message = message,
            },
        });
    }
}
