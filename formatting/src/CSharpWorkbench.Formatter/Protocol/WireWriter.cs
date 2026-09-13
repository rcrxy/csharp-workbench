using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CSharpWorkbench.Formatter.Protocol;

internal sealed class WireWriter : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public WireWriter(Stream stream)
    {
        _stream = stream;
    }

    public async Task WriteAsync<T>(T message, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}
