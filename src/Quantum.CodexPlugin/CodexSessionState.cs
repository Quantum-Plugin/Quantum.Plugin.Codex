using Quantum.OfficialPlugins.Codex.Application;

namespace Quantum.OfficialPlugins.Codex;

internal sealed record CodexSessionState
{
    public string? ThreadId { get; set; }
    public required string WorkingDirectory { get; set; }
    public required string Model { get; set; }
    public CodexChatMessage[] Messages { get; set; } = [];
    public Dictionary<string, string> PendingPluginInstalls { get; set; } = [];
}
