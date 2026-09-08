import { parse } from "editorconfig";
import type * as vscode from "vscode";

export type ResolvedEditorConfigProperties = Readonly<Record<string, string>>;

export async function resolveRawEditorConfig(resource: vscode.Uri): Promise<ResolvedEditorConfigProperties> {
    if (resource.scheme !== "file") {
        return {};
    }

    try {
        const properties = await parse(resource.fsPath, { unset: true });
        return Object.fromEntries(Object.entries(properties).map(([key, value]) => [key, String(value)]));
    } catch {
        return {};
    }
}
