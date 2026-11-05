using NOF.Contract;
using Quantum.Plugin.Abstraction;
using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

[Description("Read and control the official Quantum Codex integration. Connecting and sending may start a local Codex process or model task; obtain user authorization for the corresponding operation before calling.")]
[TransportOverQuantum]
public interface ICodexRpcService : IRpcService
{
    [Description("Attempt automatic connection once per plugin runtime. Called after the first interactive page render. Shares suppression state with manual disconnect and does not start duplicate processes. Failure details are retained in the returned snapshot.")]
    [RequestCapability("codex.ensure-auto-connected", QuantumDispatch.Ordered, UniqueOwner = true, RequireSingleProvider = true)]
    Result<CodexIntegrationSnapshot> EnsureAutoConnected(CodexAutoConnectRequest request);
    [Description("Read connection state, thread ID, activity, models, and current messages without starting a Codex process.")]
    [RequestCapability("codex.snapshot", QuantumDispatch.Ordered, UniqueOwner = true, RequireSingleProvider = true)]
    Result<CodexIntegrationSnapshot> GetSnapshot(CodexSnapshotRequest request);
    [Description("Read the cached model list: initial choices before connection and App Server choices after connection. Does not start a model request.")]
    [RequestCapability("codex.models", QuantumDispatch.Ordered, UniqueOwner = true, RequireSingleProvider = true)]
    Result<CodexModelOption[]> ListModels(CodexModelsRequest request);
    [Description("Connect to the local Codex App Server, resume any saved conversation, and expose tools from other Quantum plugins. Empty conversations create their external thread only on the first send. Requires a local Codex installation and sign-in.")]
    [RequestCapability("codex.connect", QuantumDispatch.Ordered, UniqueOwner = true, RequireSingleProvider = true)]
    Result<CodexIntegrationSnapshot> Connect(CodexConnectRequest request);
    [Description("Reset the conversation with an optional model. Clears messages and the thread ID; the external thread is created on the first send. Does not send a prompt.")]
    [RequestCapability("codex.new-thread", QuantumDispatch.Ordered, UniqueOwner = true, RequireSingleProvider = true)]
    Result<CodexIntegrationSnapshot> StartNewThread(CodexNewThreadRequest request);
    [Description("Change the model for subsequent turns while retaining the thread ID and messages. Does not start inference. Rejected while a turn is running.")]
    [RequestCapability("codex.change-model", QuantumDispatch.Ordered, UniqueOwner = true, RequireSingleProvider = true)]
    Result<CodexIntegrationSnapshot> ChangeModel(CodexChangeModelRequest request);
    [Description("Send text, selected images/files or recorded voice to the current thread. Voice is recognized through speech.transcribe before the recognized text starts a turn in the existing conversation. Poll snapshot for progress and errors. Rejected while a turn is running.")]
    [RequestCapability("codex.send", QuantumDispatch.Ordered, UniqueOwner = true, RequireSingleProvider = true)]
    Result<CodexIntegrationSnapshot> Send(CodexSendRequest request);
    [Description("Disconnect and stop the App Server process owned by this plugin. May interrupt a running model task. A disconnected state is treated as success.")]
    [RequestCapability("codex.disconnect", QuantumDispatch.Ordered, UniqueOwner = true, RequireSingleProvider = true)]
    Result<CodexIntegrationSnapshot> Disconnect(CodexDisconnectRequest request);
    [Description("Refresh the published mobile page from current desktop data. Reopen or refresh the mobile view afterwards to display the new snapshot.")]
    [RequestCapability("codex.mobile-refresh", QuantumDispatch.Ordered, UniqueOwner = true, RequireSingleProvider = true)]
    Result RefreshMobilePage(RefreshCodexMobilePageRequest request);
}
