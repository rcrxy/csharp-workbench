import assert from "node:assert/strict";
import { join } from "node:path";
import { describe, it } from "node:test";
import {
    createFormatterLaunchSpec,
    resolveBundledFormatterTarget,
} from "../../features/formatting/client/formatterRuntime";

describe("Formatter runtime resolution", () => {
    it("maps Windows x64 to the Windows VS Code target", () => {
        assert.deepEqual(resolveBundledFormatterTarget("win32", "x64"), {
            target: "win32-x64",
            executable: "CSharpWorkbench.Formatter.exe",
        });
    });

    it("maps Linux x64 to the Linux VS Code target", () => {
        assert.deepEqual(resolveBundledFormatterTarget("linux", "x64"), {
            target: "linux-x64",
            executable: "CSharpWorkbench.Formatter",
        });
    });

    it("does not resolve unsupported platforms or architectures", () => {
        assert.equal(resolveBundledFormatterTarget("win32", "arm64"), undefined);
        assert.equal(resolveBundledFormatterTarget("linux", "arm64"), undefined);
        assert.equal(resolveBundledFormatterTarget("darwin", "x64"), undefined);
    });

    it("keeps the development dotnet DLL launch shape", () => {
        const extensionPath = join("test", "extension");

        assert.deepEqual(createFormatterLaunchSpec(extensionPath, true, "darwin", "arm64"), {
            command: "dotnet",
            args: [
                join(
                    extensionPath,
                    "formatting",
                    "src",
                    "CSharpWorkbench.Formatter",
                    "bin",
                    "Release",
                    "net10.0",
                    "CSharpWorkbench.Formatter.dll",
                ),
            ],
        });
    });

    it("resolves production launch paths from the extension directory", () => {
        const extensionPath = join("test", "extension");

        assert.deepEqual(createFormatterLaunchSpec(extensionPath, false, "win32", "x64"), {
            command: join(extensionPath, "runtime", "win32-x64", "CSharpWorkbench.Formatter.exe"),
            args: [],
        });
        assert.deepEqual(createFormatterLaunchSpec(extensionPath, false, "linux", "x64"), {
            command: join(extensionPath, "runtime", "linux-x64", "CSharpWorkbench.Formatter"),
            args: [],
        });
        assert.equal(createFormatterLaunchSpec(extensionPath, false, "linux", "arm64"), undefined);
    });
});
