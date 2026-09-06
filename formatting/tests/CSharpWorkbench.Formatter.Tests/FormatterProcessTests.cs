using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CSharpWorkbench.Formatting.Core;
using CSharpWorkbench.Formatting.Core.Contracts;
using Xunit;

namespace CSharpWorkbench.Formatter.Tests;

public sealed class FormatterProcessTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task HandshakeReportsOnlyWireCapabilitiesAndCleanStdout()
    {
        await using var formatter = FormatterProcess.Start();

        await formatter.SendAsync(new
        {
            id = 1,
            method = "handshake",
            @params = new { protocolVersion = 1 },
        });
        using var response = await formatter.ReadAsync();

        Assert.Equal(1, response.RootElement.GetProperty("id").GetInt32());
        var result = response.RootElement.GetProperty("result");
        Assert.Equal(1, result.GetProperty("protocolVersion").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("formatterVersion").GetString()));
        var capabilities = result.GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("formatDocument").GetBoolean());
        Assert.Equal(new[] { "csharp" }, capabilities.GetProperty("languages")
            .EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.False(capabilities.TryGetProperty("formatRange", out _));
        Assert.False(capabilities.TryGetProperty("formatSnippet", out _));

        await formatter.ShutdownAsync(2);
        Assert.Equal(string.Empty, await formatter.ReadRemainingStdoutAsync());
        Assert.Equal(string.Empty, await formatter.ReadStderrAsync());
    }

    [Fact]
    public async Task FormatDocumentMatchesCoreAndPassesThroughEditorConfig()
    {
        const string source = "class Demo{void Run(int left,int right){}}";
        var editorConfig = new Dictionary<string, string>
        {
            ["csharp_new_line_before_open_brace"] = "none",
            ["csharp_space_after_comma"] = "false",
        };
        var fallback = new EditorFallback
        {
            InsertSpaces = true,
            TabSize = 4,
            MaxLineLength = 120,
            LineEnding = "\n",
        };
        var expected = await new FormattingEngine().FormatAsync(
            new FormattingRequest(FormattingLanguage.CSharp, source, editorConfig, fallback));
        await using var formatter = FormatterProcess.Start();
        await formatter.HandshakeAsync();

        await formatter.SendAsync(new
        {
            id = 2,
            method = "formatDocument",
            @params = new
            {
                language = "csharp",
                source,
                resolvedEditorConfig = editorConfig,
                editorFallback = new
                {
                    insertSpaces = true,
                    tabSize = 4,
                    maxLineLength = 120,
                    lineEnding = "\n",
                },
            },
        });
        using var response = await formatter.ReadAsync();
        var changes = response.RootElement.GetProperty("result").GetProperty("changes").EnumerateArray()
            .Select(change => new
            {
                Start = change.GetProperty("span").GetProperty("start").GetInt32(),
                Length = change.GetProperty("span").GetProperty("length").GetInt32(),
                NewText = change.GetProperty("newText").GetString(),
            }).ToArray();

        Assert.Equal(expected.Changes.Count, changes.Length);
        for (var index = 0; index < changes.Length; index++)
        {
            Assert.Equal(expected.Changes[index].Span.Start, changes[index].Start);
            Assert.Equal(expected.Changes[index].Span.Length, changes[index].Length);
            Assert.Equal(expected.Changes[index].NewText, changes[index].NewText);
        }

        Assert.Contains("class Demo {", changes.Single().NewText, StringComparison.Ordinal);
        Assert.Contains("Run(int left,int right)", changes.Single().NewText, StringComparison.Ordinal);
        await formatter.ShutdownAsync(3);
    }

    [Fact]
    public async Task ProtocolErrorsDoNotStopLaterValidRequests()
    {
        await using var formatter = FormatterProcess.Start();

        await formatter.SendAsync(new
        {
            id = 1,
            method = "formatDocument",
            @params = CreateFormatParams("csharp", "class Demo{}"),
        });
        using var handshakeRequired = await formatter.ReadAsync();
        Assert.Equal("handshakeRequired", GetErrorCode(handshakeRequired));

        await formatter.SendAsync(new { id = 2, method = "unknown", @params = new { } });
        using var methodNotFound = await formatter.ReadAsync();
        Assert.Equal("methodNotFound", GetErrorCode(methodNotFound));

        await formatter.SendRawJsonAsync("{not-json}");
        using var invalidMessage = await formatter.ReadAsync();
        Assert.Equal(JsonValueKind.Null, invalidMessage.RootElement.GetProperty("id").ValueKind);
        Assert.Equal("invalidMessage", GetErrorCode(invalidMessage));

        await formatter.SendAsync(new
        {
            id = 3,
            method = "handshake",
            @params = new { protocolVersion = 99 },
        });
        using var mismatch = await formatter.ReadAsync();
        Assert.Equal("protocolVersionMismatch", GetErrorCode(mismatch));

        await formatter.HandshakeAsync(4);
        await formatter.SendAsync(new
        {
            id = 5,
            method = "formatDocument",
            @params = CreateFormatParams("unknown", "class Demo{}"),
        });
        using var unsupportedLanguage = await formatter.ReadAsync();
        Assert.Equal("unsupportedLanguage", GetErrorCode(unsupportedLanguage));

        await formatter.SendAsync(new
        {
            id = 6,
            method = "formatDocument",
            @params = CreateFormatParams("csharp", "class Demo{}"),
        });
        using var validFormat = await formatter.ReadAsync();
        Assert.True(validFormat.RootElement.TryGetProperty("result", out _));
        await formatter.ShutdownAsync(7);
    }

    [Fact]
    public async Task CancelReturnsRequestCancelledAndServerRemainsUsable()
    {
        await using var formatter = FormatterProcess.Start();
        await formatter.HandshakeAsync();
        var source = CreateLargeSource(40000);

        await formatter.SendAsync(new
        {
            id = 2,
            method = "formatDocument",
            @params = CreateFormatParams("csharp", source),
        });
        await formatter.SendAsync(new
        {
            method = "cancel",
            @params = new { id = 2 },
        });
        using var cancelled = await formatter.ReadAsync();
        Assert.Equal(2, cancelled.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("requestCancelled", GetErrorCode(cancelled));

        await formatter.SendAsync(new
        {
            id = 3,
            method = "formatDocument",
            @params = CreateFormatParams("csharp", "class Demo{}"),
        });
        using var nextResponse = await formatter.ReadAsync();
        Assert.True(nextResponse.RootElement.TryGetProperty("result", out _));
        await formatter.ShutdownAsync(4);
    }

    [Fact]
    public async Task ShutdownReturnsResponseAndExitsWithZero()
    {
        await using var formatter = FormatterProcess.Start();
        await formatter.HandshakeAsync();

        await formatter.ShutdownAsync(100);

        Assert.True(formatter.HasExited);
        Assert.Equal(0, formatter.ExitCode);
    }

    [Fact]
    public async Task StdinEofCancelsActiveRequestsAndExitsNormally()
    {
        await using var formatter = FormatterProcess.Start();
        await formatter.HandshakeAsync();
        await formatter.SendAsync(new
        {
            id = 2,
            method = "formatDocument",
            @params = CreateFormatParams("csharp", CreateLargeSource(40000)),
        });

        formatter.CloseInput();
        using var cancelled = await formatter.ReadAsync();
        Assert.Equal("requestCancelled", GetErrorCode(cancelled));
        await formatter.WaitForExitAsync();

        Assert.Equal(0, formatter.ExitCode);
    }

    [Fact]
    public async Task DamagedFramingTerminatesProcessWithNonZeroExitCode()
    {
        await using var formatter = FormatterProcess.Start();

        await formatter.WriteRawAsync("Content-Length: invalid\r\n\r\n{}");
        formatter.CloseInput();
        await formatter.WaitForExitAsync();

        Assert.NotEqual(0, formatter.ExitCode);
        Assert.Contains("Content-Length", await formatter.ReadStderrAsync(), StringComparison.Ordinal);
    }

    private static object CreateFormatParams(string language, string source)
    {
        return new
        {
            language,
            source,
            resolvedEditorConfig = new Dictionary<string, string>(),
            editorFallback = new
            {
                insertSpaces = true,
                tabSize = 4,
                lineEnding = "\n",
            },
        };
    }

    private static string CreateLargeSource(int methodCount)
    {
        var source = new StringBuilder("class Large {");
        for (var index = 0; index < methodCount; index++)
        {
            source.Append("void M").Append(index).Append("(){int value=").Append(index).Append(";}");
        }

        return source.Append('}').ToString();
    }

    private static string? GetErrorCode(JsonDocument response)
    {
        return response.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    private sealed class FormatterProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly Stream _input;
        private readonly Stream _output;

        private FormatterProcess(Process process)
        {
            _process = process;
            _input = process.StandardInput.BaseStream;
            _output = process.StandardOutput.BaseStream;
        }

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.ExitCode;

        public static FormatterProcess Start()
        {
            var executable = Path.Combine(
                AppContext.BaseDirectory,
                OperatingSystem.IsWindows() ? "CSharpWorkbench.Formatter.exe" : "CSharpWorkbench.Formatter");
            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("server");
            return new FormatterProcess(Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start formatter process."));
        }

        public Task HandshakeAsync(int id = 1)
        {
            return SendAndDiscardResponseAsync(new
            {
                id,
                method = "handshake",
                @params = new { protocolVersion = 1 },
            });
        }

        public async Task ShutdownAsync(int id)
        {
            await SendAsync(new { id, method = "shutdown", @params = new { } });
            using var response = await ReadAsync();
            Assert.Equal(id, response.RootElement.GetProperty("id").GetInt32());
            Assert.True(response.RootElement.TryGetProperty("result", out _));
            await WaitForExitAsync();
        }

        public Task SendAsync<T>(T message)
        {
            return SendBytesAsync(JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions));
        }

        public Task SendRawJsonAsync(string json)
        {
            return SendBytesAsync(Encoding.UTF8.GetBytes(json));
        }

        public async Task WriteRawAsync(string content)
        {
            await _input.WriteAsync(Encoding.ASCII.GetBytes(content));
            await _input.FlushAsync();
        }

        public void CloseInput()
        {
            _process.StandardInput.Close();
        }

        public async Task<JsonDocument> ReadAsync()
        {
            var header = await ReadLineAsync();
            Assert.StartsWith("Content-Length:", header, StringComparison.Ordinal);
            var contentLength = int.Parse(header["Content-Length:".Length..].Trim());
            Assert.Equal(string.Empty, await ReadLineAsync());
            var body = new byte[contentLength];
            var offset = 0;
            while (offset < body.Length)
            {
                var bytesRead = await _output.ReadAsync(body.AsMemory(offset));
                Assert.NotEqual(0, bytesRead);
                offset += bytesRead;
            }

            return JsonDocument.Parse(body);
        }

        public async Task<string> ReadRemainingStdoutAsync()
        {
            return await _process.StandardOutput.ReadToEndAsync();
        }

        public async Task<string> ReadStderrAsync()
        {
            return await _process.StandardError.ReadToEndAsync();
        }

        public Task WaitForExitAsync()
        {
            return _process.WaitForExitAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }

            _process.Dispose();
        }

        private async Task SendAndDiscardResponseAsync<T>(T message)
        {
            await SendAsync(message);
            using var response = await ReadAsync();
            Assert.True(response.RootElement.TryGetProperty("result", out _));
        }

        private async Task SendBytesAsync(byte[] body)
        {
            var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
            await _input.WriteAsync(header);
            await _input.WriteAsync(body);
            await _input.FlushAsync();
        }

        private async Task<string> ReadLineAsync()
        {
            using var line = new MemoryStream();
            var sawCarriageReturn = false;
            while (true)
            {
                var next = new byte[1];
                var bytesRead = await _output.ReadAsync(next);
                Assert.NotEqual(0, bytesRead);
                if (sawCarriageReturn)
                {
                    Assert.Equal((byte)'\n', next[0]);
                    return Encoding.ASCII.GetString(line.ToArray());
                }

                if (next[0] == (byte)'\r')
                {
                    sawCarriageReturn = true;
                }
                else
                {
                    line.WriteByte(next[0]);
                }
            }
        }
    }
}
