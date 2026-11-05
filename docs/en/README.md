# Quantum.Plugin.Codex

[简体中文](../zh-CN/README.md)

Conversations, approvals and workspace tools through a separately installed Codex [CLI](glossary.md#cli).

[Architecture](architecture.md) · [Development](contributing.md) · [Glossary](glossary.md)

Requires .NET 10. Dependencies restore from public NuGet. The published [SDK](glossary.md#sdk) version is fixed in Directory.Packages.props. No sibling checkout is needed.

```sh
dotnet restore Quantum.Plugin.Codex.slnx
dotnet test Quantum.Plugin.Codex.slnx -c Release
pwsh .ci/Build.ps1
```

The build helper creates a validated archive in artifacts. Extract it for the Quantum plugin loader; keep the manifest and wwwroot alongside the assembly. [CI](glossary.md#ci) verifies Windows and Linux.

Install and authenticate Codex before connecting. Models come from that local installation. An empty selection uses the first available model; saved explicit selections must still be available. Speech uses System.Speech on Windows. Normal tests skip opt-in live audio checks requiring a local recording.

[External software notices](../../src/Quantum.CodexPlugin/THIRD-PARTY-NOTICES.md) describe Codex and Markdig attribution.

[Apache License 2.0](../../LICENSE) · [NOTICE](../../NOTICE)

## Development standards

Follow the [English development standards](../../.standards/docs/en/README.md). The .standards submodule records the exact reviewed revision; upstream changes apply only after an explicit update of that recorded commit. Initialize it before development:

```sh
git submodule update --init --recursive
python .standards/.ci/check-adoption.py
```

Keep both language editions aligned. AGENTS.md links to the adopted English entry point; project-specific instructions supplement the shared requirements.
