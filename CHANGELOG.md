## [0.1.0] - 2026-09-04

- Add length-aware Razor and HTML attribute wrapping with `html_attribute_wrap = normal`.
- Apply `html_attribute_style` and `html_attribute_indent` after a normalized opening tag exceeds `max_line_length`.
- Preserve `do_not_touch`, recover multiline tags that fit on one line, and measure indentation using visual tab width.
- Document supported and parsed-only HTML attribute wrapping policies.

## [Unreleased]

- Add shared EditorConfig parsing with normalized indentation fallback.
- Add a conservative Razor document formatting provider for safe Markup regions.
- Bundle the self-contained Formatter runtime in platform-specific Windows x64 and Linux glibc x64 VSIX packages.
- Run document and selection formatting through the bundled Formatter without requiring a system .NET installation.
- Declare virtual workspaces unsupported, add host/runtime diagnostics, and document the verified Windows x64 and WSL
  glibc x64 hosts.

## [0.0.1] - 2026-08-30

- Add C# file templates.
- Add Razor single-file and three-file templates.
- Add namespace and language-version awareness.
