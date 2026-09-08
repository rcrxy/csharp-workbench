import * as vscode from "vscode";
import { resolveEditorConfig, resolveRawEditorConfig, type EditorConfigFallback } from "../../../core/editorConfig";
import { FormatterClient } from "../client/formatterClient";
import { FormatterClientError, FormatterRequestError } from "../client/formatterProtocol";
import {
    applyCSharpTextChanges,
    createCSharpFormattingOptions,
    type CSharpFormattingBackend,
} from "../csharpFormattingBackend";
import { formatRazorMarkup } from "../services/razorMarkupFormatter";

export class RazorDocumentFormattingProvider
    implements vscode.DocumentFormattingEditProvider, vscode.DocumentRangeFormattingEditProvider
{
    constructor(
        private readonly log: vscode.LogOutputChannel,
        private readonly csharpBackend?: CSharpFormattingBackend,
        private readonly formatterClient?: FormatterClient,
    ) {}

    async provideDocumentFormattingEdits(
        document: vscode.TextDocument,
        options: vscode.FormattingOptions,
        token: vscode.CancellationToken,
    ): Promise<vscode.TextEdit[]> {
        if (this.formatterClient) {
            return this.provideClientDocumentEdits(document, options, token);
        }

        return this.provideFullDocumentFormattingEdits(document, options, token, "document");
    }

    async provideDocumentRangeFormattingEdits(
        document: vscode.TextDocument,
        range: vscode.Range,
        options: vscode.FormattingOptions,
        token: vscode.CancellationToken,
    ): Promise<vscode.TextEdit[]> {
        if (this.formatterClient) {
            return this.provideClientRangeEdits(document, range, options, token);
        }

        return this.provideFullDocumentFormattingEdits(document, options, token, "range", formatRange(range));
    }

    async provideDocumentRangesFormattingEdits(
        document: vscode.TextDocument,
        ranges: vscode.Range[],
        options: vscode.FormattingOptions,
        token: vscode.CancellationToken,
    ): Promise<vscode.TextEdit[]> {
        return this.provideFullDocumentFormattingEdits(document, options, token, "ranges", `count=${ranges.length}`);
    }

    private async provideClientDocumentEdits(
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
                const result = await this.formatterClient!.formatDocument(
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

    private async provideClientRangeEdits(
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
            const start = document.offsetAt(range.start);
            const targetSpan = { start, length: document.offsetAt(range.end) - start };
            const controller = new AbortController();
            const cancellationSubscription = token.onCancellationRequested(() => controller.abort());

            try {
                const result = await this.formatterClient!.formatRange(
                    {
                        language: getRazorLanguage(document),
                        source,
                        span: targetSpan,
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

    private async provideFullDocumentFormattingEdits(
        document: vscode.TextDocument,
        options: vscode.FormattingOptions,
        token: vscode.CancellationToken,
        trigger: "document" | "range" | "ranges",
        requestedRanges?: string,
    ): Promise<vscode.TextEdit[]> {
        const startedAt = performance.now();

        try {
            const editorConfig = await resolveEditorConfig(document.uri, getIndentationFallback(document, options));
            if (token.isCancellationRequested) {
                this.log.info(`Razor formatting cancelled: ${document.uri.toString()} (trigger=${trigger}).`);
                return [];
            }

            const source = document.getText();
            const controller = new AbortController();
            const cancellationSubscription = token.onCancellationRequested(() => controller.abort());
            let formatted: string;

            try {
                formatted = await formatRazorMarkup(source, {
                    indentation: editorConfig.html.indentation,
                    csharpIndentation: editorConfig.csharpIndentation,
                    csharpNewLines: editorConfig.csharpNewLines,
                    html: editorConfig.html,
                    lineEnding: editorConfig.lineEnding,
                    insertFinalNewline: editorConfig.insertFinalNewline,
                    trimTrailingWhitespace: editorConfig.trimTrailingWhitespace,
                    charset: editorConfig.charset,
                    formatCSharp: this.csharpBackend
                        ? async csharpSource => {
                              const result = await this.csharpBackend!.format({
                                  source: csharpSource,
                                  kind: "snippet",
                                  snippetKind: "type-members",
                                  options: createCSharpFormattingOptions(editorConfig),
                                  signal: controller.signal,
                              });
                              return applyCSharpTextChanges(csharpSource, result.changes);
                          }
                        : undefined,
                });
            } finally {
                cancellationSubscription.dispose();
            }

            if (token.isCancellationRequested) {
                this.log.info(`Razor formatting cancelled: ${document.uri.toString()} (trigger=${trigger}).`);
                return [];
            }

            const changed = formatted !== source;

            this.log.info(
                `Razor formatting completed: ${document.uri.toString()} ` +
                    `(trigger=${trigger}, csharpBackend=${this.csharpBackend?.kind ?? "none"}, ` +
                    `changed=${changed}, duration=${formatElapsedTime(startedAt)}, ` +
                    `inputChars=${source.length}, outputChars=${formatted.length}` +
                    `${requestedRanges ? `, request=${requestedRanges}` : ""}).`,
            );

            if (!changed) {
                return [];
            }

            const documentRange = new vscode.Range(document.positionAt(0), document.positionAt(source.length));
            return [vscode.TextEdit.replace(documentRange, formatted)];
        } catch (error) {
            if (token.isCancellationRequested) {
                this.log.info(`Razor formatting cancelled: ${document.uri.toString()} (trigger=${trigger}).`);
                return [];
            }

            this.log.error(`Razor formatting failed: ${document.uri.toString()} (trigger=${trigger}).`, error);
            throw error;
        }
    }
}

function formatRange(range: vscode.Range): string {
    return `${range.start.line}:${range.start.character}-${range.end.line}:${range.end.character}`;
}

function formatElapsedTime(startedAt: number): string {
    return `${(performance.now() - startedAt).toFixed(1)} ms`;
}

function getIndentationFallback(document: vscode.TextDocument, options: vscode.FormattingOptions): EditorConfigFallback {
    const editor = vscode.window.visibleTextEditors.find(candidate => {
        return candidate.document.uri.toString() === document.uri.toString();
    });
    const editorTabSize = editor?.options.tabSize;
    const editorInsertSpaces = editor?.options.insertSpaces;

    return {
        insertSpaces: typeof editorInsertSpaces === "boolean" ? editorInsertSpaces : options.insertSpaces,
        tabSize: typeof editorTabSize === "number" ? editorTabSize : options.tabSize,
        maxLineLength: vscode.workspace.getConfiguration("editor", document.uri).get<number>("wordWrapColumn"),
        profileFileName: "document.razor",
        lineEnding: document.eol === vscode.EndOfLine.CRLF ? "\r\n" : "\n",
        insertFinalNewline: true,
        trimTrailingWhitespace: false,
    };
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
    changes: readonly { span: { start: number; length: number }; newText: string }[],
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
