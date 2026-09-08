import * as vscode from "vscode";
import { defaultProfilePath, initializeDefaultEditorConfigProfile } from "../../core/editorConfig/defaultProfile";
import { LightweightCSharpFormattingBackend } from "./backends/lightweightCSharpFormattingBackend";
import type { CSharpFormattingBackend } from "./csharpFormattingBackend";
import { FormatterClient } from "./client/formatterClient";
import { createDevelopmentFormatterLaunchSpec } from "./client/formatterRuntime";
import { CSharpDocumentFormattingProvider } from "./providers/csharpDocumentFormattingProvider";
import { RazorDocumentFormattingProvider } from "./providers/razorDocumentFormattingProvider";

export async function registerFormattingFeature(
    context: vscode.ExtensionContext,
    csharpBackend?: CSharpFormattingBackend,
): Promise<void> {
    const defaultProfileUri = vscode.Uri.joinPath(context.extensionUri, ...defaultProfilePath);
    initializeDefaultEditorConfigProfile(await vscode.workspace.fs.readFile(defaultProfileUri));
    const log = vscode.window.createOutputChannel("C# Workbench", { log: true });
    const backend = csharpBackend ?? new LightweightCSharpFormattingBackend();
    const formatterLaunch = createDevelopmentFormatterLaunchSpec(context);
    const formatterClient = formatterLaunch
        ? new FormatterClient({
              launch: formatterLaunch,
              log,
              handshakeTimeoutMs: 10_000,
              requestTimeoutMs: 60_000,
              shutdownTimeoutMs: 2_000,
          })
        : undefined;

    const csharpRangeFormattingProvider = vscode.languages.registerDocumentRangeFormattingEditProvider(
        { language: "csharp" },
        new CSharpDocumentFormattingProvider(log, backend, formatterClient),
    );
    const razorRangeFormattingProvider = vscode.languages.registerDocumentRangeFormattingEditProvider(
        [{ language: "aspnetcorerazor" }, { language: "razor" }, { language: "cshtml" }],
        new RazorDocumentFormattingProvider(log, formatterClient ? undefined : backend, formatterClient),
    );

    const subscriptions = [log, csharpRangeFormattingProvider, razorRangeFormattingProvider];

    if (formatterClient) {
        const csharpDocumentFormattingProvider = vscode.languages.registerDocumentFormattingEditProvider(
            { language: "csharp" },
            new CSharpDocumentFormattingProvider(log, undefined, formatterClient),
        );
        const razorDocumentFormattingProvider = vscode.languages.registerDocumentFormattingEditProvider(
            [{ language: "aspnetcorerazor" }, { language: "razor" }, { language: "cshtml" }],
            new RazorDocumentFormattingProvider(log, undefined, formatterClient),
        );

        subscriptions.push(csharpDocumentFormattingProvider, razorDocumentFormattingProvider);
    }

    log.info(
        `Formatting feature registered for ASP.NET Razor, Razor, C# document/selection formatting ` +
            `(C# backend=${backend.kind}, formatterClient=${formatterClient ? "enabled" : "disabled"}).`,
    );
    context.subscriptions.push(...subscriptions);
}
