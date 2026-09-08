import { join } from "node:path";
import type { FormatterLaunchSpec } from "./formatterProtocol";

export interface BundledFormatterTarget {
    readonly target: "win32-x64" | "linux-x64";
    readonly executable: "CSharpWorkbench.Formatter.exe" | "CSharpWorkbench.Formatter";
}

export function resolveBundledFormatterTarget(platform: string, arch: string): BundledFormatterTarget | undefined {
    if (platform === "win32" && arch === "x64") {
        return {
            target: "win32-x64",
            executable: "CSharpWorkbench.Formatter.exe",
        };
    }

    if (platform === "linux" && arch === "x64") {
        return {
            target: "linux-x64",
            executable: "CSharpWorkbench.Formatter",
        };
    }

    return undefined;
}

export function createFormatterLaunchSpec(
    extensionPath: string,
    development: boolean,
    platform: string,
    arch: string,
): FormatterLaunchSpec | undefined {
    if (development) {
        return {
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
        };
    }

    const bundledTarget = resolveBundledFormatterTarget(platform, arch);
    if (!bundledTarget) {
        return undefined;
    }

    return {
        command: join(extensionPath, "runtime", bundledTarget.target, bundledTarget.executable),
        args: [],
    };
}
