import process from "node:process";

const mode = process.argv[2] ?? "normal";
let input = Buffer.alloc(0);

process.stdin.on("data", (chunk: Buffer | string) => {
    input = Buffer.concat([input, Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk)]);
    readFrames();
});

function readFrames(): void {
    while (true) {
        const separator = input.indexOf(Buffer.from("\r\n\r\n", "ascii"));
        if (separator < 0) {
            return;
        }

        const header = input.subarray(0, separator).toString("ascii");
        const contentLength = Number(header.slice("Content-Length:".length).trim());
        const bodyStart = separator + 4;
        if (input.length - bodyStart < contentLength) {
            return;
        }

        const body = input.subarray(bodyStart, bodyStart + contentLength);
        input = input.subarray(bodyStart + contentLength);
        handleMessage(JSON.parse(body.toString("utf8")) as { id?: number; method: string });
    }
}

function handleMessage(message: { id?: number; method: string; params?: { source?: string } }): void {
    if (mode === "no-handshake") {
        return;
    }

    if (message.method === "handshake" && typeof message.id === "number") {
        write({
            id: message.id,
            result: {
                protocolVersion: 1,
                formatterVersion: "fixture",
                capabilities: { formatDocument: true, languages: ["csharp"] },
            },
        });
        return;
    }

    if (message.method === "formatDocument" && typeof message.id === "number") {
        if (mode === "crash-on-format" && message.params?.source === "crash") {
            process.exit(3);
        }

        if (mode === "timeout-format") {
            return;
        }

        write({ id: message.id, result: { changes: [] } });
        return;
    }

    if (message.method === "shutdown" && typeof message.id === "number") {
        write({ id: message.id, result: {} });
        process.exit(0);
    }
}

function write(message: unknown): void {
    const body = Buffer.from(JSON.stringify(message), "utf8");
    process.stdout.write(Buffer.concat([Buffer.from(`Content-Length: ${body.length}\r\n\r\n`, "ascii"), body]));
}
