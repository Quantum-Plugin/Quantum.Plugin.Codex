# 开发

[English](../en/contributing.md)

遵循 .editorconfig：推断类型时使用 var，控制流必须有大括号，静态字段使用 PascalCase，私有实例字段使用 _camelCase，单行方法使用表达式体，多语句方法使用块体。每个生产文件放一个类型，直接关联的请求与响应消息可放一起。测试对应源码职责。

```sh
dotnet restore Quantum.Plugin.Codex.slnx
pwsh .ci/Format.ps1
npm ci --prefix .ci
npm run format --prefix .ci
dotnet test Quantum.Plugin.Codex.slnx -c Release
node --test tests/Quantum.CodexPlugin.Tests/Web/test-codex-media.mjs tests/Quantum.CodexPlugin.Tests/Web/test-codex-composer.mjs
pwsh .ci/Build.ps1
```

使用 `pwsh .ci/Format.ps1 -Verify` 与 `npm run check --prefix .ci` 验证。脚本检查空白与要求的样式诊断；人工复查 Razor 标记，不修改 obj 中的生成文件。

英中双语文档放在 docs/en 和 docs/zh-CN 下独立文件并互相链接，AGENTS.md 只保留英文。行话和缩写须链接到[术语表](glossary.md)对应段落并同步维护双语解释。公开示例使用虚构标识，排除本地路径、私有标识、凭据、构建产物及排查日志。

| 约定 | .editorconfig 设置 |
| --- | --- |
| 优先 var | csharp_style_var_for_built_in_types, csharp_style_var_when_type_is_apparent, csharp_style_var_elsewhere |
| 控制流大括号 | csharp_prefer_braces |
| 单行方法表达式体 | csharp_style_expression_bodied_methods |
| 静态字段 PascalCase | dotnet_naming_rule.static_fields.*, dotnet_naming_rule.private_static_fields.* |
| 私有实例字段前缀 | dotnet_naming_rule.private_fields.* |
| 四空格与行尾 | indent_style, indent_size, end_of_line, insert_final_newline |
