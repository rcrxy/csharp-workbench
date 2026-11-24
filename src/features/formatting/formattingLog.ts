import type * as vscode from "vscode";
import { FormattingCompletionLog, type FormattingCompletion } from "./formattingCompletionLog";

type FormattingOutputChannel = Pick<vscode.LogOutputChannel, "debug" | "info" | "warn" | "error" | "dispose"> & {
    show(preserveFocus: boolean): void;
};

export class FormattingLog {
    private active?: { channel: FormattingOutputChannel; completions: FormattingCompletionLog };

    public constructor(private readonly createChannel: () => FormattingOutputChannel) {}

    public enable(): boolean {
        if (this.active) {
            return false;
        }

        const channel = this.createChannel();
        this.active = { channel, completions: new FormattingCompletionLog(channel) };
        channel.show(true);
        return true;
    }

    public disable(): void {
        const active = this.active;
        this.active = undefined;
        active?.completions.dispose(false);
        active?.channel.dispose();
    }

    public info(message: string): void {
        this.active?.channel.info(message);
    }

    public warn(message: string): void {
        this.active?.channel.warn(message);
    }

    public error(message: string, ...args: unknown[]): void {
        this.active?.channel.error(message, ...args);
    }

    public record(completion: FormattingCompletion): void {
        this.active?.completions.record(completion);
    }

    public flushCompletions(): void {
        this.active?.completions.dispose();
    }

    public dispose(): void {
        this.flushCompletions();
        this.disable();
    }
}
