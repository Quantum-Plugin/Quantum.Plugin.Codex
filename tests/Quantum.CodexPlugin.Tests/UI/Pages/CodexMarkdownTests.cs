using Quantum.OfficialPlugins.Codex.UI.Pages;
using Xunit;

namespace Quantum.CodexPlugin.Tests.UI.Pages;

public sealed class CodexMarkdownTests
{
    [Fact]
    public void FormatsCommonReplyElementsAndEscapesCode()
    {
        var html = CodexMarkdown.RenderMarkdown("""
            ## 标题

            **重点**，*强调*，~~删除~~，`inline`

            - 第一项
            - 第二项

            > 引用

            ```html
            <script>alert(1)</script>
            ```

            | 列 A | 列 B |
            | --- | --- |
            | 一 | 二 |

            - [x] 完成

            [文档](https://example.com/docs)
            """);
        Assert.Contains("<h2>标题</h2>", html);
        Assert.Contains("<strong>重点</strong>", html);
        Assert.Contains("<em>强调</em>", html);
        Assert.Contains("<del>删除</del>", html);
        Assert.Contains("<code>inline</code>", html);
        Assert.Contains("<ul>", html);
        Assert.Contains("<blockquote>", html);
        Assert.Contains("<pre><code class=\"language-html\">", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("<table>", html);
        Assert.Contains("type=\"checkbox\"", html);
        Assert.Contains("href=\"https://example.com/docs\"", html);
        Assert.Contains("target=\"_blank\"", html);
        Assert.Contains("rel=\"noopener noreferrer\"", html);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("jav&#x61;script:alert(1)")]
    [InlineData("data:text/html,script")]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("vbscript:msgbox(1)")]
    public void UntrustedReplyCannotProduceExecutableLinks(string url)
    {
        var html = CodexMarkdown.RenderMarkdown($"[link]({url})");
        Assert.Contains("href=\"#\"", html);
    }

    [Fact]
    public void HtmlIsLiteralAndImageReferencesDoNotFetchRemoteContent()
    {
        var html = CodexMarkdown.RenderMarkdown("<script>alert(1)</script>\n\n<img src=x onerror=alert(1)>\n\n![image](https://example.com/image.png)");
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("href=\"https://example.com/image.png\"", html);
    }

    [Fact]
    public void IncompleteStreamingCodeFenceRemainsReadableAndCanBeReRendered()
    {
        const string partial = "说明\n\n```csharp\nvar value = 1;";
        var during = CodexMarkdown.RenderMarkdown(partial);
        var completed = CodexMarkdown.RenderMarkdown(partial + "\n```\n\n**完成**");
        Assert.Contains("var value = 1;", during);
        Assert.Contains("<pre><code", during);
        Assert.Contains("<strong>完成</strong>", completed);
        Assert.DoesNotContain("<strong>完成</strong>", during);
    }
}
