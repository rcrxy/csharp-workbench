import { basename, isAbsolute, join, relative, sep } from "node:path";
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

/**
 * 生成用于诊断的 Formatter 可执行文件描述，只暴露扩展目录内的相对路径。
 */
export function describeFormatterExecutable(extensionPath: string, launch: FormatterLaunchSpec | undefined): string {
    if (!launch) {
        return "unsupported";
    }

    const relativeCommand = relative(extensionPath, launch.command);
    if (relativeCommand && !relativeCommand.startsWith("..") && !isAbsolute(relativeCommand)) {
        return relativeCommand.split(sep).join("/");
    }

    return basename(launch.command);
}
