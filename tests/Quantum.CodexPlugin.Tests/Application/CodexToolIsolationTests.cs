using Quantum.OfficialPlugins.Codex.Application;
using System.Text.Json;
using Xunit;

namespace Quantum.CodexPlugin.Tests.Application;

public sealed class CodexToolIsolationTests
{
    [Fact]
    public void SelfControlRpcMethodsAreNotInjectedIntoTheSameCodexSession()
    {
        var catalog = JsonSerializer.SerializeToElement(new { schemaVersion = 2, capabilities = new[] { Method("quantum.capabilities.list"), Method("codex.send"), Method("codex.disconnect"), Method("speech.transcribe", "quantum.plugin.codex"), Method("ledger.list"), Method("calendar.create") } });
        var tools = QuantumRpcToolCatalog.Parse(catalog);
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, tool => tool.RpcName == "ledger.list");
        Assert.Contains(tools, tool => tool.RpcName == "calendar.create");
    }

    private static object Method(string name, string providerPluginId = "test.provider") => new
    {
        name,
        kind = "request",
        providerPluginIds = new[]
        {
            providerPluginId
        },
        description = name,
        returnsValue = true,
        requestSchema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { } })
    };
}
