# Supported Configurations

[简体中文](SUPPORTED_CONFIGURATIONS.zh-cn.md)

This document records configuration properties that C# Workbench currently interprets and applies. A property being
returned by the `editorconfig` parser does not mean that Workbench implements its behavior.

## EditorConfig

### Built-In Default Profile

C# Workbench expresses its fixed default formatting style through the bundled
[default EditorConfig Profile](src/core/editorConfig/profiles/default.editorconfig). The Profile is parsed with the
same EditorConfig section matching used for project files and explicitly covers every property that Workbench applies.
Microsoft/.NET SDK values are used for supported C# formatting properties. HTML properties use the corresponding
JetBrains ReSharper/Rider HTML rule names and compatible values. Properties without a published upstream default use
the documented Workbench compatibility default.

The built-in Profile is the final fallback only. A matching project `.editorconfig` always has priority over it.
Values derived from the active editor or document remain dynamic and are resolved before the Profile, so the Profile
does not force a fixed tab size, line ending, maximum line length, final newline, whitespace cleanup, or charset over
the current editing context.

The general resolution order is:

1. Matching project `.editorconfig` property.
2. Current VS Code editor or document state, for properties with a dynamic equivalent.
3. Bundled EditorConfig Profile, sourced from Microsoft, JetBrains, or the documented compatibility default.
4. Defensive code fallback, used only if the Profile cannot provide a valid value.

### Applied Properties

| Property       | Supported values        | Behavior                                                                                |
| -------------- | ----------------------- | --------------------------------------------------------------------------------------- |
| `indent_style` | `space`, `tab`          | Selects spaces or tab characters for each indentation level.                            |
| `indent_size`  | Positive integer, `tab` | Sets the logical indentation size. When set to `tab`, the resolved `tab_width` is used. |
| `tab_width`    | Positive integer        | Sets the tab width and resolves the size of `indent_size = tab`.                        |
| `max_line_length` | Positive integer, `off` | Wraps C# at safe comma and binary-operator boundaries and, when HTML wrapping is enabled, wraps long Razor/HTML opening tags. `off` disables length-based wrapping. |
| `end_of_line`  | `lf`, `crlf`            | Normalizes document line endings to the configured newline style.                       |
| `insert_final_newline` | `true`, `false` | Ensures the file ends with or without a final newline.                              |
| `trim_trailing_whitespace` | `true`, `false` | Removes trailing spaces and tabs before line breaks when enabled.               |
| `charset` | `utf-8`, `utf-8-bom` | Adds or removes the UTF-8 BOM when formatting. Other EditorConfig charset values are parsed but do not trigger file transcoding. |

These properties currently apply to Razor and C# document formatting. `max_line_length` currently wraps C# documents
and C# code inside Razor `@code` and `@functions` blocks. Razor/HTML markup attributes use it when
`html_attribute_wrap = normal`.

For C# **Format Document**, all properties in this table are applied. **Format Selection** applies indentation and
`trim_trailing_whitespace` only within the selected full lines; it does not change document-wide line endings, the
final newline, or the BOM.

### C# Indentation

| Property                                 | Supported values                                   | Default                 | Behavior                                                                         |
| ---------------------------------------- | -------------------------------------------------- | ----------------------- | -------------------------------------------------------------------------------- |
| `csharp_indent_block_contents`           | `true`, `false`                                    | `true`                  | Indents statements and declarations inside brace-delimited blocks.               |
| `csharp_indent_braces`                   | `true`, `false`                                    | `false`                 | Adds one indentation level to block braces.                                      |
| `csharp_indent_case_contents`            | `true`, `false`                                    | `true`                  | Indents statements under `case` and `default` labels.                            |
| `csharp_indent_switch_labels`            | `true`, `false`                                    | `true`                  | Indents `case` and `default` labels relative to the containing switch statement. |
| `csharp_indent_case_contents_when_block` | `true`, `false`                                    | `true`                  | Indents an explicit block and its statements under a case label.                 |
| `csharp_indent_labels`                   | `flush_left`, `one_less_than_current`, `no_change` | `one_less_than_current` | Controls indentation of ordinary statement labels.                               |

Both **Format Document** and **Format Selection** are supported for C# documents.
Razor `@code` and `@functions` blocks reuse the same C# formatter through the `CSharpCodeFormatter` interface.
`registerFormattingFeature` accepts an optional `CSharpCodeFormatter` when a different implementation should be used
for embedded Razor C# code.

`csharp_indent_block_contents` also applies to C# statements and markup inside Razor control blocks. Supported control
flows include `@if`/`else if`/`else`, `@for`, `@foreach`, `@while`, `@switch`, `@using`, `@lock`,
`@try`/`catch`/`finally`, and `@do`/`while`. Their contents gain one indentation level when the property is enabled.

