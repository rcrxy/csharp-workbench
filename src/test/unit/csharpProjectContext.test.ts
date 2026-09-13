import assert from "node:assert/strict";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import * as path from "node:path";
import { describe, it } from "node:test";
import { resolveNamespace } from "../../shared/csharp/csharpNamespace";
import { findNearestProject, resolveProjectContext, XmlProjectContextProvider } from "../../shared/csharp/projectContext";

describe("C# project context discovery", () => {
    it("finds the nearest csproj while walking up nested directories", async () => {
        const root = await createProjectFixture([
            ["src/App/App.csproj", "<Project><PropertyGroup><RootNamespace>Acme.App</RootNamespace></PropertyGroup></Project>"],
        ]);

        try {
            const nestedDirectory = path.join(root, "src", "App", "Features", "Orders");
            await mkdir(nestedDirectory, { recursive: true });

            assert.equal(await findNearestProject(nestedDirectory), path.join(root, "src", "App", "App.csproj"));
        } finally {
            await rm(root, { recursive: true, force: true });
        }
    });

    it("derives nested namespaces from RootNamespace and the relative directory", async () => {
        const root = await createProjectFixture([
            ["src/App/App.csproj", "<Project><PropertyGroup><RootNamespace>Acme.App</RootNamespace></PropertyGroup></Project>"],
        ]);

        try {
            const nestedDirectory = path.join(root, "src", "App", "Features", "Orders");
            await mkdir(nestedDirectory, { recursive: true });

            const context = await resolveProjectContext(nestedDirectory, [new XmlProjectContextProvider()]);

            assert.equal(context?.projectDirectory, path.join(root, "src", "App"));
            assert.equal(context?.rootNamespace, "Acme.App");
            assert.equal(resolveNamespace(nestedDirectory, context), "Acme.App.Features.Orders");
        } finally {
            await rm(root, { recursive: true, force: true });
        }
    });

    it("falls back to AssemblyName and then to the project file name", async () => {
        const root = await createProjectFixture([
            [
                "src/WithAssembly/WithAssembly.csproj",
                "<Project><PropertyGroup><AssemblyName>Acme.Assembly</AssemblyName></PropertyGroup></Project>",
            ],
            ["src/Plain/Plain.csproj", "<Project><PropertyGroup /></Project>"],
        ]);

        try {
            const assemblyContext = await resolveProjectContext(path.join(root, "src", "WithAssembly"), [
                new XmlProjectContextProvider(),
            ]);
            const plainContext = await resolveProjectContext(path.join(root, "src", "Plain"), [
                new XmlProjectContextProvider(),
            ]);

            assert.equal(resolveNamespace(path.join(root, "src", "WithAssembly"), assemblyContext), "Acme.Assembly");
            assert.equal(resolveNamespace(path.join(root, "src", "Plain"), plainContext), "Plain");
        } finally {
            await rm(root, { recursive: true, force: true });
        }
    });

    it("ignores directories outside the project directory", async () => {
        const root = await createProjectFixture([
            ["src/App/App.csproj", "<Project><PropertyGroup><RootNamespace>Acme.App</RootNamespace></PropertyGroup></Project>"],
        ]);

        try {
            const outsideDirectory = path.join(root, "outside");
            await mkdir(outsideDirectory, { recursive: true });

            const context = await resolveProjectContext(path.join(root, "src", "App"), [new XmlProjectContextProvider()]);

            assert.equal(resolveNamespace(outsideDirectory, context), "Acme.App");
        } finally {
            await rm(root, { recursive: true, force: true });
        }
    });
});

async function createProjectFixture(files: readonly (readonly [string, string])[]): Promise<string> {
    const root = await mkdtemp(path.join(tmpdir(), "csharp-workbench-project-"));

    for (const [relativePath, content] of files) {
        const filePath = path.join(root, ...relativePath.split("/"));
        await mkdir(path.dirname(filePath), { recursive: true });
        await writeFile(filePath, content);
    }

    return root;
}
