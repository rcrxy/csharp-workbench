# CSharpWorkbench Formatting Core

This directory contains the reusable formatting engine for C# Workbench.

## Projects

- `CSharpWorkbench.Formatting.Core` targets `netstandard2.0` and owns language-neutral formatting contracts and dispatch.
- `CSharpWorkbench.Formatting.Core.Tests` targets `net10.0` and validates the Core independently of VS Code and process hosting.

Roslyn is the implementation technology for C# formatting. It is not the boundary of the formatting engine.

## Boundary

TypeScript resolves `.editorconfig` files and collects editor fallback values. It passes raw key/value properties and fallback values to Formatting Core. The C# option resolver interprets standard and `csharp_*` properties, creates typed options, and maps them to Roslyn.

`FormattingEngine` currently dispatches document requests to the C# formatter. Existing C# range and snippet behavior remains inside the C# implementation boundary.

The formatter tool and wire protocol are separate layers and are not part of this project.

## Tests

Run the Formatting Core tests directly:

```shell
dotnet test ./formatting/CSharpWorkbench.Formatting.slnx --configuration Release
```

The repository-level `npm run pretest` command also runs the Core tests.