### C# New Lines

| Property                            | Supported values                                                  | Default |
| ----------------------------------- | ----------------------------------------------------------------- | ------- |
| `csharp_new_line_before_open_brace` | `all`, `none`, or a comma-separated list of Roslyn brace contexts | `all`   |
| `csharp_new_line_before_else`       | `true`, `false`                                                   | `true`  |
| `csharp_new_line_before_catch`      | `true`, `false`                                                   | `true`  |
| `csharp_new_line_before_finally`    | `true`, `false`                                                   | `true`  |
| `csharp_new_line_before_members_in_object_initializers` | `true`, `false` | `true` |
| `csharp_new_line_before_members_in_anonymous_types` | `true`, `false` | `true` |
| `csharp_new_line_between_query_expression_clauses` | `true`, `false` | `true` |

Supported brace contexts are `accessors`, `anonymous_methods`, `anonymous_types`, `control_blocks`, `events`,
`indexers`, `lambdas`, `local_functions`, `methods`, `object_collection_array_initializers`, `properties`, and `types`.

Razor control blocks also apply these new-line rules. `csharp_new_line_before_open_brace` uses the `control_blocks`
context for their opening braces, while `csharp_new_line_before_else`, `csharp_new_line_before_catch`, and
`csharp_new_line_before_finally` control whether those continuation keywords follow the previous closing brace on the
same line. Razor `@do`/`while` is formatted as `} while (...);`.

The object-initializer and anonymous-type rules place members after the first member on separate lines when enabled,
or join them with spaces when disabled. The query-expression rule similarly controls line breaks between top-level
query clauses. Nested initializers and nested query expressions are handled independently.

### C# Spacing

| Property                                                 | Supported values                     | Default            |
| -------------------------------------------------------- | ------------------------------------ | ------------------ |
| `csharp_space_after_keywords_in_control_flow_statements` | `true`, `false`                      | `true`             |
| `csharp_space_around_binary_operators`                   | `before_and_after`, `none`, `ignore` | `before_and_after` |
| `csharp_space_after_comma`                               | `true`, `false`                      | `true`             |
| `csharp_space_before_comma`                              | `true`, `false`                      | `false`            |
| `csharp_space_after_semicolon_in_for_statement`          | `true`, `false`                      | `true`             |
| `csharp_space_before_semicolon_in_for_statement`         | `true`, `false`                      | `false`            |
| `csharp_space_after_cast`                                | `true`, `false`                      | `false`            |
| `csharp_space_before_colon_in_inheritance_clause`        | `true`, `false`                      | `true`             |
| `csharp_space_after_colon_in_inheritance_clause`         | `true`, `false`                      | `true`             |
| `csharp_space_after_dot`                                 | `true`, `false`                      | `false`            |
| `csharp_space_before_dot`                                | `true`, `false`                      | `false`            |
| `csharp_space_before_open_square_brackets`               | `true`, `false`                      | `false`            |
| `csharp_space_between_empty_square_brackets`             | `true`, `false`                      | `false`            |
| `csharp_space_between_square_brackets`                   | `true`, `false`                      | `false`            |
| `csharp_space_around_declaration_statements`             | `ignore`, `false`                   | `false`            |
| `csharp_space_between_method_call_name_and_opening_parenthesis` | `true`, `false` | `false` |
| `csharp_space_between_method_call_parameter_list_parentheses` | `true`, `false` | `false` |
| `csharp_space_between_method_call_empty_parameter_list_parentheses` | `true`, `false` | `false` |
| `csharp_space_between_method_declaration_name_and_open_parenthesis` | `true`, `false` | `false` |
| `csharp_space_between_method_declaration_parameter_list_parentheses` | `true`, `false` | `false` |
| `csharp_space_between_method_declaration_empty_parameter_list_parentheses` | `true`, `false` | `false` |
| `csharp_space_between_parentheses` | `false`, or a comma-separated list of `control_flow_statements`, `expressions`, and `type_casts` | `false` |

Method-call and method-declaration settings independently control the space before `(` and the spaces immediately
inside empty or non-empty parameter lists. `csharp_space_between_parentheses` applies the same inner-boundary behavior
to the selected control-flow, parenthesized-expression, and cast contexts. Existing line breaks in multiline parameter
lists are preserved. Object creation, lambda parameter lists, tuples, and ambiguous parenthesis forms are left
unchanged by these rules.

### C# Wrapping And Preservation

