import * as vscode from "vscode";
import { resolveRawEditorConfig } from "../../../core/editorConfig";
import { FormatterClient } from "../client/formatterClient";
import { FormatterClientError, FormatterRequestError, type FormatterTextSpan } from "../client/formatterProtocol";

export class RazorDocumentFormattingProvider
    implements vscode.DocumentFormattingEditProvider, vscode.DocumentRangeFormattingEditProvider
{
    constructor(
        private readonly log: vscode.LogOutputChannel,
        private readonly formatterClient: FormatterClient,
    ) {}

    async provideDocumentFormattingEdits(
        document: vscode.TextDocument,
        options: vscode.FormattingOptions,
        token: vscode.CancellationToken,
    ): Promise<vscode.TextEdit[]> {
        const startedAt = performance.now();
        const version = document.version;

        try {
            if (token.isCancellationRequested) {
                this.log.info(`Razor document formatting cancelled: ${document.uri.toString()}.`);
                return [];
            }

            const source = document.getText();
            const controller = new AbortController();
            const cancellationSubscription = token.onCancellationRequested(() => controller.abort());

            try {
                const result = await this.formatterClient.formatDocument(
                    {
                        language: getRazorLanguage(document),
                        source,
                        resolvedEditorConfig: await resolveRawEditorConfig(document.uri),
                        editorFallback: createFormatterEditorFallback(document, options),
                    },
                    controller.signal,
                );

                if (token.isCancellationRequested || document.version !== version) {
                    this.log.info(`Razor document formatting cancelled or stale: ${document.uri.toString()}.`);
                    return [];
                }

                const edits = mapFormatterChanges(document, result.changes, source.length);
                this.log.info(
                    `Razor document formatting completed via tool: ${document.uri.toString()} ` +
                        `(changed=${edits.length > 0}, duration=${formatElapsedTime(startedAt)}, ` +
                        `inputChars=${source.length}, changeCount=${result.changes.length}).`,
                );
                return edits;
            } finally {
                cancellationSubscription.dispose();
            }
        } catch (error) {
            if (token.isCancellationRequested) {
                this.log.info(`Razor document formatting cancelled: ${document.uri.toString()}.`);
                return [];
            }

            if (error instanceof FormatterClientError || error instanceof FormatterRequestError) {
                this.log.warn(`Razor document formatter client rejected request: ${document.uri.toString()} (${error.code}).`);
                return [];
            }

            this.log.error(`Razor document formatting failed: ${document.uri.toString()}.`, error);
            throw error;
        }
    }

    async provideDocumentRangeFormattingEdits(
        document: vscode.TextDocument,
        range: vscode.Range,
        options: vscode.FormattingOptions,
        token: vscode.CancellationToken,
    ): Promise<vscode.TextEdit[]> {
        const startedAt = performance.now();
        const version = document.version;

        try {
            if (token.isCancellationRequested) {
                this.log.info(`Razor range formatting cancelled: ${document.uri.toString()}.`);
                return [];
            }

            const source = document.getText();
            const controller = new AbortController();
            const cancellationSubscription = token.onCancellationRequested(() => controller.abort());

            try {
                const result = await this.formatterClient.formatRange(
                    {
                        language: getRazorLanguage(document),
                        source,
                        span: toTextSpan(document, range),
                        resolvedEditorConfig: await resolveRawEditorConfig(document.uri),
                        editorFallback: createFormatterEditorFallback(document, options),
                    },
                    controller.signal,
                );

                if (token.isCancellationRequested || document.version !== version) {
                    this.log.info(`Razor range formatting cancelled or stale: ${document.uri.toString()}.`);
                    return [];
                }

                const edits = mapFormatterChanges(document, result.changes, source.length);
                this.log.info(
                    `Razor range formatting completed via tool: ${document.uri.toString()} ` +
                        `(changed=${edits.length > 0}, duration=${formatElapsedTime(startedAt)}, ` +
                        `range=${formatRange(range)}, inputChars=${source.length}, changeCount=${result.changes.length}).`,
                );
                return edits;
            } finally {
                cancellationSubscription.dispose();
            }
        } catch (error) {
            if (token.isCancellationRequested) {
                this.log.info(`Razor range formatting cancelled: ${document.uri.toString()}.`);
                return [];
            }

            if (error instanceof FormatterClientError || error instanceof FormatterRequestError) {
                this.log.warn(`Razor range formatter client rejected request: ${document.uri.toString()} (${error.code}).`);
                return [];
            }

            this.log.error(`Razor range formatting failed: ${document.uri.toString()}.`, error);
            throw error;
        }
    }
}

function toTextSpan(document: vscode.TextDocument, range: vscode.Range): FormatterTextSpan {
    const start = document.offsetAt(range.start);
    return { start, length: document.offsetAt(range.end) - start };
}

function formatRange(range: vscode.Range): string {
    return `${range.start.line}:${range.start.character}-${range.end.line}:${range.end.character}`;
}

function formatElapsedTime(startedAt: number): string {
    return `${(performance.now() - startedAt).toFixed(1)} ms`;
}

function createFormatterEditorFallback(
    document: vscode.TextDocument,
    options: vscode.FormattingOptions,
): {
    insertSpaces: boolean;
    tabSize: number;
    maxLineLength?: number;
    lineEnding: "\n" | "\r\n";
    insertFinalNewline: boolean;
    trimTrailingWhitespace: boolean;
} {
    return {
        insertSpaces: options.insertSpaces,
        tabSize: options.tabSize,
        maxLineLength: vscode.workspace.getConfiguration("editor", document.uri).get<number>("wordWrapColumn"),
        lineEnding: document.eol === vscode.EndOfLine.CRLF ? "\r\n" : "\n",
        insertFinalNewline: true,
        trimTrailingWhitespace: false,
    };
}

function getRazorLanguage(document: vscode.TextDocument): "razor" | "cshtml" {
    return document.uri.path.toLowerCase().endsWith(".cshtml") ? "cshtml" : "razor";
}

function mapFormatterChanges(
    document: vscode.TextDocument,
    changes: readonly { span: FormatterTextSpan; newText: string }[],
    sourceLength = document.getText().length,
): vscode.TextEdit[] {
    return changes.map(change => {
        const start = change.span.start;
        const length = change.span.length;
        const end = start + length;
        if (start < 0 || length < 0 || end > sourceLength) {
            throw new RangeError("Formatter returned a change outside the requested document span.");
        }

        return vscode.TextEdit.replace(new vscode.Range(document.positionAt(start), document.positionAt(end)), change.newText);
    });
}
