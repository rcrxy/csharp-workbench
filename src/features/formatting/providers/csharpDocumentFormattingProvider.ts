import * as vscode from "vscode";
import { resolveEditorConfig, resolveRawEditorConfig, type EditorConfigFallback } from "../../../core/editorConfig";
import { FormatterClient } from "../client/formatterClient";
import { FormatterClientError } from "../client/formatterProtocol";
import {
    createCSharpFormattingOptions,
    type CSharpDocumentFormattingRequest,
    type CSharpFormattingBackend,
    type CSharpFormattingKind,
    type CSharpFormattingRequest,
    type CSharpFormattingResult,
    type CSharpRangeFormattingRequest,
    type CSharpTextSpan,
} from "../csharpFormattingBackend";

export class CSharpDocumentFormattingProvider
    implements vscode.DocumentFormattingEditProvider, vscode.DocumentRangeFormattingEditProvider
{
    constructor(
        private readonly log: vscode.LogOutputChannel,
        private readonly backend?: CSharpFormattingBackend,
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

        return this.provideEdits(document, fullDocumentRange(document), options, token, "document");
    }

    async provideDocumentRangeFormattingEdits(
        document: vscode.TextDocument,
        range: vscode.Range,
        options: vscode.FormattingOptions,
        token: vscode.CancellationToken,
    ): Promise<vscode.TextEdit[]> {
        return this.provideEdits(document, expandToFullLines(document, range), options, token, "range");
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
                this.log.info(`C# document formatting cancelled: ${document.uri.toString()}.`);
                return [];
            }

            const source = document.getText();
            const controller = new AbortController();
            const cancellationSubscription = token.onCancellationRequested(() => controller.abort());

            try {
                const result = await this.formatterClient!.formatDocument(
                    {
                        language: "csharp",
                        source,
                        resolvedEditorConfig: await resolveRawEditorConfig(document.uri),
                        editorFallback: createFormatterEditorFallback(document, options, "csharp"),
                    },
                    controller.signal,
                );

                if (token.isCancellationRequested || document.version !== version) {
                    this.log.info(`C# document formatting cancelled or stale: ${document.uri.toString()}.`);
                    return [];
                }

                const edits = mapFormatterChanges(document, source.length, result.changes);
                this.log.info(
                    `C# document formatting completed via tool: ${document.uri.toString()} ` +
                        `(changed=${edits.length > 0}, duration=${formatElapsedTime(startedAt)}, ` +
                        `inputChars=${source.length}, changeCount=${result.changes.length}).`,
                );
                return edits;
            } finally {
                cancellationSubscription.dispose();
            }
        } catch (error) {
            if (token.isCancellationRequested) {
                this.log.info(`C# document formatting cancelled: ${document.uri.toString()}.`);
                return [];
            }

            if (error instanceof FormatterClientError) {
                this.log.warn(`C# document formatter client rejected request: ${document.uri.toString()} (${error.code}).`);
                return [];
            }

            this.log.error(`C# document formatting failed: ${document.uri.toString()}.`, error);
            throw error;
        }
    }

    private async provideEdits(
        document: vscode.TextDocument,
        targetRange: vscode.Range,
        options: vscode.FormattingOptions,
        token: vscode.CancellationToken,
        kind: Exclude<CSharpFormattingKind, "snippet">,
    ): Promise<vscode.TextEdit[]> {
        if (!this.backend) {
            return [];
        }

        const startedAt = performance.now();
        try {
            const editorConfig = await resolveEditorConfig(document.uri, getEditorConfigFallback(document, options));
            if (token.isCancellationRequested) {
                this.log.info(`C# ${kind} formatting cancelled: ${document.uri.toString()}.`);
                return [];
            }

            const source = document.getText();
            const originalSelection = document.getText(targetRange);
            const formattingOptions = createCSharpFormattingOptions(editorConfig);
            const request: CSharpDocumentFormattingRequest | CSharpRangeFormattingRequest =
                kind === "document"
                    ? { source, kind, options: formattingOptions }
                    : { source, kind, span: toTextSpan(document, targetRange), options: formattingOptions };
            const result = await this.formatWithCancellation(token, request);
            if (token.isCancellationRequested) {
                this.log.info(`C# ${kind} formatting cancelled: ${document.uri.toString()}.`);
                return [];
            }

            const targetSpan = toTextSpan(document, targetRange);
            const edits = result.changes.map(change => {
                validateChangeSpan(source.length, targetSpan, change.span);
                return vscode.TextEdit.replace(toRange(document, change.span), change.newText);
            });

            this.log.info(
                `C# ${kind} formatting completed: ${document.uri.toString()} ` +
                    `(backend=${this.backend.kind}, changed=${edits.length > 0}, duration=${formatElapsedTime(startedAt)}, ` +
                    `range=${formatRange(targetRange)}, inputChars=${originalSelection.length}, ` +
                    `outputChars=${calculateOutputLength(originalSelection.length, result)}).`,
            );

            return edits;
        } catch (error) {
            if (token.isCancellationRequested) {
                this.log.info(`C# ${kind} formatting cancelled: ${document.uri.toString()}.`);
                return [];
            }

            this.log.error(`C# ${kind} formatting failed: ${document.uri.toString()}.`, error);
            throw error;
        }
    }

    private async formatWithCancellation(
        token: vscode.CancellationToken,
        request: CSharpFormattingRequest,
    ): Promise<CSharpFormattingResult> {
        const controller = new AbortController();
        const cancellationSubscription = token.onCancellationRequested(() => controller.abort());

        try {
            return await this.backend!.format({ ...request, signal: controller.signal });
        } finally {
            cancellationSubscription.dispose();
        }
    }
}

