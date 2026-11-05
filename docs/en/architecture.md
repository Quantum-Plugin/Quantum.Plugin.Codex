# Architecture and contracts

[简体中文](../zh-CN/architecture.md)

Source uses technical responsibilities: Contract for messages, Application for use cases, Domain for invariants and [UI](glossary.md#ui) for pages/localization. Runtime adapters, initialization and database contributors stay at the project root. Tests mirror these responsibilities.

[NOF](glossary.md#nof) generates [RPC](glossary.md#rpc) clients and servers. Pages use local generated clients. Cross-plugin requests use IQuantumRequester and named capabilities. The host owns routing, serialization, database lifetime and installation. Host and cross-plugin integration tests belong to the consuming host.

| Identity | Value |
| --- | --- |
| Plugin | quantum.plugin.codex |
| Assembly | Quantum.CodexPlugin |

Preserve existing [CLR](glossary.md#clr) names, named requests, route components and migration ordering. The host executes database migrations; never edit a released migration. Add a new numbered migration for new schema changes. Public contracts are defined by:

- [ICodexRpcService.cs](../../src/Quantum.CodexPlugin/Contract/ICodexRpcService.cs)
- [ICodexSpeechRpcService.cs](../../src/Quantum.CodexPlugin/Contract/ICodexSpeechRpcService.cs)
- [CodexAttachmentInfo.cs](../../src/Quantum.CodexPlugin/Contract/Models/CodexAttachmentInfo.cs)
- [CodexChatMessage.cs](../../src/Quantum.CodexPlugin/Contract/Models/CodexChatMessage.cs)
- [CodexConnectionState.cs](../../src/Quantum.CodexPlugin/Contract/Models/CodexConnectionState.cs)
- [CodexIntegrationSnapshot.cs](../../src/Quantum.CodexPlugin/Contract/Models/CodexIntegrationSnapshot.cs)
- [CodexMessageRole.cs](../../src/Quantum.CodexPlugin/Contract/Models/CodexMessageRole.cs)
- [CodexModelOption.cs](../../src/Quantum.CodexPlugin/Contract/Models/CodexModelOption.cs)
- [CodexAutoConnectRequest.cs](../../src/Quantum.CodexPlugin/Contract/Requests/CodexAutoConnectRequest.cs)
- [CodexChangeModelRequest.cs](../../src/Quantum.CodexPlugin/Contract/Requests/CodexChangeModelRequest.cs)
- [CodexConnectRequest.cs](../../src/Quantum.CodexPlugin/Contract/Requests/CodexConnectRequest.cs)
- [CodexDisconnectRequest.cs](../../src/Quantum.CodexPlugin/Contract/Requests/CodexDisconnectRequest.cs)
- [CodexModelsRequest.cs](../../src/Quantum.CodexPlugin/Contract/Requests/CodexModelsRequest.cs)
- [CodexNewThreadRequest.cs](../../src/Quantum.CodexPlugin/Contract/Requests/CodexNewThreadRequest.cs)
- [CodexSendRequest.cs](../../src/Quantum.CodexPlugin/Contract/Requests/CodexSendRequest.cs)
- [CodexSnapshotRequest.cs](../../src/Quantum.CodexPlugin/Contract/Requests/CodexSnapshotRequest.cs)
- [CodexTranscribeRequest.cs](../../src/Quantum.CodexPlugin/Contract/Requests/CodexTranscribeRequest.cs)
- [RefreshCodexMobilePageRequest.cs](../../src/Quantum.CodexPlugin/Contract/Requests/RefreshCodexMobilePageRequest.cs)
