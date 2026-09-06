import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import {
    FormatterClientError,
    FormatterRequestError,
    type FormatterClientOptions,
    type FormatterClientStatus,
    type FormatterFormatDocumentRequest,
    type FormatterFormatDocumentResult,
    type FormatterInfo,
    type WireResponse,
} from "./formatterProtocol";
import { encodeWireFrame, FormatterWireDecoder } from "./formatterWire";

interface PendingRequest<TResult> {
    readonly resolve: (result: TResult) => void;
    readonly reject: (error: Error) => void;
    readonly timer?: NodeJS.Timeout;
    abortCleanup?: () => void;
}

const PROTOCOL_VERSION = 1;
const DEFAULT_HANDSHAKE_TIMEOUT_MS = 10_000;
const DEFAULT_REQUEST_TIMEOUT_MS = 60_000;
const DEFAULT_SHUTDOWN_TIMEOUT_MS = 2_000;

export class FormatterClient {
    private readonly options: FormatterClientOptions;
    private readonly pending = new Map<number, PendingRequest<unknown>>();
    private process?: ChildProcessWithoutNullStreams;
    private startPromise?: Promise<void>;
    private disposePromise?: Promise<void>;
    private infoValue?: FormatterInfo;
    private currentStatus: FormatterClientStatus = "stopped";
    private nextRequestId = 1;
    private processFailureCount = 0;
    private terminatingProcess?: ChildProcessWithoutNullStreams;

    public constructor(options: FormatterClientOptions) {
        this.options = options;
    }

    public get status(): FormatterClientStatus {
        return this.currentStatus;
    }

    public get info(): FormatterInfo | undefined {
        return this.infoValue;
    }

    public async formatDocument(
        request: FormatterFormatDocumentRequest,
        signal?: AbortSignal,
    ): Promise<FormatterFormatDocumentResult> {
        if (signal?.aborted) {
            throw this.cancelledError();
        }

        await this.ensureReady();
        if (signal?.aborted) {
            throw this.cancelledError();
        }

        return this.sendRequest<FormatterFormatDocumentResult>(
            "formatDocument",
            request,
            signal,
            this.options.requestTimeoutMs ?? DEFAULT_REQUEST_TIMEOUT_MS,
        );
    }

    public dispose(): Promise<void> {
        if (this.disposePromise) {
            return this.disposePromise;
        }

        this.currentStatus = "disposed";
        this.disposePromise = this.disposeCore();
        return this.disposePromise;
    }

    private async ensureReady(): Promise<void> {
        if (this.currentStatus === "disposed") {
            throw new FormatterClientError("disposed", "FormatterClient has been disposed.");
        }

        if (this.currentStatus === "disabled") {
            throw new FormatterClientError("disabled", "FormatterClient is disabled after repeated startup failures.");
        }

        if (this.currentStatus === "ready") {
            return;
        }

        if (this.currentStatus === "starting" && this.startPromise) {
            await this.startPromise;
            return;
        }

        this.currentStatus = "starting";
        const startPromise = this.startProcess();
        this.startPromise = startPromise;
        try {
            await startPromise;
        } finally {
            if (this.startPromise === startPromise) {
                this.startPromise = undefined;
            }
        }
    }

    private async startProcess(): Promise<void> {
        let child: ChildProcessWithoutNullStreams;
        try {
            child = spawn(this.options.launch.command, [...(this.options.launch.args ?? []), "server"], {
                stdio: ["pipe", "pipe", "pipe"],
                shell: false,
                windowsHide: true,
            });
        } catch (error) {
            throw this.handleStartupFailure(toError(error));
        }

        this.process = child;
        this.attachProcessHandlers(child);

        try {
            const handshake = await this.sendRequest<unknown>(
                "handshake",
                { protocolVersion: PROTOCOL_VERSION },
                undefined,
                this.options.handshakeTimeoutMs ?? DEFAULT_HANDSHAKE_TIMEOUT_MS,
            );
            if (this.currentStatus === "disposed") {
                this.terminateProcess(child);
                throw new FormatterClientError("disposed", "FormatterClient has been disposed.");
            }

            this.infoValue = parseFormatterInfo(handshake);
            this.processFailureCount = 0;
            this.currentStatus = "ready";
        } catch (error) {
            const startupError =
                error instanceof FormatterClientError && error.code === "processFailure"
                    ? error
                    : new FormatterClientError("handshakeFailure", toError(error).message);
            if (this.process === child) {
                this.handleStartupFailure(startupError);
            }
            this.terminateProcess(child);
            throw startupError;
        }
    }

