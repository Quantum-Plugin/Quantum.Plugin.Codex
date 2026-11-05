# Quantum.Plugin.Codex

[English](../en/README.md)

通过单独安装的 Codex [CLI](glossary.md#cli) 提供对话、审批和工作区工具。

[架构](architecture.md) · [开发](contributing.md) · [术语表](glossary.md)

需要 .NET 10，依赖从公开 NuGet 源还原。已发布的 [SDK](glossary.md#sdk) 版本固定在 Directory.Packages.props，无需相邻项目源码。

```sh
dotnet restore Quantum.Plugin.Codex.slnx
dotnet test Quantum.Plugin.Codex.slnx -c Release
pwsh .ci/Build.ps1
```

构建工具在 artifacts 中生成经过验证的压缩包，解压后交给 Quantum 插件加载器；清单和 wwwroot 须与程序集一起保留。[CI](glossary.md#ci) 验证 Windows 和 Linux。

连接前单独安装并登录 Codex。可用模型来自该本地安装；空选择使用第一个可用模型，保存的显式选择仍须可用。语音通过 System.Speech 在 Windows 上提供。常规测试跳过需要本地录音并显式启用的真实音频检查。

[外部软件声明](../../src/Quantum.CodexPlugin/THIRD-PARTY-NOTICES.md) 说明 Codex 与 Markdig 的版权归属。

[Apache License 2.0](../../LICENSE) · [NOTICE](../../NOTICE)
