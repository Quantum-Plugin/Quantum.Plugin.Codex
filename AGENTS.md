# Repository instructions

- Follow the [English development standards](.standards/docs/en/README.md) at the commit recorded by this repository. Use the contributor-approved Git identity for both author and committer; never invent a placeholder identity.
- Follow the [Git branching standards](.standards/docs/en/02-development/git-branching.md) before editing: inspect the branch and working tree, refresh the approved target and create or reuse a compliant topic branch without discarding unrelated work. Do not edit, commit or push directly on protected branches; merge through a change request with required checks and approvals. Administrator access and passing tests do not waive this workflow.
- Keep temporary and one-off scripts/data, ad hoc acceptance checks, individual-task evidence, per-run histories and temporary investigation/handoff notes out of version control under [MNT-09](.standards/docs/en/05-maintenance/maintenance-standards.md#keep-acceptance-records-outside-source-repositories). Hidden directories do not exempt these files. Store retained evidence externally; version only maintained project assets with a lasting purpose, ownership and appropriate review/verification.

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
