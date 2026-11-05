using NOF.Contract;
using NOF.Domain;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed class ConnectCodex(ICodexRuntime codex) : CodexRpcService.Connect
{
    public override async Task<Result<CodexIntegrationSnapshot>> HandleAsync(CodexConnectRequest request, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.WorkingDirectory is null)
        {
            throw new DomainException("codex_invalid_request", "Working directory is required.");
        }

        if (request.Model is null)
        {
            throw new DomainException("codex_invalid_request", "Model must be a string; an empty value selects a discovered model.");
        }

        await codex.ConnectAsync(request.WorkingDirectory, request.Model, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = codex.Snapshot;
        if (snapshot.ConnectionState != CodexConnectionState.Connected)
        {
            throw new DomainException("codex_connection_failed", snapshot.ErrorMessage ?? "Codex connection did not complete.");
        }

        return snapshot;
    }
}
