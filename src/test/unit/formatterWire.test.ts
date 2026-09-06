import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { FormatterClientError } from "../../features/formatting/client/formatterProtocol";
import { encodeWireFrame, FormatterWireDecoder, MAX_FRAME_SIZE } from "../../features/formatting/client/formatterWire";

describe("Formatter wire protocol", () => {
    it("encodes UTF-8 byte length and decodes split frames", () => {
        const first = encodeWireFrame({ id: 1, result: { text: "中文😀" } });
        const second = encodeWireFrame({ id: 2, result: { changes: [] } });
        const decoder = new FormatterWireDecoder();

        const firstSeparator = first.indexOf(Buffer.from("\r\n\r\n"));
        const firstFrames = decoder.feed(Buffer.concat([first, second.subarray(0, 8)]));
        assert.equal(firstFrames.length, 1);
        assert.equal(JSON.parse(firstFrames[0]!.toString("utf8")).result.text, "中文😀");

        const frames = decoder.feed(second.subarray(8));

        assert.equal(JSON.parse(frames[0]!.toString("utf8")).id, 2);
        assert.equal(firstSeparator > 0, true);
    });

    it("rejects malformed and oversized response frames", () => {
        const decoder = new FormatterWireDecoder();

        assert.throws(
            () => decoder.feed(Buffer.from("Content-Length: nope\r\n\r\n", "ascii")),
            (error: unknown) => error instanceof FormatterClientError && error.code === "protocolFailure",
        );
        assert.throws(
            () => decoder.feed(Buffer.from(`Content-Length: ${MAX_FRAME_SIZE + 1}\r\n\r\n`, "ascii")),
            (error: unknown) => error instanceof FormatterClientError && error.code === "protocolFailure",
        );
    });
});
