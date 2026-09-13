import assert from "node:assert/strict";
import { resolve } from "node:path";
import { execPath } from "node:process";
import { describe, it } from "node:test";
import { FormatterClient } from "../../features/formatting/client/formatterClient";
import { FormatterClientError } from "../../features/formatting/client/formatterProtocol";

const fixture = resolve(process.cwd(), "out/test/fixtures/formatterFixture.js");

function createClient(mode = "normal", options: Record<string, number> = {}): FormatterClient {
    return new FormatterClient({
        launch: {
            command: execPath,
            args: [fixture, mode],
        },
        handshakeTimeoutMs: options.handshakeTimeoutMs ?? 1_500,
        requestTimeoutMs: options.requestTimeoutMs ?? 1_500,
        shutdownTimeoutMs: options.shutdownTimeoutMs ?? 1_000,
        log: {
            info: () => undefined,
            warn: () => undefined,
            error: () => undefined,
        },
    });
}

const request = {
    language: "csharp",
    source: "class Demo{}",
    resolvedEditorConfig: {},
    editorFallback: { insertSpaces: true, tabSize: 4, lineEnding: "\n" as const },
};

describe("FormatterClient", () => {
    it("lazily starts, reuses one process, and exposes handshake info", async () => {
        const client = createClient();

        const first = await client.formatDocument(request);
        const second = await client.formatDocument(request);

        assert.deepEqual(first, { changes: [] });
        assert.deepEqual(second, { changes: [] });
        assert.equal(client.status, "ready");
        assert.equal(client.info?.formatterVersion, "fixture");
        await client.dispose();
        assert.equal(client.status, "disposed");
    });

    it("shares concurrent startup and supports cancellation without disabling", async () => {
        const client = createClient();
        const controller = new AbortController();
        const first = client.formatDocument(request, controller.signal);
        const second = client.formatDocument(request);
        controller.abort();

        await assert.rejects(first, (error: unknown) => error instanceof FormatterClientError && error.code === "cancelled");
        assert.deepEqual(await second, { changes: [] });
        assert.equal(client.status, "ready");
        await client.dispose();
    });

    it("distinguishes timeout and keeps the process usable", async () => {
        const client = createClient("timeout-format", { requestTimeoutMs: 50 });

        await assert.rejects(
            client.formatDocument(request),
            (error: unknown) => error instanceof FormatterClientError && error.code === "timeout",
        );
        assert.notEqual(client.status, "disabled");
        await client.dispose();
    });

    it("rejects pending requests after process crash and restarts on the next request", async () => {
        const client = createClient("crash-on-format");

        await assert.rejects(
            client.formatDocument({ ...request, source: "crash" }),
            (error: unknown) => error instanceof FormatterClientError && error.code === "processFailure",
        );
        assert.equal(client.status, "stopped");
        assert.deepEqual(await client.formatDocument(request), { changes: [] });
        await client.dispose();
    });

    it("rejects capability mismatches before sending the business request", async () => {
        const client = createClient("no-format-document");

        await assert.rejects(
            client.formatDocument(request),
            (error: unknown) => error instanceof FormatterClientError && error.code === "capabilityMismatch",
        );
        await client.dispose();
    });

    it("supports range formatting through the formatter protocol", async () => {
        const client = createClient();

        assert.deepEqual(
            await client.formatRange({
                language: "csharp",
                source: "class Demo{void Run(){if(true){}}}",
                span: { start: 15, length: 11 },
                resolvedEditorConfig: {},
                editorFallback: { insertSpaces: true, tabSize: 4, lineEnding: "\n" as const },
            }),
            { changes: [] },
        );
        await client.dispose();
    });

    it("rejects range capability mismatches before sending the business request", async () => {
        const client = createClient("no-format-range");

        await assert.rejects(
            client.formatRange({
                language: "csharp",
                source: "class Demo{}",
                span: { start: 0, length: 12 },
                resolvedEditorConfig: {},
                editorFallback: { insertSpaces: true, tabSize: 4, lineEnding: "\n" as const },
            }),
            (error: unknown) => error instanceof FormatterClientError && error.code === "capabilityMismatch",
        );
        await client.dispose();
    });

    it("rejects unsupported language requests before sending the business request", async () => {
        const client = createClient();

        await assert.rejects(
            client.formatDocument({ ...request, language: "razor" }),
            (error: unknown) => error instanceof FormatterClientError && error.code === "capabilityMismatch",
        );
        await client.dispose();
    });

    it("disables after two consecutive handshake failures", async () => {
        const client = createClient("no-handshake", { handshakeTimeoutMs: 50 });

        await assert.rejects(client.formatDocument(request));
        assert.equal(client.status, "stopped");
        await assert.rejects(
            client.formatDocument(request),
            (error: unknown) => error instanceof FormatterClientError && error.code === "handshakeFailure",
        );
        assert.equal(client.status, "disabled");
        await client.dispose();
    });

    it("makes dispose idempotent and rejects new requests", async () => {
        const client = createClient();
        await client.formatDocument(request);

        await Promise.all([client.dispose(), client.dispose()]);
        await assert.rejects(
            client.formatDocument(request),
            (error: unknown) => error instanceof FormatterClientError && error.code === "disposed",
        );
    });
});