| Property                                 | Supported values | Default |
| ---------------------------------------- | ---------------- | ------- |
| `csharp_preserve_single_line_statements` | `true`, `false`  | `true`  |
| `csharp_preserve_single_line_blocks`     | `true`, `false`  | `true`  |
| `dotnet_style_operator_placement_when_wrapping` | `beginning_of_line`, `end_of_line` | `beginning_of_line` |

Comments, regular strings, verbatim strings, raw strings, and character literals are protected from code-style text
transformations. The formatter is syntax-aware for the constructs listed above, but it is not a complete Roslyn syntax
tree implementation. Unsupported or ambiguous constructs are left unchanged where possible.

### HTML And Razor Tags

These property names follow the ReSharper/Rider HTML formatting rules. C# Workbench accepts both the documented
`html_*` form and the compatible `resharper_html_*` form. When both forms are present, the unprefixed `html_*` property
wins.

| Property                                                       | Supported values                                                                         | Default          | Behavior                                                                                                               |
| -------------------------------------------------------------- | ---------------------------------------------------------------------------------------- | ---------------- | ---------------------------------------------------------------------------------------------------------------------- |
| `html_spaces_around_eq_in_attribute`                           | `true`, `false`                                                                          | `false`          | Controls spaces around `=` in attributes.                                                                              |
| `html_space_after_last_attribute`                              | `true`, `false`                                                                          | `false`          | Controls the space between the last attribute and `>`.                                                                 |
| `html_space_before_self_closing`                               | `true`, `false`                                                                          | `true`           | Controls the space before `/>`.                                                                                        |
| `html_attribute_style`                                         | `on_single_line`, `first_attribute_on_single_line`, `on_different_lines`, `do_not_touch` | `on_single_line` | Controls attribute line layout.                                                                                        |
| `html_attribute_wrap`                                          | `off`, `normal`, `on_every_item`, `split_into_lines`                                     | `off`            | Controls length-based attribute wrapping. `normal` keeps a formatted opening tag on one line when it fits and applies `html_attribute_style` after it exceeds `max_line_length`. `on_every_item` and `split_into_lines` are parsed for compatibility but not currently applied. |
| `ij_html_attribute_wrap`                                       | `off`, `normal`, `on_every_item`, `split_into_lines`                                     | `off`            | Compatibility alias for `html_attribute_wrap`; the standard key takes priority. The two non-applied values remain parse-only. |
| `html_attribute_indent`                                        | `single_indent`, `double_indent`, `align_by_first_attribute`                             | `single_indent`  | Controls indentation of multiline attributes.                                                                          |
| `html_max_blank_lines_between_tags`                            | Non-negative integer                                                                     | `1`              | Limits blank lines between adjacent tags.                                                                              |
| `html_linebreak_before_all_elements`                           | `true`, `false`                                                                          | `false`          | Places every element on a new line when enabled.                                                                       |
| `html_linebreak_before_multiline_elements`                     | `true`, `false`                                                                          | `true`           | Places multiline elements on a new line.                                                                               |
| `html_linebreaks_inside_tags_for_multiline_elements`           | `true`, `false`                                                                          | `true`           | Places multiline element content between line breaks.                                                                  |
| `html_linebreaks_inside_tags_for_elements_with_child_elements` | `true`, `false`                                                                          | `true`           | Places child elements and the parent closing tag on separate lines when the parent has no direct text.                 |
| `html_no_indent_inside_elements`                               | Comma-separated element names                                                            | `pre,textarea`   | Prevents indentation changes inside the listed elements.                                                               |
| `html_preserve_spaces_inside_tags`                             | Comma-separated element names                                                            | `pre,textarea`   | Preserves the complete contents of the listed elements.                                                                |
| `html_extra_spaces`                                            | `remove_all`, `leave_tabs`, `leave_multiple`, `leave_all`                                | `remove_all`     | Removes redundant horizontal tag whitespace for `remove_all`; the `leave_*` values preserve existing extra whitespace. |

#### Attribute Wrapping

`html_attribute_wrap` decides whether an opening tag enters a multiline layout. `html_attribute_style` and
`html_attribute_indent` decide how attributes are arranged and indented after that transition.

With `html_attribute_wrap = normal`, the formatter first builds a normalized single-line candidate without relying on
the original tag's line breaks. Its visual width includes the tag's existing base indentation, and tabs advance to the
next `tab_width` boundary. The candidate remains on one line when its width is less than or equal to
`max_line_length`; it enters the configured multiline layout only when its width exceeds the limit.

The multiline layouts are:

- `on_single_line`: places all attributes together on one indented continuation line.
- `first_attribute_on_single_line`: keeps the first attribute beside the tag name and places each remaining attribute
	on a separate line.
