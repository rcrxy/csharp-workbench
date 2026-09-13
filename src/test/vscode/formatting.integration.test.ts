import assert from "node:assert/strict";
import * as vscode from "vscode";

const extensionId = "zeco.csharp-workbench";
const formattingOptions: vscode.FormattingOptions = {
    insertSpaces: true,
    tabSize: 8,
};

suite("VS Code formatting provider integration", () => {
    suiteSetup(async () => {
        const extension = vscode.extensions.getExtension(extensionId);
        assert.ok(extension, `Extension ${extensionId} is not loaded in the Extension Host.`);
        await extension.activate();
    });

    test("formats C# documents and ranges through the registered providers", async () => {
        const document = await openWorkspaceDocument("Demo.cs", "csharp");
        const documentOutput = applyTextEdits(document, await executeDocumentFormatting(document));

        assert.match(documentOutput, /^class Demo \{/u);
        assert.match(documentOutput, /^ {3}void Run\(int left,int right\) \{/mu);
        assert.match(documentOutput, /^ {6}if \(left < right\) \{/mu);

        const selectedSource = "if(left<right){System.Console.WriteLine(left);}";
        const selectedStart = document.getText().indexOf(selectedSource);
        assert.notEqual(selectedStart, -1, "C# range fixture is missing the selected source.");
        const range = new vscode.Range(
            document.positionAt(selectedStart),
            document.positionAt(selectedStart + selectedSource.length),
        );
        const rangeOutput = applyTextEdits(document, await executeRangeFormatting(document, range));

        assert.equal(rangeOutput.slice(0, selectedStart), document.getText().slice(0, selectedStart));
        assert.match(rangeOutput, /if \(left < right\) \{/u);
        assert.match(rangeOutput, /System\.Console\.WriteLine\(left\);/u);
    });

    test("formats Razor documents and ranges through the registered providers", async () => {
        const document = await openWorkspaceDocument("Component.razor", "razor");
        const documentOutput = applyTextEdits(document, await executeDocumentFormatting(document));

        assert.match(documentOutput, /^<div>\n {3}<Widget Value="@\(left \+ right\)" \/>\n<\/div>$/u);
        assert.equal(await formatAttributeRange(document), '<div><Widget Value="@(left + right)" /></div>\n');
    });

    test("formats CSHTML documents and ranges through the registered providers", async () => {
        const document = await openWorkspaceDocument("Page.cshtml", "razor");
        const documentOutput = applyTextEdits(document, await executeDocumentFormatting(document));

        assert.match(documentOutput, /^<section>\n {3}<Widget Value="@\(left \+ right\)" \/>\n<\/section>$/u);
        assert.equal(await formatAttributeRange(document), '<section><Widget Value="@(left + right)" /></section>\n');
    });
});

async function openWorkspaceDocument(fileName: string, expectedLanguageId: string): Promise<vscode.TextDocument> {
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    assert.ok(workspaceFolder, "The VS Code test workspace is not open.");

    const document = await vscode.workspace.openTextDocument(vscode.Uri.joinPath(workspaceFolder.uri, fileName));
    assert.equal(document.languageId, expectedLanguageId, `${fileName} opened with an unexpected language ID.`);
    return document;
}

async function executeDocumentFormatting(document: vscode.TextDocument): Promise<readonly vscode.TextEdit[]> {
    const edits = await vscode.commands.executeCommand<vscode.TextEdit[] | undefined>(
        "vscode.executeFormatDocumentProvider",
        document.uri,
        formattingOptions,
    );
    assert.ok(edits, `No document formatting provider handled ${document.uri.toString()}.`);
    assert.ok(edits.length > 0, `Document formatting returned no edits for ${document.uri.toString()}.`);
    return edits;
}

async function executeRangeFormatting(
    document: vscode.TextDocument,
    range: vscode.Range,
): Promise<readonly vscode.TextEdit[]> {
    const edits = await vscode.commands.executeCommand<vscode.TextEdit[] | undefined>(
        "vscode.executeFormatRangeProvider",
        document.uri,
        range,
        formattingOptions,
    );
    assert.ok(edits, `No range formatting provider handled ${document.uri.toString()}.`);
    assert.ok(edits.length > 0, `Range formatting returned no edits for ${document.uri.toString()}.`);
    return edits;
}

async function formatAttributeRange(document: vscode.TextDocument): Promise<string> {
    const source = document.getText();
    const selectedStart = source.indexOf("Value");
    assert.notEqual(selectedStart, -1, `${document.uri.toString()} is missing the selected attribute.`);
    const range = new vscode.Range(document.positionAt(selectedStart), document.positionAt(selectedStart + "Value".length));
    return applyTextEdits(document, await executeRangeFormatting(document, range));
}

function applyTextEdits(document: vscode.TextDocument, edits: readonly vscode.TextEdit[]): string {
    const replacements = edits
        .map(edit => ({
            start: document.offsetAt(edit.range.start),
            end: document.offsetAt(edit.range.end),
            newText: edit.newText,
        }))
        .sort((left, right) => right.start - left.start);
    let result = document.getText();
    let previousStart = result.length;

    for (const replacement of replacements) {
        assert.ok(replacement.start >= 0 && replacement.end >= replacement.start && replacement.end <= result.length);
        assert.ok(replacement.end <= previousStart, "Formatting provider returned overlapping text edits.");
        result = result.slice(0, replacement.start) + replacement.newText + result.slice(replacement.end);
        previousStart = replacement.start;
    }

    return result;
}
