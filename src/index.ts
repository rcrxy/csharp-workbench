import * as vscode from "vscode";
import type { ExtensionFeature } from "./core/extensionFeature";
import { registerFileCreationFeature } from "./features/fileCreation";
import { disposeFormattingFeature, registerFormattingFeature } from "./features/formatting";

const features: readonly ExtensionFeature[] = [registerFileCreationFeature, registerFormattingFeature];

/**
 * 激活扩展，并依次注册各个独立功能模块。
 */
export async function activate(context: vscode.ExtensionContext): Promise<void> {
    for (const registerFeature of features) {
        await registerFeature(context);
    }
}

/**
 * 扩展停用入口。等待 Formatter 子进程完成协议关闭。
 */
export async function deactivate(): Promise<void> {
    await disposeFormattingFeature();
}
