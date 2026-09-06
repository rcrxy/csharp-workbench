import assert from "node:assert/strict";
import { existsSync } from "node:fs";
import { join } from "node:path";
import { describe, it } from "node:test";
import { FormatterClient } from "../../features/formatting/client/formatterClient";

const formatterDll = join(
    process.cwd(),
    "formatting",
    "src",
    "CSharpWorkbench.Formatter",
    "bin",
    "Release",
    "net10.0",
    "CSharpWorkbench.Formatter.dll",
);

describe("FormatterClient real Formatter integration", () => {
    it("starts the Formatter, formats through Core, reuses it, and disposes it", async context => {
        assert.equal(existsSync(formatterDll), true, `Missing Formatter build: ${formatterDll}`);
        const client = new FormatterClient({
            launch: {
                command: "dotnet",
                args: [formatterDll],
            },
            handshakeTimeoutMs: 10_000,
            requestTimeoutMs: 10_000,
            shutdownTimeoutMs: 2_000,
            log: {
                info: context.diagnostic,
                warn: context.diagnostic,
                error: context.diagnostic,
            },
        });

        try {
            const request = {
                language: "csharp",
                source: "class Demo{void Run(int left,int right){}}",
                resolvedEditorConfig: {
                    csharp_new_line_before_open_brace: "none",
                    csharp_space_after_comma: "false",
                },
                editorFallback: {
                    insertSpaces: true,
                    tabSize: 4,
                    lineEnding: "\n" as const,
                },
            };
            const first = await client.formatDocument(request);
            const second = await client.formatDocument(request);

            assert.equal(client.status, "ready");
            assert.equal(client.info?.protocolVersion, 1);
            assert.equal(client.info?.formatterVersion, "0.2.0");
            assert.equal(client.info?.capabilities.formatDocument, true);
            assert.deepEqual(first, second);
            assert.equal(first.changes.length, 1);
            assert.match(first.changes[0]!.newText, /class Demo \{/u);
            assert.match(first.changes[0]!.newText, /Run\(int left,int right\)/u);
        } finally {
            await client.dispose();
        }

        assert.equal(client.status, "disposed");
    });
});
