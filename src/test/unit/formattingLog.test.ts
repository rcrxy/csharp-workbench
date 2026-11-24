import assert from "node:assert/strict";
import { it } from "node:test";
import { FormattingLog } from "../../features/formatting/formattingLog";

const completion = {
    language: "C#",
    kind: "document",
    uri: "file:///project/Demo.cs",
    durationMs: 10,
    changeCount: 1,
    detail: "C# document formatting completed via tool: file:///project/Demo.cs.",
} as const;

function createLog() {
    const events: string[] = [];
    const log = new FormattingLog(() => {
        events.push("created");
        return {
            show: (preserveFocus?: boolean) => events.push(`shown: ${preserveFocus}`),
            dispose: () => events.push("disposed"),
            info: message => events.push(`info: ${message}`),
            debug: message => events.push(`debug: ${message}`),
            warn: message => events.push(`warn: ${message}`),
            error: message => events.push(`error: ${message}`),
        };
    });
    return { log, events };
}

it("creates and reveals a log channel only while logging is enabled", () => {
    const { log, events } = createLog();
    log.info("startup");
    log.warn("warning");
    log.error("error");
    log.record(completion);
    assert.deepEqual(events, []);

    assert.equal(log.enable(), true);
    assert.equal(log.enable(), false);
    log.info("startup");
    log.warn("warning");
    log.error("error");
    log.disable();
    log.error("after disabling");

    assert.deepEqual(events, ["created", "shown: true", "info: startup", "warn: warning", "error: error", "disposed"]);
});

it("drops pending completions on disable and flushes the active session on extension shutdown", context => {
    context.mock.timers.enable({ apis: ["setTimeout"] });
    const { log, events } = createLog();
    log.enable();
    log.record({ ...completion, detail: "first session" });
    log.disable();
    log.enable();
    log.record({ ...completion, detail: "second session" });
    log.dispose();
    context.mock.timers.tick(150);

    assert.deepEqual(events, [
        "created", "shown: true", "debug: first session", "disposed",
        "created", "shown: true", "debug: second session", "info: second session", "disposed",
    ]);
});
