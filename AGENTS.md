# Repository instructions

- Follow the [English development standards](.standards/docs/en/README.md) at the commit recorded by this repository. Use the contributor-approved Git identity for both author and committer; never invent a placeholder identity.

- Follow the [English documentation](docs/en/README.md) and [development rules](docs/en/contributing.md).
- Keep English and Simplified Chinese in separate, mutually linked files under docs/en and docs/zh-CN. This file stays English only.
- Reference the published Quantum.Plugin.Abstraction package. Do not add source references to a sibling repository, host or another plugin.
- Preserve plugin IDs, component names, published CLR namespaces, request names, migration contents and application-data keys.
- Put production types in separate files, except directly associated request/response messages under Contract/Requests.
- Reuse NOF-generated local clients, dispatch and mapping. Domain objects own invariants. Cross-plugin requests use named host capabilities, never another plugin assembly.
- Initialization, runtime adapters and database contributors stay at the project root. UI and localization belong under UI. Tests mirror production responsibilities.
- Explain jargon with direct links to the relevant [glossary](docs/en/glossary.md) entry.
- Follow .editorconfig: var, control-flow braces, PascalCase static fields, _camelCase private instance fields, single-line expression bodies.
- Run .ci/Format.ps1, solution tests, frontend checks and .ci/Build.ps1 before handoff.
- Put auxiliary tools in dot-prefixed directories. Public files must omit workstation paths, private identifiers, credentials and investigation logs.
