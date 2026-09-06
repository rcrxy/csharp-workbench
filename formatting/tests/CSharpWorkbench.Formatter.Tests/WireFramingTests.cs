using System.Text;
using System.Text.Json;
using CSharpWorkbench.Formatter.Protocol;
using Xunit;

namespace CSharpWorkbench.Formatter.Tests;

public sealed class WireFramingTests
{
    [Fact]
    public async Task WriterUsesUtf8ByteLengthAndReaderRoundTripsNonAsciiJson()
    {
        await using var stream = new MemoryStream();
        using (var writer = new WireWriter(stream))
        {
            await writer.WriteAsync(new { text = "中文😀" });
        }

        var bytes = stream.ToArray();
        var separator = Encoding.ASCII.GetBytes("\r\n\r\n");
        var separatorIndex = bytes.AsSpan().IndexOf(separator);
        var header = Encoding.ASCII.GetString(bytes, 0, separatorIndex);
        var bodyLength = bytes.Length - separatorIndex - separator.Length;

        Assert.Equal($"Content-Length: {bodyLength}", header);

        stream.Position = 0;
        var body = await new WireReader(stream).ReadAsync();
        using var document = JsonDocument.Parse(body!);
        Assert.Equal("中文😀", document.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ReaderSupportsPartialReadsAndConsecutiveFrames()
    {
        var first = CreateFrame("{\"id\":1}");
        var second = CreateFrame("{\"id\":2}");
        await using var stream = new ChunkedReadStream(first.Concat(second).ToArray(), 2);
        var reader = new WireReader(stream);

        var firstBody = await reader.ReadAsync();
        var secondBody = await reader.ReadAsync();
        var eof = await reader.ReadAsync();

        Assert.Equal("{\"id\":1}", Encoding.UTF8.GetString(firstBody!));
        Assert.Equal("{\"id\":2}", Encoding.UTF8.GetString(secondBody!));
        Assert.Null(eof);
    }

    [Fact]
    public async Task ReaderRejectsInvalidOrOversizedContentLength()
    {
        await Assert.ThrowsAsync<WireProtocolException>(() => ReadAsync("Content-Length: nope\r\n\r\n{}"));
        await Assert.ThrowsAsync<WireProtocolException>(() => ReadAsync("Content-Length: 0\r\n\r\n"));
        await Assert.ThrowsAsync<WireProtocolException>(() =>
            ReadAsync($"Content-Length: {WireReader.MaxFrameSize + 1}\r\n\r\n"));
        await Assert.ThrowsAsync<WireProtocolException>(() => ReadAsync("X-Length: 2\r\n\r\n{}"));
    }

    [Fact]
    public async Task ReaderRejectsUnexpectedHeaderOrBodyEof()
    {
        await Assert.ThrowsAsync<WireProtocolException>(() => ReadAsync("Content-Length: 2\r\n"));
        await Assert.ThrowsAsync<WireProtocolException>(() => ReadAsync("Content-Length: 4\r\n\r\n{}"));
    }

    private static byte[] CreateFrame(string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        return Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n").Concat(body).ToArray();
    }

    private static async Task ReadAsync(string content)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await new WireReader(stream).ReadAsync();
    }

    private sealed class ChunkedReadStream : Stream
    {
        private readonly byte[] _content;
        private readonly int _chunkSize;
        private int _position;

        public ChunkedReadStream(byte[] content, int chunkSize)
        {
            _content = content;
            _chunkSize = chunkSize;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _content.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadChunk(buffer.AsSpan(offset, count));
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ReadChunk(buffer.Span));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int ReadChunk(Span<byte> buffer)
        {
            var remaining = _content.Length - _position;
            var bytesToRead = Math.Min(Math.Min(buffer.Length, _chunkSize), remaining);
            _content.AsSpan(_position, bytesToRead).CopyTo(buffer);
            _position += bytesToRead;
            return bytesToRead;
        }
    }
}