    private attachProcessHandlers(child: ChildProcessWithoutNullStreams): void {
        const decoder = new FormatterWireDecoder();
        child.stdout.on("data", (chunk: Buffer | string) => {
            if (this.process !== child) {
                return;
            }

            try {
                const frames = decoder.feed(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
                for (const frame of frames) {
                    this.handleResponse(JSON.parse(frame.toString("utf8")) as unknown);
                }
            } catch (error) {
                this.handleProcessFailure(child, new FormatterClientError("protocolFailure", toError(error).message));
                this.terminateProcess(child);
            }
        });

        child.stderr.on("data", (chunk: Buffer | string) => {
            const text = (Buffer.isBuffer(chunk) ? chunk.toString("utf8") : chunk).trimEnd();
            if (text) {
                this.options.log?.error(`[Formatter] ${text}`);
            }
        });

        child.once("error", error => {
            this.handleProcessFailure(child, new FormatterClientError("processFailure", error.message));
        });

        child.once("exit", (code, signal) => {
            if (this.process !== child) {
                return;
            }

            if (this.terminatingProcess === child) {
                this.process = undefined;
                this.infoValue = undefined;
                this.terminatingProcess = undefined;
                return;
            }

            const reason = `Formatter process exited (${signal ?? `code ${code ?? "unknown"}`}).`;
            this.handleProcessFailure(child, new FormatterClientError("processFailure", reason));
        });
    }

    private handleResponse(value: unknown): void {
        const response = parseWireResponse(value);
        if (response.id === null) {
            this.options.log?.warn("Ignored Formatter response without a request id.");
            return;
        }

        const pending = this.pending.get(response.id);
        if (!pending) {
            this.options.log?.warn(`Ignored Formatter response for unknown request id ${response.id}.`);
            return;
        }

        this.pending.delete(response.id);
        clearTimeout(pending.timer);
        pending.abortCleanup?.();
        if (response.error) {
            pending.reject(new FormatterRequestError(response.error.code, response.error.message));
        } else {
            pending.resolve(response.result);
        }
    }

    private sendRequest<TResult>(
        method: string,
        params: unknown,
        signal: AbortSignal | undefined,
        timeoutMs: number,
    ): Promise<TResult> {
        if (signal?.aborted) {
            return Promise.reject(this.cancelledError());
        }

        const child = this.process;
        if (!child || !child.stdin.writable) {
            throw new FormatterClientError("processFailure", "Formatter process is not available.");
        }

        const id = this.nextRequestId++;
        return new Promise<TResult>((resolve, reject) => {
            const pending: PendingRequest<unknown> = {
                resolve: resolve as (result: unknown) => void,
                reject,
                timer: setTimeout(() => {
                    if (!this.pending.delete(id)) {
                        return;
                    }

                    this.sendNotification("cancel", { id }).catch((error: unknown) => {
                        this.options.log?.warn(`Failed to send Formatter cancellation: ${toError(error).message}`);
                    });
                    reject(new FormatterClientError("timeout", `Formatter request ${id} timed out.`));
                }, timeoutMs),
            };

            if (signal) {
                const abort = () => {
                    if (!this.pending.delete(id)) {
                        return;
                    }

                    this.sendNotification("cancel", { id }).catch((error: unknown) => {
                        this.options.log?.warn(`Failed to send Formatter cancellation: ${toError(error).message}`);
                    });
                    clearTimeout(pending.timer);
                    reject(this.cancelledError());
                };
                signal.addEventListener("abort", abort, { once: true });
                pending.abortCleanup = () => signal.removeEventListener("abort", abort);
            }

            this.pending.set(id, pending);
            this.writeFrame(child, {
                id,
                method,
                params,
            }).catch((error: unknown) => {
                if (!this.pending.delete(id)) {
                    return;
                }

                clearTimeout(pending.timer);
                pending.abortCleanup?.();
                reject(toError(error));
            });
        });
    }

    private sendNotification(method: string, params: unknown): Promise<void> {
        const child = this.process;
        if (!child || !child.stdin.writable) {
            return Promise.reject(new FormatterClientError("processFailure", "Formatter process is not available."));
        }

        return this.writeFrame(child, { method, params });
    }

    private writeFrame(child: ChildProcessWithoutNullStreams, message: unknown): Promise<void> {
        const frame = encodeWireFrame(message);
        return new Promise<void>((resolve, reject) => {
            child.stdin.write(frame, (error?: Error | null) => {
                if (error) {
                    reject(new FormatterClientError("processFailure", error.message));
                } else {
                    resolve();
                }
            });
        });
    }

    private handleProcessFailure(child: ChildProcessWithoutNullStreams, error: FormatterClientError): void {
        if (this.process !== child) {
            return;
        }

        this.process = undefined;
        this.infoValue = undefined;
        if (this.currentStatus === "disposed") {
            this.rejectPending(error);
            return;
        }

        this.currentStatus = "stopped";
        this.rejectPending(error);
        this.processFailureCount++;
        if (this.processFailureCount >= 2) {
            this.currentStatus = "disabled";
        }
    }

    private handleStartupFailure(error: Error): FormatterClientError {
        this.process = undefined;
        this.infoValue = undefined;
        if (this.currentStatus === "disposed") {
            this.rejectPending(error);
            return error instanceof FormatterClientError ? error : new FormatterClientError("handshakeFailure", error.message);
        }

        this.currentStatus = "stopped";
        this.rejectPending(error);
        this.processFailureCount++;
        if (this.processFailureCount >= 2) {
            this.currentStatus = "disabled";
        }

        return error instanceof FormatterClientError ? error : new FormatterClientError("handshakeFailure", error.message);
    }

    private async disposeCore(): Promise<void> {
        const child = this.process;
        if (!child) {
            this.rejectPending(new FormatterClientError("disposed", "FormatterClient has been disposed."));
            return;
        }

        const wasReady = this.infoValue !== undefined && this.currentStatus === "disposed";
        if (wasReady) {
            try {
                await this.sendRequest(
                    "shutdown",
                    {},
                    undefined,
                    this.options.shutdownTimeoutMs ?? DEFAULT_SHUTDOWN_TIMEOUT_MS,
                );
            } catch (error) {
                this.options.log?.warn(`Formatter shutdown failed: ${toError(error).message}`);
            }
        }

        this.terminatingProcess = child;
        this.terminateProcess(child);
        if (!child.killed) {
            await waitForExit(child, this.options.shutdownTimeoutMs ?? DEFAULT_SHUTDOWN_TIMEOUT_MS);
        }
        this.process = undefined;
        this.infoValue = undefined;
        this.rejectPending(new FormatterClientError("disposed", "FormatterClient has been disposed."));
    }

    private terminateProcess(child: ChildProcessWithoutNullStreams): void {
        if (!child.killed) {
            child.kill();
        }
    }

    private rejectPending(error: Error): void {
        for (const [id, pending] of this.pending) {
            this.pending.delete(id);
            clearTimeout(pending.timer);
            pending.abortCleanup?.();
            pending.reject(error);
        }
    }

    private cancelledError(): FormatterClientError {
        return new FormatterClientError("cancelled", "Formatter request was cancelled.");
    }
}

function parseFormatterInfo(value: unknown): FormatterInfo {
    if (
        !isRecord(value) ||
        value.protocolVersion !== PROTOCOL_VERSION ||
        typeof value.formatterVersion !== "string" ||
        !isRecord(value.capabilities) ||
        typeof value.capabilities.formatDocument !== "boolean" ||
        !Array.isArray(value.capabilities.languages) ||
        !value.capabilities.languages.every((language): language is string => typeof language === "string")
    ) {
        throw new FormatterClientError("handshakeFailure", "Formatter handshake response is invalid.");
    }

    return {
        protocolVersion: value.protocolVersion,
        formatterVersion: value.formatterVersion,
        capabilities: {
            formatDocument: value.capabilities.formatDocument,
            languages: [...value.capabilities.languages],
        },
    };
}

function parseWireResponse(value: unknown): WireResponse {
    if (
        !isRecord(value) ||
        !(value.id === null || (typeof value.id === "number" && Number.isSafeInteger(value.id))) ||
        (value.result === undefined && !isRecord(value.error)) ||
        (value.error !== undefined &&
            (!isRecord(value.error) || typeof value.error.code !== "string" || typeof value.error.message !== "string"))
    ) {
        throw new FormatterClientError("protocolFailure", "Formatter response has an invalid shape.");
    }

    return value as unknown as WireResponse;
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === "object" && value !== null && !Array.isArray(value);
}

function toError(error: unknown): Error {
    return error instanceof Error ? error : new Error(String(error));
}

function waitForExit(child: ChildProcessWithoutNullStreams, timeoutMs: number): Promise<void> {
    if (child.exitCode !== null) {
        return Promise.resolve();
    }

    return new Promise<void>(resolve => {
        const timer = setTimeout(resolve, timeoutMs);
        child.once("exit", () => {
            clearTimeout(timer);
            resolve();
        });
    });
}
