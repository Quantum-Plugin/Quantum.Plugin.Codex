using System.ComponentModel;

namespace Quantum.OfficialPlugins.Codex.Application;

public sealed record CodexConnectRequest
{
    [Description("An existing local working directory for the first conversation; empty uses the fixed Quantum Codex workspace. Later connections resume the saved conversation and retain its directory. Connecting starts the local Codex App Server and exposes Quantum tools from other plugins.")]
    public string WorkingDirectory { get; set; } = string.Empty;

    [Description("Model ID supported by the local Codex installation. Empty selects the first model reported by the local Codex installation.")]
    public string Model { get; set; } = string.Empty;
}
