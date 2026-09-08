import assert from "node:assert/strict";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import * as path from "node:path";
import { describe, it } from "node:test";
import { resolveRawEditorConfig } from "../../core/editorConfig";

describe("resolveRawEditorConfig", () => {
    it("resolves real EditorConfig files as raw string values", async () => {
        const directory = await mkdtemp(path.join(tmpdir(), "csharp-workbench-raw-editorconfig-"));

        try {
            await writeFile(
                path.join(directory, ".editorconfig"),
                [
                    "root = true",
                    "",
                    "[*.cs]",
                    "indent_size = 2",
                    "insert_final_newline = true",
                    "csharp_space_after_comma = false",
                ].join("\n"),
            );

            const properties = await resolveRawEditorConfig(fileResource(path.join(directory, "Example.cs")));

            assert.equal(properties.indent_size, "2");
            assert.equal(properties.insert_final_newline, "true");
            assert.equal(properties.csharp_space_after_comma, "false");
        } finally {
            await rm(directory, { recursive: true, force: true });
        }
    });

    it("honors section matching and root boundaries", async () => {
        const directory = await mkdtemp(path.join(tmpdir(), "csharp-workbench-editorconfig-root-"));
        const projectDirectory = path.join(directory, "project");

        try {
            await mkdir(projectDirectory);
            await writeFile(path.join(directory, ".editorconfig"), ["[*]", "parent_only = true"].join("\n"));
            await writeFile(
                path.join(projectDirectory, ".editorconfig"),
                ["root = true", "", "[*]", "indent_size = 4", "", "[*.cs]", "indent_size = 2"].join("\n"),
            );

            const csharp = await resolveRawEditorConfig(fileResource(path.join(projectDirectory, "Example.cs")));
            const razor = await resolveRawEditorConfig(fileResource(path.join(projectDirectory, "Example.razor")));

            assert.equal(csharp.indent_size, "2");
            assert.equal(razor.indent_size, "4");
            assert.equal(csharp.parent_only, undefined);
            assert.equal(razor.parent_only, undefined);
        } finally {
            await rm(directory, { recursive: true, force: true });
        }
    });

    it("returns an empty map for non-file resources", async () => {
        assert.deepEqual(await resolveRawEditorConfig({ scheme: "untitled" } as never), {});
    });

    it("returns an empty map when EditorConfig parsing fails", async () => {
        assert.deepEqual(await resolveRawEditorConfig(fileResource("invalid\0path.cs")), {});
    });
});

function fileResource(fsPath: string): never {
    return { scheme: "file", fsPath } as never;
}