- `on_different_lines`: places every attribute on a separate line.
- `do_not_touch`: preserves the existing attribute line structure.

`html_attribute_indent` controls the continuation indentation for all multiline layouts. Existing multiline tags are
collapsed when their normalized single-line candidate fits. A single overlong attribute value is never split, and the
formatter does not independently move `>` or `/>` to another line. `max_line_length = off` disables length-based
attribute wrapping.

The compatibility values `on_every_item` and `split_into_lines` are parsed but currently add no wrapping behavior.
Existing `html_attribute_style` behavior still applies when either value is configured.

HTML-specific indentation aliases are also supported:

```ini
html_indent_style = space
html_indent_size = 4
html_tab_width = 4
```

Razor directives and `@code`/`@functions` blocks are protected while tags are parsed. The embedded C# blocks are then
formatted by the configured `CSharpCodeFormatter`.

#### HTML Resolution Priority

HTML/Razor tag formatting uses this priority for every applicable setting:

1. The unprefixed `html_*` property from the project `.editorconfig`.
2. The compatible `resharper_html_*` property, then the equivalent standard project EditorConfig property when one exists.
3. The current VS Code editor setting.
4. The bundled ReSharper/Rider-compatible HTML Profile value.
5. Workbench's defensive runtime default.

For indentation, the complete chain is `html_indent_*` → `resharper_html_indent_*` → standard `indent_*`/
`tab_width` → current `TextEditor.options` → bundled Profile → runtime default. HTML rules without a standard
EditorConfig or VS Code equivalent fall back from their project language-specific forms to the bundled Profile and
then Workbench's runtime defaults.

### Resolution Priority

Dynamic indentation properties and `max_line_length` are resolved independently using this priority:

1. Matching `.editorconfig` property.
2. Current VS Code editor options.
3. Bundled EditorConfig Profile.

The VS Code fallback values are:

| Workbench value                | VS Code editor option             |
| ------------------------------ | --------------------------------- |
| Indentation style              | `TextEditor.options.insertSpaces` |
| Indentation size and tab width | `TextEditor.options.tabSize`      |
| Maximum line length            | `editor.wordWrapColumn`           |

The bundled Profile covers all currently applied properties. Its source order is:

1. Microsoft/.NET SDK defaults for C# formatting properties.
2. JetBrains ReSharper/Rider HTML property definitions for Razor/CSHTML formatting.
3. Workbench compatibility defaults when an upstream default is not published or the property is an alias.

The shared and document-specific sections include:

```ini
[*]
indent_style = space
max_line_length = 120
end_of_line = lf
trim_trailing_whitespace = false
charset = utf-8

[*.cs]
indent_size = 4
tab_width = 4
insert_final_newline = false

[*.{razor,cshtml}]
indent_size = 4
tab_width = 4
insert_final_newline = true
html_attribute_style = on_single_line
html_attribute_wrap = off
```

### Examples

Use two spaces for Razor files:

```ini
[*.razor]
indent_style = space
indent_size = 2
```

Use tabs with a width of four columns:

```ini
[*.razor]
indent_style = tab
indent_size = tab
tab_width = 4
```

Wrap Razor/HTML attributes only when the normalized opening tag exceeds 120 columns, keep the first attribute beside
the tag name, and align continuation attributes with it:

```ini
[*.razor]
indent_style = space
indent_size = 3
tab_width = 3
max_line_length = 120

html_attribute_wrap = normal
html_attribute_style = first_attribute_on_single_line
html_attribute_indent = align_by_first_attribute
```

Wrap C# code at 120 columns, or disable formatter-driven line wrapping:

```ini
[*.cs]
max_line_length = 120

[Generated/*.cs]
max_line_length = off
```

### EditorConfig Discovery

C# Workbench delegates EditorConfig discovery and matching to EditorConfig Core. This includes:

- Searching from the target file directory toward the filesystem root.
- Merging matching sections from multiple `.editorconfig` files.
- Applying section glob patterns.
- Stopping at `root = true`.
- Applying `unset` semantics.

### Parsed but Not Applied

Other EditorConfig and .NET code-style properties may be present in the parsed property map, but Workbench does not
currently execute them. This includes properties such as:

- `utf-16be`
- `utf-16le`
- `latin1` charset transcoding
- `dotnet_*`
- C# formatting properties other than the indentation, new-line, spacing, and preservation options listed above
- HTML formatting properties other than the tag rules listed above

Move a property into the applied-properties table only after a Workbench feature implements and validates its behavior.

## VS Code Settings

C# Workbench currently defines no extension-specific formatting-style settings. VS Code editor indentation options are
used only as fallback values when the corresponding `.editorconfig` properties are absent.
