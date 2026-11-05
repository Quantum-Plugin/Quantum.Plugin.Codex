using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;
using System.Globalization;

namespace Quantum.OfficialPlugins.Codex.UI.Pages;

public partial class CodexMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UsePipeTables().UseTaskLists().UseAutoLinks().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough).UseSoftlineBreakAsHardlineBreak().Build();
    [Parameter]
    public string Text { get; set; } = string.Empty;

    private string? _renderedText;
    private string _html = string.Empty;
    protected override void OnParametersSet()
    {
        if (_renderedText != Text)
        {
            _html = RenderMarkdown(Text);
            _renderedText = Text;
        }
    }

    internal static string RenderMarkdown(string text)
    {
        var document = Markdown.Parse(text, Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            // Show image references as links instead of fetching model-supplied URLs automatically.
            link.IsImage = false;
            link.GetAttributes().AddProperty("target", "_blank");
            link.GetAttributes().AddProperty("rel", "noopener noreferrer");
        }

        foreach (var link in document.Descendants<AutolinkInline>())
        {
            link.GetAttributes().AddProperty("target", "_blank");
            link.GetAttributes().AddProperty("rel", "noopener noreferrer");
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer)
        {
            LinkRewriter = SafeLink
        };
        Pipeline.Setup(renderer);
        renderer.Render(document);
        return writer.ToString();
    }

    private static string SafeLink(string url)
    {
        if (url.Any(char.IsControl) || url.Contains('\\'))
        {
            return "#";
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Scheme is "http" or "https" or "mailto" ? url : "#";
        }

        return url.StartsWith("//", StringComparison.Ordinal) || url.Contains(':') ? "#" : url;
    }
}
