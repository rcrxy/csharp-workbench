import * as vscode from "vscode";
import { defaultProfilePath, initializeDefaultEditorConfigProfile } from "../../core/editorConfig/defaultProfile";
import type { CSharpFormattingBackend } from "./csharpFormattingBackend";
import { FormatterClient } from "./client/formatterClient";
import { createFormatterLaunchSpec, resolveBundledFormatterTarget } from "./client/formatterRuntime";
import { CSharpDocumentFormattingProvider } from "./providers/csharpDocumentFormattingProvider";
import { RazorDocumentFormattingProvider } from "./providers/razorDocumentFormattingProvider";

let formatterClient: FormatterClient | undefined;

export async function registerFormattingFeature(
    context: vscode.ExtensionContext,
    csharpBackend?: CSharpFormattingBackend,
): Promise<void> {
    const defaultProfileUri = vscode.Uri.joinPath(context.extensionUri, ...defaultProfilePath);
    initializeDefaultEditorConfigProfile(await vscode.workspace.fs.readFile(defaultProfileUri));
    const log = vscode.window.createOutputChannel("C# Workbench", { log: true });
    const development = context.extensionMode === vscode.ExtensionMode.Development;
    const bundledTarget = resolveBundledFormatterTarget(process.platform, process.arch);
    const formatterLaunch = csharpBackend
        ? undefined
        : createFormatterLaunchSpec(context.extensionUri.fsPath, development, process.platform, process.arch);
    const client = formatterLaunch
        ? new FormatterClient({
              launch: formatterLaunch,
              log,
              handshakeTimeoutMs: 10_000,
              requestTimeoutMs: 60_000,
              shutdownTimeoutMs: 2_000,
          })
        : undefined;

    log.info(
        `Formatter runtime resolved: runtimeMode=${development ? "development" : "bundled"}, ` +
            `hostPlatform=${process.platform}, hostArch=${process.arch}, ` +
            `runtimeTarget=${development ? "repository" : (bundledTarget?.target ?? "unsupported")}.`,
    );

    if (!csharpBackend && !client) {
        log.error(
            `Formatting is unavailable because no bundled Formatter runtime supports ` +
                `${process.platform}/${process.arch}.`,
        );
        context.subscriptions.push(log);
        return;
    }

    formatterClient = client;

    const csharpRangeFormattingProvider = vscode.languages.registerDocumentRangeFormattingEditProvider(
        { language: "csharp" },
        new CSharpDocumentFormattingProvider(log, csharpBackend, client),
    );
    const razorRangeFormattingProvider = vscode.languages.registerDocumentRangeFormattingEditProvider(
        [{ language: "aspnetcorerazor" }, { language: "razor" }, { language: "cshtml" }],
        new RazorDocumentFormattingProvider(log, csharpBackend, client),
    );

    const subscriptions = [log, csharpRangeFormattingProvider, razorRangeFormattingProvider];

    if (client) {
        const csharpDocumentFormattingProvider = vscode.languages.registerDocumentFormattingEditProvider(
            { language: "csharp" },
            new CSharpDocumentFormattingProvider(log, undefined, client),
        );
        const razorDocumentFormattingProvider = vscode.languages.registerDocumentFormattingEditProvider(
            [{ language: "aspnetcorerazor" }, { language: "razor" }, { language: "cshtml" }],
            new RazorDocumentFormattingProvider(log, undefined, client),
        );

        subscriptions.push(csharpDocumentFormattingProvider, razorDocumentFormattingProvider);
    }

    log.info(
        `Formatting feature registered for ASP.NET Razor, Razor, C# document/selection formatting ` +
            `(C# backend=${csharpBackend?.kind ?? "none"}, formatterClient=${client ? "enabled" : "disabled"}).`,
    );
    context.subscriptions.push(...subscriptions);
}

export async function disposeFormattingFeature(): Promise<void> {
    const client = formatterClient;
    formatterClient = undefined;
    await client?.dispose();
}
