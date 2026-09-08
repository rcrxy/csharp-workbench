import { defineConfig } from '@vscode/test-cli';

export default defineConfig({
    files: "out/test/vscode/**/*.test.js",
    workspaceFolder: "src/test/vscode/workspace",
    mocha: {
        timeout: 30_000,
    },
});
