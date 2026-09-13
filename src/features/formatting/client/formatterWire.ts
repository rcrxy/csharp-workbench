import { FormatterClientError } from "./formatterProtocol";

export const MAX_FRAME_SIZE = 16 * 1024 * 1024;

const FRAME_SEPARATOR = Buffer.from("\r\n\r\n", "ascii");

export function encodeWireFrame(message: unknown): Buffer {
    const body = Buffer.from(JSON.stringify(message), "utf8");
    if (body.length === 0 || body.length > MAX_FRAME_SIZE) {
        throw new FormatterClientError("protocolFailure", "Wire message exceeds the maximum frame size.");
    }

    const header = Buffer.from(`Content-Length: ${body.length}\r\n\r\n`, "ascii");
    return Buffer.concat([header, body]);
}

export class FormatterWireDecoder {
    private buffer = Buffer.alloc(0);

    public feed(chunk: Buffer): Buffer[] {
        this.buffer = Buffer.concat([this.buffer, chunk]);
        const frames: Buffer[] = [];

        while (true) {
            const separatorIndex = this.buffer.indexOf(FRAME_SEPARATOR);
            if (separatorIndex < 0) {
                if (this.buffer.length > 4096) {
                    throw new FormatterClientError("protocolFailure", "Wire header is too large.");
                }

                break;
            }

            const header = this.buffer.subarray(0, separatorIndex).toString("ascii");
            const contentLength = parseContentLength(header);
            const bodyStart = separatorIndex + FRAME_SEPARATOR.length;
            if (this.buffer.length - bodyStart < contentLength) {
                break;
            }

            frames.push(this.buffer.subarray(bodyStart, bodyStart + contentLength));
            this.buffer = this.buffer.subarray(bodyStart + contentLength);
        }

        return frames;
    }
}

function parseContentLength(header: string): number {
    if (!header.startsWith("Content-Length:") || header.includes("\r") || header.includes("\n")) {
        throw new FormatterClientError("protocolFailure", "Invalid Formatter frame header.");
    }

    const value = header.slice("Content-Length:".length).trim();
    if (!/^[0-9]+$/u.test(value)) {
        throw new FormatterClientError("protocolFailure", "Content-Length must be a decimal integer.");
    }

    const contentLength = Number(value);
    if (!Number.isSafeInteger(contentLength) || contentLength <= 0 || contentLength > MAX_FRAME_SIZE) {
        throw new FormatterClientError("protocolFailure", "Content-Length is outside the supported frame size.");
    }

    return contentLength;
}
