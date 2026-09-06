export interface FormatterLaunchSpec {
    readonly command: string;
    readonly args?: readonly string[];
}

export interface FormatterClientLog {
    info(message: string): void;
    warn(message: string): void;
    error(message: string): void;
}

export interface FormatterEditorFallback {
    readonly insertSpaces?: boolean;
    readonly tabSize?: number;
    readonly maxLineLength?: number;
    readonly lineEnding?: "\n" | "\r\n";
    readonly insertFinalNewline?: boolean;
    readonly trimTrailingWhitespace?: boolean;
    readonly charset?: string;
}

export interface FormatterFormatDocumentRequest {
    readonly language: string;
    readonly source: string;
    readonly resolvedEditorConfig?: Readonly<Record<string, string>>;
    readonly editorFallback?: FormatterEditorFallback;
}

export interface FormatterTextSpan {
    readonly start: number;
    readonly length: number;
}

export interface FormatterTextChange {
    readonly span: FormatterTextSpan;
    readonly newText: string;
}

export interface FormatterFormatDocumentResult {
    readonly changes: readonly FormatterTextChange[];
}

export interface FormatterCapabilities {
    readonly formatDocument: boolean;
    readonly languages: readonly string[];
}

export interface FormatterInfo {
    readonly protocolVersion: number;
    readonly formatterVersion: string;
    readonly capabilities: FormatterCapabilities;
}

export interface WireRequest {
    readonly id: number;
    readonly method: string;
    readonly params: unknown;
}

export interface WireNotification {
    readonly method: string;
    readonly params: unknown;
}

export interface WireError {
    readonly code: string;
    readonly message: string;
}

export interface WireResponse {
    readonly id: number | null;
    readonly result?: unknown;
    readonly error?: WireError;
}

export class FormatterRequestError extends Error {
    public constructor(
        public readonly code: string,
        message: string,
    ) {
        super(message);
        this.name = "FormatterRequestError";
    }
}

export type FormatterClientErrorCode =
    | "processFailure"
    | "protocolFailure"
    | "handshakeFailure"
    | "timeout"
    | "cancelled"
    | "disabled"
    | "disposed";

export class FormatterClientError extends Error {
    public constructor(
        public readonly code: FormatterClientErrorCode,
        message: string,
    ) {
        super(message);
        this.name = "FormatterClientError";
    }
}

export type FormatterClientStatus = "stopped" | "starting" | "ready" | "disabled" | "disposed";

export interface FormatterClientOptions {
    readonly launch: FormatterLaunchSpec;
    readonly log?: FormatterClientLog;
    readonly handshakeTimeoutMs?: number;
    readonly requestTimeoutMs?: number;
    readonly shutdownTimeoutMs?: number;
}
