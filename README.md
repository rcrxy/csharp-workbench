# C# Workbench

C# Workbench is a collection of C# and Razor editing enhancements built on top of the official VS Code .NET tooling.

Configuration reference: [English](SUPPORTED_CONFIGURATIONS.md) | [简体中文](SUPPORTED_CONFIGURATIONS.zh-cn.md)

## Features

### Project-Aware File Creation

- Create classes, interfaces, records, structs, enums, abstract classes, and static classes from the Explorer context menu.
- Create a single Razor component or a Razor component with code-behind and scoped CSS files.
- Resolve the nearest C# project, root namespace, target framework, and C# language capabilities.
- Generate namespaces and Razor routes from the selected target directory.

### C# Formatting

- Supports **Format Document** and **Format Selection** for C# documents.
- Uses Roslyn syntax trees and formatting services for C# document, selection, and embedded Razor C# formatting.
- Applies project EditorConfig properties together with dynamic editor fallbacks and defaults resolved by the C#
	Formatting Core.

### Razor And HTML Formatting

- Formats Razor and CSHTML tags using ReSharper/Rider-compatible `html_*` rule names.
- Supports attribute spacing, attribute layout, multiline attribute indentation, element line breaks, blank-line limits,
	self-closing tag spacing, and whitespace-sensitive element protection.
- Supports `html_attribute_wrap = off` and `normal`; `normal` first builds a normalized single-line opening tag and
	uses its visual width to decide whether the configured attribute layout should become multiline.
- Accepts compatible `resharper_html_*` property names while documenting the unprefixed `html_*` form.
- Uses a source-preserving `RazorDocumentModel` for tags, attributes, control blocks, embedded C#, and range
	classification.
- Formats embedded C# with Roslyn while preserving Razor/markup boundaries.

### Formatting Architecture And Configuration

The TypeScript extension host resolves matching project `.editorconfig` files as raw string key/value pairs and sends
them to the bundled Formatter Tool. All option semantics and fixed formatting defaults are resolved in the C#
Formatting Core.

The general resolution order is:

1. Matching project `.editorconfig` property.
2. A compatible alias where the specific rule supports one.
3. Current VS Code editor or document state for applicable dynamic values such as indentation, EOL, and wrapping
	column.
4. The C# Formatting Core default.

See the [supported configuration reference](SUPPORTED_CONFIGURATIONS.md) for the complete property list, values,
defaults, compatibility aliases, and language-specific priority rules.

## Current Scope

The C# formatter is Roslyn-based. Razor and CSHTML formatting uses a source-preserving document model shared by tag,
attribute, embedded-C#, and range formatting. `max_line_length` currently wraps C# at supported syntax boundaries and, with
`html_attribute_wrap = normal`, wraps Razor markup attributes when the formatted opening tag exceeds the configured
visual width. The policies `on_every_item` and `split_into_lines` are parsed for compatibility but are not currently
applied as length-based wrapping policies. Multiline attribute arrangement remains controlled by
`html_attribute_style`, while `html_attribute_indent` controls continuation indentation.

## Bundled Formatter Runtime

Release packages include a self-contained Formatter runtime, so formatting does not require a system .NET
installation or a separately installed `dotnet` tool. Install the VSIX that matches the workspace extension host:

- `win32-x64` for Windows x64.
- `linux-x64` for glibc-based Linux x64, including conventional WSL, Remote SSH, and Dev Container hosts.

macOS, ARM, and Alpine Linux hosts are not supported by the 0.2.0 bundled runtime.

## Project Structure

```text
src/
├─ core/
│  └─ editorConfig/             # Raw EditorConfig resolution
├─ features/
│  ├─ fileCreation/             # Commands, models, renderers, services and templates
│  └─ formatting/               # VS Code providers, Formatter client, runtime adapter, and IPC
├─ shared/
│  └─ csharp/                   # Reusable C# project and language capabilities
└─ index.ts                     # Feature composition root

formatting/
└─ src/CSharpWorkbench.Formatting.Core/  # All C#, Razor, and HTML formatting semantics
```

Each feature exposes a registration function from its `index.ts`. The extension entry point only composes these
registrations. Features do not depend on each other; shared capabilities such as EditorConfig resolution belong in
`src/core`.

Formatting style is sourced from `.editorconfig`. VS Code settings remain appropriate for feature switches, UI,
performance, and other extension behavior, but do not duplicate code-style rules.

## Development

```powershell
npm install
npm run compile
npm run watch
```

Run static checks and unit tests:

```powershell
npm run pretest
npm run test:unit
```
