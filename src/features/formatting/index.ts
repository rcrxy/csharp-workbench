import * as vscode from "vscode";
import { FormatterClient } from "./client/formatterClient";
import {
    createFormatterLaunchSpec,
    describeFormatterExecutable,
    resolveBundledFormatterTarget,
} from "./client/formatterRuntime";
import { FormattingLog } from "./formattingLog";
import { CSharpDocumentFormattingProvider } from "./providers/csharpDocumentFormattingProvider";
import { RazorDocumentFormattingProvider } from "./providers/razorDocumentFormattingProvider";

let formatterClient: FormatterClient | undefined;
let formattingLog: FormattingLog | undefined;

const LOGGING_SETTING = "csharpworkbench.logging.enabled";

export async function registerFormattingFeature(context: vscode.ExtensionContext): Promise<void> {
    const log = new FormattingLog(() => vscode.window.createOutputChannel("C# Workbench", { log: true }));
    formattingLog = log;
    const development = context.extensionMode === vscode.ExtensionMode.Development;
    const bundledTarget = resolveBundledFormatterTarget(process.platform, process.arch);
    const formatterLaunch = createFormatterLaunchSpec(context.extensionUri.fsPath, development, process.platform, process.arch);

    const updateLogging = () => {
        if (!vscode.workspace.getConfiguration().get<boolean>(LOGGING_SETTING, false)) {
            log.disable();
            return;
        }

        if (!log.enable()) {
            return;
        }

        log.info(
            `Formatter runtime resolved: runtimeMode=${development ? "development" : "bundled"}, ` +
                `hostPlatform=${process.platform}, hostArch=${process.arch}, ` +
                `runtimeTarget=${development ? "repository" : (bundledTarget?.target ?? "unsupported")}, ` +
                `runtimeExecutable=${describeFormatterExecutable(context.extensionUri.fsPath, formatterLaunch)}.`,
        );
        if (!formatterLaunch) {
            log.error(
                `Formatting is unavailable because no bundled Formatter runtime supports ` +
                    `${process.platform}/${process.arch}.`,
            );
        }
    };
    context.subscriptions.push(
        vscode.workspace.onDidChangeConfiguration(event => {
            if (event.affectsConfiguration(LOGGING_SETTING)) {
                updateLogging();
            }
        }),
        log,
    );
    updateLogging();

    if (!formatterLaunch) {
        return;
    }

    const client = new FormatterClient({
        launch: formatterLaunch,
        log,
        handshakeTimeoutMs: 10_000,
        requestTimeoutMs: 60_000,
        shutdownTimeoutMs: 2_000,
    });
    formatterClient = client;
    const csharpProvider = new CSharpDocumentFormattingProvider(log, client);
    const razorProvider = new RazorDocumentFormattingProvider(log, client);
    const csharpRangeFormattingProvider = vscode.languages.registerDocumentRangeFormattingEditProvider(
        { language: "csharp" },
        csharpProvider,
    );
    const razorRangeFormattingProvider = vscode.languages.registerDocumentRangeFormattingEditProvider(
        [{ language: "aspnetcorerazor" }, { language: "razor" }, { language: "cshtml" }],
        razorProvider,
    );
    const csharpDocumentFormattingProvider = vscode.languages.registerDocumentFormattingEditProvider(
        { language: "csharp" },
        csharpProvider,
    );
    const razorDocumentFormattingProvider = vscode.languages.registerDocumentFormattingEditProvider(
        [{ language: "aspnetcorerazor" }, { language: "razor" }, { language: "cshtml" }],
        razorProvider,
    );

    log.info(
        `Formatting feature registered for ASP.NET Razor, Razor, C# document/selection formatting ` +
            `(formatterClient=enabled).`,
    );
    context.subscriptions.push(
        csharpRangeFormattingProvider,
        razorRangeFormattingProvider,
        csharpDocumentFormattingProvider,
        razorDocumentFormattingProvider,
    );
}

export async function disposeFormattingFeature(): Promise<void> {
    const client = formatterClient;
    const log = formattingLog;
    formatterClient = undefined;
    formattingLog = undefined;
    log?.flushCompletions();
    await client?.dispose();
}
