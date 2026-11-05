# Development

[简体中文](../zh-CN/contributing.md)

Follow .editorconfig: var where inferred, braces for control flow, PascalCase static fields, _camelCase private instance fields, expression bodies for single-line methods and blocks for multiple statements. Keep one production type per file except directly associated request/response messages. Match tests to source responsibilities.

```sh
dotnet restore Quantum.Plugin.Codex.slnx
pwsh .ci/Format.ps1
npm ci --prefix .ci
npm run format --prefix .ci
dotnet test Quantum.Plugin.Codex.slnx -c Release
node --test tests/Quantum.CodexPlugin.Tests/Web/test-codex-media.mjs tests/Quantum.CodexPlugin.Tests/Web/test-codex-composer.mjs
pwsh .ci/Build.ps1
```

Use `pwsh .ci/Format.ps1 -Verify` and `npm run check --prefix .ci` for verification. The script checks whitespace and the required style diagnostics. Review Razor markup manually; do not edit generated obj files.

Keep English and Simplified Chinese in separate, mutually linked files under docs/en and docs/zh-CN. AGENTS.md stays English only. Link jargon and abbreviations to their [glossary](glossary.md) entries and maintain both definitions. Use fictional public examples. Exclude local paths, private identifiers, credentials, build outputs and investigation logs.

| Convention | .editorconfig setting |
| --- | --- |
| Prefer var | csharp_style_var_for_built_in_types, csharp_style_var_when_type_is_apparent, csharp_style_var_elsewhere |
| Control-flow braces | csharp_prefer_braces |
| Single-line method expression | csharp_style_expression_bodied_methods |
| Static field PascalCase | dotnet_naming_rule.static_fields.*, dotnet_naming_rule.private_static_fields.* |
| Private instance field prefix | dotnet_naming_rule.private_fields.* |
| Four spaces and line endings | indent_style, indent_size, end_of_line, insert_final_newline |
