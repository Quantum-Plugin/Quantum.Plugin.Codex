using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class StartCodexThread(ICodexRuntime codex) : CodexRpcService.StartNewThread
{
    public override async Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexNewThreadRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await codex.StartNewThreadAsync(request.Model, cancellationToken);
        var snapshot = codex.Snapshot;
        if (snapshot.ConnectionState != CodexConnectionState.Connected)
        {
            throw new DomainException("codex_connection_failed", snapshot.ErrorMessage ?? "Codex connection did not complete.");
        }

        return snapshot;
    }
}
