import * as vscode from "vscode";
import type { FormatterLaunchSpec } from "./formatterProtocol";

export function createDevelopmentFormatterLaunchSpec(context: vscode.ExtensionContext): FormatterLaunchSpec | undefined {
    if (context.extensionMode !== vscode.ExtensionMode.Development) {
        return undefined;
    }

    return {
        command: "dotnet",
        args: [
            vscode.Uri.joinPath(
                context.extensionUri,
                "formatting",
                "src",
                "CSharpWorkbench.Formatter",
                "bin",
                "Release",
                "net10.0",
                "CSharpWorkbench.Formatter.dll",
            ).fsPath,
        ],
    };
}
