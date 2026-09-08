import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { resolve } from "node:path";
import process from "node:process";

const target = process.argv[2];
const executables = {
    "win32-x64": "CSharpWorkbench.Formatter.exe",
    "linux-x64": "CSharpWorkbench.Formatter",
};
const executable = executables[target];

if (!executable) {
    console.error(`Unsupported Formatter runtime target: ${target ?? "<missing>"}.`);
    process.exit(1);
}

const executablePath = resolve("runtime", target, executable);
if (!existsSync(executablePath)) {
    console.error(`Formatter runtime executable is missing: ${executablePath}`);
    process.exit(1);
}

const require = createRequire(import.meta.url);
const { FormatterClient } = require("../out/features/formatting/client/formatterClient.js");
const packageManifest = JSON.parse(readFileSync(resolve("package.json"), "utf8"));
const client = new FormatterClient({
    launch: {
        command: executablePath,
        args: [],
    },
    handshakeTimeoutMs: 10_000,
    requestTimeoutMs: 10_000,
    shutdownTimeoutMs: 2_000,
    log: {
        info: message => console.log(message),
        warn: message => console.warn(message),
        error: message => console.error(message),
    },
});

try {
    const result = await client.formatDocument({
        language: "csharp",
        source: "class Demo{void Run(int left,int right){}}",
        resolvedEditorConfig: {
            csharp_new_line_before_open_brace: "none",
            csharp_space_after_comma: "false",
        },
        editorFallback: {
            insertSpaces: true,
            tabSize: 4,
            lineEnding: "\n",
        },
    });

    assert.equal(client.status, "ready");
    assert.equal(client.info?.protocolVersion, 1);
    assert.equal(client.info?.formatterVersion, packageManifest.version);
    assert.equal(client.info?.capabilities.formatDocument, true);
    assert.equal(client.info?.capabilities.formatRange, true);
    assert.deepEqual([...client.info.capabilities.languages].sort(), ["csharp", "cshtml", "razor"]);
    assert.equal(result.changes.length, 1);
    assert.match(result.changes[0].newText, /class Demo \{/u);
    assert.match(result.changes[0].newText, /Run\(int left,int right\)/u);
} finally {
    await client.dispose();
}

assert.equal(client.status, "disposed");
console.log(
    `Verified Formatter runtime: target=${target}, version=${packageManifest.version}, executable=${executablePath}`,
);
