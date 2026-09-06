using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSharpWorkbench.Formatter.Protocol;

internal sealed class WireMessage
{
    public int? Id { get; init; }

    public required string Method { get; init; }

    public required JsonElement Params { get; init; }
}

internal sealed class WireResponse
{
    public int? Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WireError? Error { get; init; }
}

internal sealed class WireError
{
    public required string Code { get; init; }

    public required string Message { get; init; }
}

internal sealed class HandshakeParams
{
    public required int ProtocolVersion { get; init; }
}

internal sealed class HandshakeResult
{
    public required int ProtocolVersion { get; init; }

    public required string FormatterVersion { get; init; }

    public required FormatterCapabilities Capabilities { get; init; }
}

internal sealed class FormatterCapabilities
{
    public bool FormatDocument { get; init; }

    public required IReadOnlyList<string> Languages { get; init; }
}

internal sealed class FormatDocumentParams
{
    public required string Language { get; init; }

    public required string Source { get; init; }

    public IReadOnlyDictionary<string, string>? ResolvedEditorConfig { get; init; }

    public WireEditorFallback? EditorFallback { get; init; }
}

internal sealed class CancelParams
{
    public required int Id { get; init; }
}

internal sealed class WireEditorFallback
{
    public bool? InsertSpaces { get; init; }

    public int? TabSize { get; init; }

    public int? MaxLineLength { get; init; }

    public string? LineEnding { get; init; }

    public bool? InsertFinalNewline { get; init; }

    public bool? TrimTrailingWhitespace { get; init; }

    public string? Charset { get; init; }
}

internal sealed class FormatDocumentResult
{
    public required IReadOnlyList<WireTextChange> Changes { get; init; }
}

internal sealed class WireTextSpan
{
    public int Start { get; init; }

    public int Length { get; init; }
}

internal sealed class WireTextChange
{
    public required WireTextSpan Span { get; init; }

    public required string NewText { get; init; }
}
