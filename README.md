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
- Applies EditorConfig indentation, new-line, spacing, wrapping, line-ending, final-newline, trailing-whitespace, charset,
	and maximum-line-length rules implemented by Workbench.
- Indents embedded statements without braces and supports `switch`/`case` indentation behavior.
- Protects comments, regular strings, verbatim strings, raw strings, character literals, generics, and unary operators
	from unrelated text transformations.

### Razor And HTML Formatting

- Formats Razor and CSHTML tags using ReSharper/Rider-compatible `html_*` rule names.
- Supports attribute spacing, attribute layout, multiline attribute indentation, element line breaks, blank-line limits,
	self-closing tag spacing, and whitespace-sensitive element protection.
- Supports `html_attribute_wrap = off` and `normal`; `normal` first builds a normalized single-line opening tag and
	uses its visual width to decide whether the configured attribute layout should become multiline.
- Accepts compatible `resharper_html_*` property names while documenting the unprefixed `html_*` form.
- Reuses the C# formatter for Razor `@code` and `@functions` blocks.
- Protects embedded C# while parsing tags so generics and comparison operators are not mistaken for markup.

### EditorConfig Defaults And Priority

All fixed formatting defaults are expressed in the bundled
[default EditorConfig Profile](src/core/editorConfig/profiles/default.editorconfig).

The general resolution order is:

1. Matching project `.editorconfig` property.
2. Current VS Code editor or document state for dynamic values such as indentation, EOL, and wrapping column.
3. Bundled default EditorConfig Profile.
4. Defensive code fallback when no valid Profile value is available.

See the [supported configuration reference](SUPPORTED_CONFIGURATIONS.md) for the complete property list, values,
defaults, compatibility aliases, and language-specific priority rules.

## Current Scope

The formatter implements the documented rule set with syntax-aware lexical protection, but it is not a replacement
for the complete Roslyn or JetBrains syntax tree. Unsupported or ambiguous constructs are left unchanged where
possible. `max_line_length` currently wraps C# at safe comma and binary-operator boundaries and, with
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
│  └─ editorConfig/             # EditorConfig parsing, models, and bundled default Profile
├─ features/
│  ├─ fileCreation/             # Commands, models, renderers, services and templates
│  └─ formatting/               # C#, Razor, and HTML formatting providers and services
├─ shared/
│  └─ csharp/                   # Reusable C# project and language capabilities
└─ index.ts                     # Feature composition root
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
npm run test:coverage
```

The coverage command enforces at least 90% line and function coverage for the core C# code-style and Razor tag
formatting services.
