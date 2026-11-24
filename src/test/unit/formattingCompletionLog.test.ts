import assert from "node:assert/strict";
import { describe, it, mock } from "node:test";
import { FormattingCompletionLog, type FormattingCompletion } from "../../features/formatting/formattingCompletionLog";

function createFixture() {
    const info: string[] = [];
    const debug: string[] = [];
    const completionLog = new FormattingCompletionLog({
        info: message => info.push(message),
        debug: message => debug.push(message),
    });
    return { completionLog, info, debug };
}

function completion(overrides: Partial<FormattingCompletion> = {}): FormattingCompletion {
    return {
        language: "Razor",
        kind: "range",
        uri: "file:///project/One.razor",
        durationMs: 10,
        changeCount: 0,
        detail: "Razor range formatting completed via tool: file:///project/One.razor (changed=false).",
        ...overrides,
    };
}

describe("FormattingCompletionLog", () => {
    it("keeps the original info message for one request and writes debug immediately", () => {
        mock.timers.enable({ apis: ["setTimeout"] });
        try {
            const { completionLog, info, debug } = createFixture();
            const request = completion();
            completionLog.record(request);

            assert.deepEqual(debug, [request.detail]);
            assert.deepEqual(info, []);
            mock.timers.tick(149);
            assert.deepEqual(info, []);
            mock.timers.tick(1);
            assert.deepEqual(info, [request.detail]);
            completionLog.dispose();
        } finally {
            mock.timers.reset();
        }
    });

    it("combines multiple ranges of one document and counts changed requests and edits", () => {
        mock.timers.enable({ apis: ["setTimeout"] });
        try {
            const { completionLog, info, debug } = createFixture();
            completionLog.record(completion({ durationMs: 10.1, changeCount: 2, detail: "first" }));
            mock.timers.tick(75);
            completionLog.record(completion({ durationMs: 20.2, changeCount: 0, detail: "second" }));
            mock.timers.tick(149);
            assert.deepEqual(info, []);
            mock.timers.tick(1);

            assert.deepEqual(debug, ["first", "second"]);
            assert.deepEqual(info, [
                "Razor range formatting batch completed via tool " +
                    "(requests=2, documents=1, changedRequests=1, changeCount=2, totalRequestDuration=30.3 ms).",
            ]);
            completionLog.dispose();
        } finally {
            mock.timers.reset();
        }
    });

    it("counts distinct document URIs across files", () => {
        mock.timers.enable({ apis: ["setTimeout"] });
        try {
            const { completionLog, info } = createFixture();
            completionLog.record(completion({ uri: "file:///project/One.razor" }));
            completionLog.record(completion({ uri: "file:///project/Two.razor", changeCount: 1 }));
            completionLog.record(completion({ uri: "file:///project/One.razor", changeCount: 0 }));
            mock.timers.tick(150);

            assert.deepEqual(info, [
                "Razor range formatting batch completed via tool " +
                    "(requests=3, documents=2, changedRequests=1, changeCount=1, totalRequestDuration=30.0 ms).",
            ]);
            completionLog.dispose();
        } finally {
            mock.timers.reset();
        }
    });

    it("keeps C#, Razor, document, and range groups separate", () => {
        mock.timers.enable({ apis: ["setTimeout"] });
        try {
            const { completionLog, info } = createFixture();
            completionLog.record(completion({ language: "C#", kind: "document", detail: "csharp document" }));
            completionLog.record(completion({ language: "C#", kind: "document", detail: "csharp document 2" }));
            completionLog.record(completion({ language: "C#", kind: "range", detail: "csharp range" }));
            completionLog.record(completion({ language: "Razor", kind: "range", detail: "razor range" }));
            completionLog.record(completion({ language: "Razor", kind: "range", detail: "razor range 2" }));
            mock.timers.tick(150);

            assert.deepEqual(info, [
                "C# document formatting batch completed via tool " +
                    "(requests=2, documents=1, changedRequests=0, changeCount=0, totalRequestDuration=20.0 ms).",
                "csharp range",
                "Razor range formatting batch completed via tool " +
                    "(requests=2, documents=1, changedRequests=0, changeCount=0, totalRequestDuration=20.0 ms).",
            ]);
            completionLog.dispose();
        } finally {
            mock.timers.reset();
        }
    });

    it("flushes pending batches on dispose without a later duplicate", () => {
        mock.timers.enable({ apis: ["setTimeout"] });
        try {
            const { completionLog, info } = createFixture();
            completionLog.record(completion({ detail: "first" }));
            completionLog.record(completion({ detail: "second" }));
            completionLog.dispose();
            completionLog.dispose();
            mock.timers.tick(150);

            assert.equal(info.length, 1);
            assert.match(info[0], /requests=2, documents=1/u);
        } finally {
            mock.timers.reset();
        }
    });

    it("does not combine requests separated by a full quiet window", () => {
        mock.timers.enable({ apis: ["setTimeout"] });
        try {
            const { completionLog, info } = createFixture();
            completionLog.record(completion({ detail: "first" }));
            mock.timers.tick(150);
            completionLog.record(completion({ detail: "second" }));
            mock.timers.tick(150);

            assert.deepEqual(info, ["first", "second"]);
            completionLog.dispose();
        } finally {
            mock.timers.reset();
        }
    });
});
