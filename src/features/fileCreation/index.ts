import * as vscode from "vscode";
import { createCSharpFile } from "./commands/createCSharpFile";
import { createRazorFile } from "./commands/createRazorFile";
import { createCSharpFileCommands, createRazorFileCommands } from "./commands";
import { csharpTemplateOptions } from "./models/csharpTemplate";
import { razorTemplateOptions } from "./models/razorTemplate";

const FILE_CREATION_ENABLED = "csharpWorkbench.fileCreation.enabled";

export function registerFileCreationFeature(context: vscode.ExtensionContext): void {
    const createFileDisposables = csharpTemplateOptions.map(template =>
        registerFileCreationCommand(createCSharpFileCommands[template.templateKind], uri =>
            createCSharpFile(template, uri),
        ),
    );
    const createRazorFileDisposables = razorTemplateOptions.map(template =>
        registerFileCreationCommand(createRazorFileCommands[template.templateKind], uri =>
            createRazorFile(template, context.extensionUri, uri),
        ),
    );

    context.subscriptions.push(...createFileDisposables, ...createRazorFileDisposables);
}

function registerFileCreationCommand(command: string, create: (uri?: vscode.Uri) => Promise<void>): vscode.Disposable {
    return vscode.commands.registerCommand(command, (uri?: vscode.Uri) => {
        if (vscode.workspace.getConfiguration().get<boolean>(FILE_CREATION_ENABLED) === true) {
            return create(uri);
        }
    });
}
