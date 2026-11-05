using NOF.Domain;
using Quantum.Plugin.Abstraction;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quantum.OfficialPlugins.Codex.Application;

internal static class QuantumRpcToolCatalog
{
    public const string Namespace = "quantum";
    public static IReadOnlyList<QuantumRpcTool> Parse(JsonElement catalog)
    {
        var tools = new List<QuantumRpcTool>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var capability in catalog.GetProperty("capabilities").EnumerateArray())
        {
            var rpcName = capability.GetProperty("name").GetString() ?? throw new DomainException("codex_invalid_state", "The RPC catalog contains a capability without a name.");
            // Keep control RPCs discoverable to external callers, but do not let
            // a Codex turn recursively send to or disconnect its own session.
            if (rpcName == QuantumCapabilityCatalog.CapabilityName || rpcName.StartsWith("codex.", StringComparison.Ordinal) || capability.GetProperty("kind").GetString() != "request" || capability.GetProperty("providerPluginIds").EnumerateArray().All(id => id.GetString() == "quantum.plugin.codex"))
            {
                continue;
            }

            if (rpcName == QuantumPluginInstallationExtensions.InstallLocalCapabilityName)
            {
                tools.Add(new QuantumRpcTool { ToolName = "build_and_install_web_plugin", RpcName = rpcName, Description = "When the user requests a custom Quantum plugin, write its source and call this tool. " + "It packages a self-contained JavaScript ES module into a Quantum Web plugin ZIP and requests user confirmation. " + "Export a default object with mount({ element, route, locale, signal, request }); render into element and return cleanup as needed. " + "Use standard browser APIs, no imports or external scripts, and safe DOM APIs for user content. " + "If accepted, Quantum reports the final status in the conversation, possibly after reload. Pending is not installed.", InputSchema = JsonSerializer.SerializeToElement(new { type = "object", properties = new { pluginId = new { type = "string", description = "Unique plugin ID, for example local.unit-converter." }, version = new { type = "string", description = "Semantic version, for example 1.0.0. Increase it for updates." }, title = new { type = "string", description = "Visible navigation title." }, source = new { type = "string", description = "Complete, self-contained JavaScript ES module source." } }, required = new[] { "pluginId", "version", "title", "source" }, additionalProperties = false }) });
                continue;
            }

            var toolName = CreateUniqueToolName(rpcName, names);
            var description = capability.TryGetProperty("description", out var descriptionElement) ? descriptionElement.GetString() : null;
            tools.Add(new QuantumRpcTool { ToolName = toolName, RpcName = rpcName, Description = BuildDescription(rpcName, description), InputSchema = NormalizeInputSchema(capability.GetProperty("requestSchema")) });
        }

        return tools.OrderBy(static tool => tool.RpcName, StringComparer.Ordinal).ToArray();
    }

    public static JsonArray ToDynamicTools(IReadOnlyList<QuantumRpcTool> tools) => tools.Count == 0 ? [] : new(new JsonObject { ["type"] = "namespace", ["name"] = Namespace, ["description"] = "Invoke capabilities exposed by the local Quantum desktop host.", ["tools"] = new JsonArray(tools.Select(static tool => (JsonNode)new JsonObject { ["type"] = "function", ["name"] = tool.ToolName, ["description"] = tool.Description, ["inputSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText()) }).ToArray()) });
    private static JsonElement NormalizeInputSchema(JsonElement schema)
    {
        var node = JsonNode.Parse(schema.GetRawText())?.AsObject() ?? new JsonObject();
        node.Remove("dotnetType");
        node.Remove("x-dotnetAttributes");
        RemoveQuantumExtensions(node);
        return JsonSerializer.SerializeToElement(node);
    }

    private static void RemoveQuantumExtensions(JsonNode? node)
    {
        if (node is JsonObject jsonObject)
        {
            if (jsonObject.Remove("nullable", out var nullable) && nullable is JsonValue nullableValue && nullableValue.TryGetValue<bool>(out var isNullable) && isNullable && jsonObject["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var type))
            {
                jsonObject["type"] = new JsonArray(type, "null");
            }

            jsonObject.Remove("dotnetType");
            jsonObject.Remove("x-dotnetAttributes");
            jsonObject.Remove("recursive");
            foreach (var child in jsonObject.ToArray())
            {
                RemoveQuantumExtensions(child.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                RemoveQuantumExtensions(child);
            }
        }
    }

    private static string BuildDescription(string rpcName, string? description)
    {
        var value = string.IsNullOrWhiteSpace(description) ? $"Invoke the Quantum RPC '{rpcName}'." : $"{description.Trim()} Quantum RPC: {rpcName}.";
        return value.Length <= 1024 ? value : value[..1024];
    }

    private static string CreateUniqueToolName(string rpcName, HashSet<string> names)
    {
        var builder = new StringBuilder("rpc_");
        foreach (var character in rpcName)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '_');
        }

        var candidate = CollapseUnderscores(builder.ToString()).TrimEnd('_');
        if (candidate.Length > 55)
        {
            candidate = $"{candidate[..46]}_{Hash(rpcName)}";
        }

        if (!names.Add(candidate))
        {
            candidate = $"{candidate[..Math.Min(candidate.Length, 46)]}_{Hash(rpcName)}";
            names.Add(candidate);
        }

        return candidate;
    }

    private static string CollapseUnderscores(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasUnderscore = false;
        foreach (var character in value)
        {
            if (character == '_')
            {
                if (!previousWasUnderscore)
                {
                    builder.Append(character);
                }

                previousWasUnderscore = true;
            }
            else
            {
                builder.Append(character);
                previousWasUnderscore = false;
            }
        }

        return builder.ToString();
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();
}