function toTextSpan(document: vscode.TextDocument, range: vscode.Range): CSharpTextSpan {
    const start = document.offsetAt(range.start);
    return { start, length: document.offsetAt(range.end) - start };
}

function toRange(document: vscode.TextDocument, span: CSharpTextSpan): vscode.Range {
    return new vscode.Range(document.positionAt(span.start), document.positionAt(span.start + span.length));
}

function validateChangeSpan(sourceLength: number, targetSpan: CSharpTextSpan, changeSpan: CSharpTextSpan): void {
    const targetEnd = targetSpan.start + targetSpan.length;
    const changeEnd = changeSpan.start + changeSpan.length;
    if (changeSpan.start < targetSpan.start || changeSpan.length < 0 || changeEnd > targetEnd || changeEnd > sourceLength) {
        throw new RangeError("C# formatting backend returned a text change outside the requested span.");
    }
}

function calculateOutputLength(inputLength: number, result: CSharpFormattingResult): number {
    return result.changes.reduce((length, change) => length - change.span.length + change.newText.length, inputLength);
}

function formatRange(range: vscode.Range): string {
    return `${range.start.line}:${range.start.character}-${range.end.line}:${range.end.character}`;
}

function formatElapsedTime(startedAt: number): string {
    return `${(performance.now() - startedAt).toFixed(1)} ms`;
}

function fullDocumentRange(document: vscode.TextDocument): vscode.Range {
    return new vscode.Range(document.positionAt(0), document.positionAt(document.getText().length));
}

function expandToFullLines(document: vscode.TextDocument, range: vscode.Range): vscode.Range {
    const lastLine = range.end.character === 0 && range.end.line > range.start.line ? range.end.line - 1 : range.end.line;
    return new vscode.Range(range.start.line, 0, lastLine, document.lineAt(lastLine).text.length);
}

function getEditorConfigFallback(document: vscode.TextDocument, options: vscode.FormattingOptions): EditorConfigFallback {
    return {
        insertSpaces: options.insertSpaces,
        tabSize: options.tabSize,
        maxLineLength: vscode.workspace.getConfiguration("editor", document.uri).get<number>("wordWrapColumn"),
        profileFileName: "document.cs",
        lineEnding: document.eol === vscode.EndOfLine.CRLF ? "\r\n" : "\n",
    };
}

function createFormatterEditorFallback(
    document: vscode.TextDocument,
    options: vscode.FormattingOptions,
    language: string,
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
        insertFinalNewline: language === "csharp" ? false : true,
        trimTrailingWhitespace: false,
    };
}

function mapFormatterChanges(
    document: vscode.TextDocument,
    sourceLength: number,
    changes: readonly { span: { start: number; length: number }; newText: string }[],
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
