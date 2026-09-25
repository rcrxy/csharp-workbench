import type * as vscode from "vscode";

export interface FormattingCompletion {
    readonly language: "C#" | "Razor";
    readonly kind: "document" | "range";
    readonly uri: string;
    readonly durationMs: number;
    readonly changeCount: number;
    readonly detail: string;
}

interface PendingBatch {
    readonly completions: FormattingCompletion[];
    timer: ReturnType<typeof setTimeout>;
}

// VS Code provides no formatting batch id, so a short gap between completions marks the boundary.
const QUIET_WINDOW_MS = 150;

export class FormattingCompletionLog {
    private readonly pending = new Map<string, PendingBatch>();
    private disposed = false;

    public constructor(private readonly log: Pick<vscode.LogOutputChannel, "debug" | "info">) {}

    public record(completion: FormattingCompletion): void {
        this.log.debug(completion.detail);

        if (this.disposed) {
            this.log.info(completion.detail);
            return;
        }

        const key = `${completion.language}:${completion.kind}`;
        const batch = this.pending.get(key);
        if (batch) {
            clearTimeout(batch.timer);
            batch.completions.push(completion);
            batch.timer = setTimeout(() => this.flush(key), QUIET_WINDOW_MS);
            return;
        }

        this.pending.set(key, {
            completions: [completion],
            timer: setTimeout(() => this.flush(key), QUIET_WINDOW_MS),
        });
    }

    public dispose(): void {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        for (const key of this.pending.keys()) {
            this.flush(key);
        }
    }

    private flush(key: string): void {
        const batch = this.pending.get(key);
        if (!batch) {
            return;
        }

        clearTimeout(batch.timer);
        this.pending.delete(key);
        const { completions } = batch;
        if (completions.length === 1) {
            this.log.info(completions[0].detail);
            return;
        }

        const { language, kind } = completions[0];
        const documents = new Set(completions.map(completion => completion.uri)).size;
        const changedRequests = completions.filter(completion => completion.changeCount > 0).length;
        const changeCount = completions.reduce((total, completion) => total + completion.changeCount, 0);
        const totalRequestDurationMs = completions.reduce((total, completion) => total + completion.durationMs, 0);
        this.log.info(
            `${language} ${kind} formatting batch completed via tool ` +
                `(requests=${completions.length}, documents=${documents}, changedRequests=${changedRequests}, ` +
                `changeCount=${changeCount}, totalRequestDuration=${totalRequestDurationMs.toFixed(1)} ms).`,
        );
    }
}
