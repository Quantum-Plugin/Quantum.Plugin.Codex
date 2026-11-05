using Quantum.OfficialPlugins.Codex.Application;

namespace Quantum.CodexPlugin.Tests.Fixtures;

internal static class CodexTestModels
{
    public const string DefaultId = "example-default";
    public static readonly IReadOnlyList<CodexModelOption> All = [new()
    {
        Id = DefaultId,
        DisplayName = "Example default"
    }, new()
    {
        Id = "example-code",
        DisplayName = "Example code"
    }, new()
    {
        Id = "example-fast",
        DisplayName = "Example fast"
    }, new()
    {
        Id = "example-general",
        DisplayName = "Example general"
    }

    ];
}
