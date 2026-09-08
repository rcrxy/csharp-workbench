import { mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import process from "node:process";
import { spawnSync } from "node:child_process";

const target = process.argv[2];
const supportedTargets = new Set(["win32-x64", "linux-x64"]);

if (!target || !supportedTargets.has(target)) {
    console.error(`Unsupported VSIX target: ${target ?? "<missing>"}.`);
    process.exit(1);
}

const packageManifest = JSON.parse(readFileSync(resolve("package.json"), "utf8"));
const outputDirectory = resolve("dist");
const outputPath = resolve(outputDirectory, `csharp-workbench-${packageManifest.version}-${target}.vsix`);
const foreignTarget = target === "win32-x64" ? "linux-x64" : "win32-x64";
const temporaryDirectory = mkdtempSync(join(tmpdir(), "csharp-workbench-vsce-"));
const ignoreFile = join(temporaryDirectory, ".vscodeignore");
mkdirSync(outputDirectory, { recursive: true });

writeFileSync(
    ignoreFile,
    `${readFileSync(resolve(".vscodeignore"), "utf8").trimEnd()}\nruntime/${foreignTarget}/**\n`,
    "utf8",
);

let packageResult;
try {
    packageResult = spawnSync(
        process.execPath,
        [
            resolve("node_modules", "@vscode", "vsce", "vsce"),
            "package",
            "--target",
            target,
            "--ignore-other-target-folders",
            "--ignoreFile",
            ignoreFile,
            "--out",
            outputPath,
        ],
        { stdio: "inherit" },
    );
} finally {
    rmSync(temporaryDirectory, { recursive: true, force: true });
}

if (packageResult.error) {
    console.error(packageResult.error.message);
    process.exit(1);
}

if (packageResult.status !== 0) {
    process.exit(packageResult.status ?? 1);
}

console.log(`Packaged VSIX: target=${target}, version=${packageManifest.version}, path=${outputPath}`);
