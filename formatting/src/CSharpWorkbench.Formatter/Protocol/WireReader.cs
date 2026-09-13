using System.Globalization;
using System.Text;

namespace CSharpWorkbench.Formatter.Protocol;

internal sealed class WireReader
{
    internal const int MaxFrameSize = 16 * 1024 * 1024;

    private readonly Stream _stream;

    public WireReader(Stream stream)
    {
        _stream = stream;
    }

    public async Task<byte[]?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var header = await ReadLineAsync(allowCleanEof: true, cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            return null;
        }

        const string prefix = "Content-Length:";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new WireProtocolException("Missing Content-Length header.");
        }

        var value = header[prefix.Length..].Trim();
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var contentLength))
        {
            throw new WireProtocolException("Content-Length must be a decimal integer.");
        }

        if (contentLength <= 0 || contentLength > MaxFrameSize)
        {
            throw new WireProtocolException($"Content-Length must be between 1 and {MaxFrameSize} bytes.");
        }

        var separator = await ReadLineAsync(allowCleanEof: false, cancellationToken).ConfigureAwait(false);
        if (separator!.Length != 0)
        {
            throw new WireProtocolException("Expected an empty line after Content-Length.");
        }

        var body = new byte[contentLength];
        var offset = 0;
        while (offset < body.Length)
        {
            var bytesRead = await _stream.ReadAsync(body.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new WireProtocolException("Unexpected end of stream while reading frame body.");
            }

            offset += bytesRead;
        }

        return body;
    }

    private async Task<string?> ReadLineAsync(bool allowCleanEof, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var sawCarriageReturn = false;

        while (true)
        {
            var next = new byte[1];
            var bytesRead = await _stream.ReadAsync(next.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                if (allowCleanEof && buffer.Length == 0 && !sawCarriageReturn)
                {
                    return null;
                }

                throw new WireProtocolException("Unexpected end of stream while reading frame header.");
            }

            if (sawCarriageReturn)
            {
                if (next[0] != (byte)'\n')
                {
                    throw new WireProtocolException("Frame headers must use CRLF line endings.");
                }

                return Encoding.ASCII.GetString(buffer.ToArray());
            }

            if (next[0] == (byte)'\r')
            {
                sawCarriageReturn = true;
            }
            else
            {
                buffer.WriteByte(next[0]);
            }
        }
    }
}
