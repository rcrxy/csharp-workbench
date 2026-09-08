import { existsSync, rmSync, statSync } from "node:fs";
import { resolve } from "node:path";
import process from "node:process";
import { spawnSync } from "node:child_process";

const target = process.argv[2];
const targets = {
    "win32-x64": {
        platform: "win32",
        rid: "win-x64",
        executable: "CSharpWorkbench.Formatter.exe",
    },
    "linux-x64": {
        platform: "linux",
        rid: "linux-x64",
        executable: "CSharpWorkbench.Formatter",
    },
};
const configuration = targets[target];

if (!configuration) {
    console.error(`Unsupported Formatter runtime target: ${target ?? "<missing>"}.`);
    process.exit(1);
}

if (process.platform !== configuration.platform) {
    console.error(
        `Formatter runtime target ${target} must be published on ${configuration.platform}; current platform is ${process.platform}.`,
    );
    process.exit(1);
}

const projectPath = resolve(
    "formatting",
    "src",
    "CSharpWorkbench.Formatter",
    "CSharpWorkbench.Formatter.csproj",
);
const outputPath = resolve("runtime", target);
const executablePath = resolve(outputPath, configuration.executable);

rmSync(outputPath, { recursive: true, force: true });

const dotnetCommand = process.platform === "win32" ? "dotnet.exe" : "dotnet";
const publish = spawnSync(
    dotnetCommand,
    [
        "publish",
        projectPath,
        "--configuration",
        "Release",
        "--runtime",
        configuration.rid,
        "--self-contained",
        "true",
        "--output",
        outputPath,
        "-p:DebugType=None",
    ],
    { stdio: "inherit" },
);

if (publish.error) {
    console.error(publish.error.message);
    process.exit(1);
}

if (publish.status !== 0) {
    process.exit(publish.status ?? 1);
}

if (!existsSync(executablePath)) {
    console.error(`Published Formatter executable is missing: ${executablePath}`);
    process.exit(1);
}

if (target === "linux-x64" && process.platform === "linux" && (statSync(executablePath).mode & 0o111) === 0) {
    console.error(`Published Linux Formatter is not executable: ${executablePath}`);
    process.exit(1);
}

console.log(`Published Formatter runtime: target=${target}, rid=${configuration.rid}, executable=${executablePath}`);
