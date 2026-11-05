using NOF.Domain;
using Quantum.Plugin.Abstraction;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Quantum.OfficialPlugins.Codex.Application;

internal static class CodexWebPluginPackage
{
    public static QuantumInstallLocalPluginRequest Create(JsonElement arguments)
    {
        var id = PluginId.Of(ReadString(arguments, "pluginId", PluginId.MaximumLength)).ToString();
        var version = SemanticVersion.Of(ReadString(arguments, "version", 128)).ToString();
        var title = ReadString(arguments, "title", 100);
        var source = ReadString(arguments, "source", 512 * 1024);
        if (title.Any(char.IsControl))
        {
            throw new DomainException("codex_invalid_request", "The plugin title cannot contain control characters.");
        }

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "plugin.json", JsonSerializer.Serialize(new { id, version, runtime = new { kind = "web", entry = "dist/plugin.js" }, ui = new { routes = new[] { new { path = $"/plugins/{id}", view = "main", title, showInNavigation = true } } } }));
            Write(zip, "wwwroot/dist/plugin.js", source);
        }

        return new QuantumInstallLocalPluginRequest($"{id}.zip", stream.ToArray());
    }

    private static string ReadString(JsonElement arguments, string name, int maxLength)
    {
        if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && property.GetString() is { } value && !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength)
        {
            return value;
        }

        throw new DomainException("codex_invalid_request", $"'{name}' must be a nonempty string of at most {maxLength} characters.");
    }

    private static void Write(ZipArchive zip, string path, string contents)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write(contents);
    }
}
