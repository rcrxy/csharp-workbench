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
                info: message => context.diagnostic(message),
                warn: message => context.diagnostic(message),
                error: message => context.diagnostic(message),
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
            const razorSource = '<Widget Value = "@(left+right)" />';
            const selectedStart = razorSource.indexOf("Value");
            const csharpRange = await client.formatRange({
                ...request,
                span: { start: request.source.indexOf("void"), length: "void Run(int left,int right){}".length },
            });
            const razorDocument = await client.formatDocument({
                ...request,
                language: "razor",
                source: razorSource,
                resolvedEditorConfig: {},
            });
            const cshtmlDocument = await client.formatDocument({
                ...request,
                language: "cshtml",
                source: razorSource,
                resolvedEditorConfig: {},
            });
            const razorRange = await client.formatRange({
                language: "razor",
                source: razorSource,
                span: { start: selectedStart, length: "Value".length },
                resolvedEditorConfig: {},
                editorFallback: {
                    insertSpaces: true,
                    tabSize: 4,
                    lineEnding: "\n" as const,
                },
            });
            const cshtmlRange = await client.formatRange({
                language: "cshtml",
                source: razorSource,
                span: { start: selectedStart, length: "Value".length },
                resolvedEditorConfig: {},
                editorFallback: {
                    insertSpaces: true,
                    tabSize: 4,
                    lineEnding: "\n" as const,
                },
            });
            const warmOperations = [
                () => client.formatDocument(request),
                () => client.formatDocument({ ...request, language: "razor", source: razorSource, resolvedEditorConfig: {} }),
                () => client.formatDocument({ ...request, language: "cshtml", source: razorSource, resolvedEditorConfig: {} }),
                () =>
                    client.formatRange({
                        ...request,
                        span: { start: request.source.indexOf("void"), length: "void Run(int left,int right){}".length },
                    }),
                () =>
                    client.formatRange({
                        ...request,
                        language: "razor",
                        source: razorSource,
                        span: { start: selectedStart, length: "Value".length },
                        resolvedEditorConfig: {},
                    }),
                () =>
                    client.formatRange({
                        ...request,
                        language: "cshtml",
                        source: razorSource,
                        span: { start: selectedStart, length: "Value".length },
                        resolvedEditorConfig: {},
                    }),
            ];

            for (let index = 0; index < 24; index++) {
                await warmOperations[index % warmOperations.length]!();
                assert.equal(client.status, "ready");
            }

            assert.equal(client.status, "ready");
            assert.equal(client.info?.protocolVersion, 1);
            assert.equal(client.info?.formatterVersion, "0.2.0");
            assert.equal(client.info?.capabilities.formatDocument, true);
            assert.deepEqual(first, second);
            assert.equal(first.changes.length, 1);
            assert.match(first.changes[0]!.newText, /class Demo \{/u);
            assert.match(first.changes[0]!.newText, /Run\(int left,int right\)/u);
            assert.equal(csharpRange.changes.length, 1);
            assert.equal(razorDocument.changes.length, 1);
            assert.deepEqual(razorDocument, cshtmlDocument);
            assert.deepEqual(razorRange, cshtmlRange);
            assert.equal(razorRange.changes.length, 1);
            assert.equal(razorRange.changes[0]!.newText, '<Widget Value="@(left + right)" />');
            assert.ok(razorRange.changes[0]!.span.start < selectedStart);
        } finally {
            await client.dispose();
        }

        assert.equal(client.status, "disposed");
    });
});
