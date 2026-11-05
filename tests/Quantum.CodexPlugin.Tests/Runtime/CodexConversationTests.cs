using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NOF.Contract;
using Quantum.OfficialPlugins.Codex;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.Plugin.Abstraction;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

#pragma warning disable BL0006 // Exercise generated Razor event handlers through the Blazor test renderer.

namespace Quantum.CodexPlugin.Tests.Runtime;

public sealed class CodexConversationTests
{
    private static CodexRuntime Service(FakeClient client) => new(new CatalogInvoker(), NullLoggerFactory.Instance, NullLogger<CodexRuntime>.Instance, clientFactory: () => client);
    [Fact]
    public async Task EmptyModelSelectsTheFirstModelReturnedByTheLocalInstallation()
    {
        await using var runtime = Service(new FakeClient());
        await runtime.ConnectAsync(Environment.CurrentDirectory, string.Empty);
        Assert.Equal(CodexConnectionState.Connected, runtime.Snapshot.ConnectionState);
        Assert.Equal(CodexTestModels.DefaultId, runtime.Snapshot.SelectedModel);
    }

    [Fact]
    public async Task WebPluginToolPackagesSourceAndReportsHostResultAfterReload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quantum-codex-{Guid.NewGuid():N}.json");
        var invoker = new CatalogInvoker
        {
            EnableInstallation = true
        };
        CodexRuntime Runtime(FakeClient client) => new(invoker, NullLoggerFactory.Instance, NullLogger<CodexRuntime>.Instance, clientFactory: () => client, sessionStatePath: path);
        var arguments = JsonSerializer.SerializeToElement(new { pluginId = "local.unit-converter", version = "1.0.0", title = "Unit converter", source = "export default { mount({ element }) { element.textContent = 'Converter'; } };" });
        try
        {
            var first = new FakeClient();
            await using (var runtime = Runtime(first))
            {
                await runtime.ConnectAsync(Environment.CurrentDirectory, CodexTestModels.DefaultId);
                await runtime.SendAsync("Write and install a unit converter plugin.");
                var tools = first.DynamicTools!.ToJsonString();
                Assert.Contains("build_and_install_web_plugin", tools);
                Assert.DoesNotContain("archiveBase64", tools);
                var declined = await first.InstallWebPluginAsync(arguments);
                Assert.False(declined.Success);
                Assert.Contains("plugin_install_declined", declined.Text);
                invoker.ApproveInstallation = true;
                var accepted = await first.InstallWebPluginAsync(arguments);
                Assert.True(accepted.Success);
                Assert.Contains("pending", accepted.Text);
                Assert.DoesNotContain(runtime.Snapshot.Messages, message => message.Text.StartsWith("Installed ", StringComparison.Ordinal));
            }

            var package = Assert.IsType<QuantumInstallLocalPluginRequest>(invoker.Package);
            Assert.Equal("local.unit-converter.zip", package.ArchiveFileName);
            using var zip = new ZipArchive(new MemoryStream(package.ArchiveBase64));
            using var manifest = JsonDocument.Parse(zip.GetEntry("plugin.json")!.Open());
            Assert.Equal("local.unit-converter", manifest.RootElement.GetProperty("id").GetString());
            Assert.Equal("dist/plugin.js", manifest.RootElement.GetProperty("runtime").GetProperty("entry").GetString());
            using var source = new StreamReader(zip.GetEntry("wwwroot/dist/plugin.js")!.Open());
            Assert.Equal(arguments.GetProperty("source").GetString(), await source.ReadToEndAsync());
            invoker.InstallationFinished = true;
            await using var reloaded = Runtime(new FakeClient());
            var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            reloaded.Changed += () =>
            {
                if (reloaded.Snapshot.Messages.Any(message => message.Text == "Installed local.unit-converter. Host installed the plugin."))
                {
                    reported.TrySetResult();
                }
            };
            await reloaded.ConnectAsync(Environment.CurrentDirectory, CodexTestModels.DefaultId);
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReconnectResumesTheSameThreadAndKeepsItsDirectoryAndHistory()
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await service.ConnectAsync(Environment.CurrentDirectory, CodexTestModels.DefaultId);
        await service.SendAsync("Remember me");
        await client.CompleteTurn();
        var before = service.Snapshot;
        await service.DisconnectAsync();
        await service.ConnectAsync(Path.GetTempPath(), CodexTestModels.DefaultId);
        Assert.Equal(before.ThreadId, service.Snapshot.ThreadId);
        Assert.Equal(before.WorkingDirectory, service.Snapshot.WorkingDirectory);
        Assert.Contains(service.Snapshot.Messages, message => message.Text == "Remember me");
        Assert.Equal(1, client.ThreadStarts);
        Assert.Equal((before.ThreadId, before.WorkingDirectory), Assert.Single(client.Resumes));
    }

    [Fact]
    public async Task RestartRestoresTheSavedThreadModelDirectoryAndMessages()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quantum-codex-{Guid.NewGuid():N}.json");
        try
        {
            var first = new FakeClient();
            string? threadId;
            await using (var service = new CodexRuntime(new CatalogInvoker(), NullLoggerFactory.Instance, NullLogger<CodexRuntime>.Instance, clientFactory: () => first, sessionStatePath: path))
            {
                await service.ConnectAsync(Environment.CurrentDirectory, CodexTestModels.DefaultId);
                await service.ChangeModelAsync("example-fast");
                await service.SendAsync("Persist this message");
                await first.CompleteTurn();
                threadId = service.Snapshot.ThreadId;
            }

            var second = new FakeClient();
            await using var restarted = new CodexRuntime(new CatalogInvoker(), NullLoggerFactory.Instance, NullLogger<CodexRuntime>.Instance, clientFactory: () => second, sessionStatePath: path);
            await restarted.ConnectAsync(Path.GetTempPath(), CodexTestModels.DefaultId);
            Assert.Equal(threadId, restarted.Snapshot.ThreadId);
            Assert.Equal("example-fast", restarted.Snapshot.SelectedModel);
            Assert.Equal(Path.GetFullPath(Environment.CurrentDirectory), restarted.Snapshot.WorkingDirectory);
            Assert.Contains(restarted.Snapshot.Messages, message => message.Text == "Persist this message");
            Assert.Equal(0, second.ThreadStarts);
            Assert.Single(second.Resumes);
            await restarted.StartNewThreadAsync();
            Assert.NotEqual(threadId, restarted.Snapshot.ThreadId);
            Assert.Null(restarted.Snapshot.ThreadId);
            Assert.Equal(0, second.ThreadStarts);
            await restarted.SendAsync("New conversation message");
            Assert.Equal(1, second.ThreadStarts);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FailedRecoveryNeverSilentlyCreatesAnotherThread()
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await service.ConnectAsync(Environment.CurrentDirectory, CodexTestModels.DefaultId);
        await service.SendAsync("A persisted conversation");
        await client.CompleteTurn();
        var threadId = service.Snapshot.ThreadId;
        await service.DisconnectAsync();
        client.FailResume = true;
        await service.ConnectAsync(Path.GetTempPath(), CodexTestModels.DefaultId);
        await service.ConnectAsync(Path.GetTempPath(), CodexTestModels.DefaultId);
        Assert.Equal(CodexConnectionState.Failed, service.Snapshot.ConnectionState);
        Assert.Equal(threadId, service.Snapshot.ThreadId);
        Assert.Equal(1, client.ThreadStarts);
        Assert.Equal(2, client.Resumes.Count);
        await service.StartNewThreadAsync();
        Assert.Equal(CodexConnectionState.Connected, service.Snapshot.ConnectionState);
        Assert.NotEqual(threadId, service.Snapshot.ThreadId);
        Assert.Null(service.Snapshot.ThreadId);
        Assert.Equal(1, client.ThreadStarts);
        await service.SendAsync("Explicit replacement");
        Assert.Equal(2, client.ThreadStarts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyConversationRestartsWithoutResumingOrCreatingAnExternalThread(bool legacy)
    {
        var path = Path.Combine(Path.GetTempPath(), $"quantum-codex-{Guid.NewGuid():N}.json");
        try
        {
            var directory = Path.GetFullPath(Environment.CurrentDirectory);
            if (legacy)
            {
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new CodexSessionState { ThreadId = "unused-thread-with-no-rollout", WorkingDirectory = directory, Model = "example-fast" }));
            }

            for (var restart = 0; restart < 3; restart++)
            {
                var client = new FakeClient
                {
                    FailResume = true
                };
                await using var service = new CodexRuntime(new CatalogInvoker(), NullLoggerFactory.Instance, NullLogger<CodexRuntime>.Instance, clientFactory: () => client, sessionStatePath: path);
                await service.ConnectAsync(restart == 0 ? directory : Path.GetTempPath(), CodexTestModels.DefaultId);
                Assert.Equal(CodexConnectionState.Connected, service.Snapshot.ConnectionState);
                Assert.Null(service.Snapshot.ErrorMessage);
                Assert.Null(service.Snapshot.ThreadId);
                Assert.Equal(directory, service.Snapshot.WorkingDirectory);
                if (restart == 0 && !legacy)
                {
                    await service.ChangeModelAsync("example-fast");
                }

                Assert.Equal("example-fast", service.Snapshot.SelectedModel);
                await service.StartNewThreadAsync();
                Assert.Equal(0, client.ThreadStarts);
                Assert.Empty(client.Resumes);
                Assert.Empty(client.Turns);
                if (restart == 2)
                {
                    await service.SendAsync("First actual message");
                    await client.CompleteTurn();
                    var id = service.Snapshot.ThreadId;
                    Assert.NotNull(id);
                    await service.SendAsync("Continue the same conversation");
                    Assert.Equal(1, client.ThreadStarts);
                    Assert.All(client.Turns, turn => Assert.Equal(id, turn.ThreadId));
                }
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ConcurrentFirstSendCannotCreateDuplicateThreadsAndCancellationAllowsRetry()
    {
        var client = new FakeClient
        {
            ThreadCreationEntered = new(TaskCreationOptions.RunContinuationsAsynchronously),
            ThreadCreationRelease = new(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        await using var service = Service(client);
        await service.ConnectAsync(Environment.CurrentDirectory, CodexTestModels.DefaultId);
        using var cancellation = new CancellationTokenSource();
        var first = service.SendAsync("First message", cancellation.Token);
        await client.ThreadCreationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<NOF.Domain.DomainException>(() => service.SendAsync("Concurrent message"));
        Assert.Equal(1, client.ThreadStarts);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(service.Snapshot.IsBusy);
        Assert.Null(service.Snapshot.ThreadId);
        Assert.Empty(client.Turns);
        client.ThreadCreationRelease.SetResult();
        await service.SendAsync("Retry first message");
        await client.CompleteTurn();
        await service.SendAsync("Continue");
        Assert.Equal(2, client.ThreadStarts);
        Assert.Equal(2, client.Turns.Count);
        Assert.All(client.Turns, turn => Assert.Equal(service.Snapshot.ThreadId, turn.ThreadId));
    }

    [Fact]
    public void ResumeWireParametersKeepTheThreadDirectoryAndToolMetadata()
    {
        var payload = JsonSerializer.SerializeToElement(CodexAppServerClient.CreateResumeParameters("existing", "C:/fixed", "model"));
        Assert.Equal("existing", payload.GetProperty("threadId").GetString());
        Assert.Equal("C:/fixed", payload.GetProperty("cwd").GetString());
        Assert.Equal("never", payload.GetProperty("approvalPolicy").GetString());
        Assert.Equal("read-only", payload.GetProperty("sandbox").GetString());
        Assert.False(payload.TryGetProperty("dynamicTools", out _));
    }

    [Fact]
    public async Task ModelChangesPreserveThreadHistoryAndToolsAndApplyToEverySubsequentTurn()
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await service.ConnectAsync(Environment.CurrentDirectory, CodexTestModels.DefaultId);
        await service.SendAsync("Remember this conversation");
        await client.CompleteTurn();
        var before = service.Snapshot;
        Assert.Equal(1, before.ToolCount);
        await service.ChangeModelAsync("example-fast");
        await service.ChangeModelAsync("example-fast");
        Assert.Equal(before.ThreadId, service.Snapshot.ThreadId);
        Assert.Equal(before.Messages, service.Snapshot.Messages);
        Assert.Equal(before.ToolCount, service.Snapshot.ToolCount);
        Assert.Equal(1, client.ThreadStarts);
        Assert.Single(client.Turns);
        await service.SendAsync("Continue");
        await client.CompleteTurn();
        await service.SendAsync("Continue again");
        Assert.All(client.Turns, turn => Assert.Equal(before.ThreadId, turn.ThreadId));
        Assert.Equal(new[] { CodexTestModels.DefaultId, "example-fast", "example-fast" }, client.Turns.Select(turn => turn.Model));
        Assert.Contains(service.Snapshot.Messages, message => message.Text == "Remember this conversation");
    }

    [Fact]
    public async Task OnlyExplicitNewConversationReplacesTheThreadAndClearsHistory()
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await service.ConnectAsync(Environment.CurrentDirectory, CodexTestModels.DefaultId);
        await service.SendAsync("Old message");
        await client.CompleteTurn();
        await service.ChangeModelAsync("example-fast");
        var oldId = service.Snapshot.ThreadId;
        await service.StartNewThreadAsync();
        Assert.NotEqual(oldId, service.Snapshot.ThreadId);
        Assert.Equal("example-fast", service.Snapshot.SelectedModel);
        Assert.DoesNotContain(service.Snapshot.Messages, message => message.Role == CodexMessageRole.User);
        Assert.Null(service.Snapshot.ThreadId);
        Assert.Equal(1, client.ThreadStarts);
        await service.SendAsync("New message");
        Assert.Equal(2, client.ThreadStarts);
    }

    [Fact]
    public async Task BusyInvalidCancelledAndDisconnectedSelectionsLeaveConversationUntouched()
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await Assert.ThrowsAsync<NOF.Domain.DomainException>(() => service.ChangeModelAsync("example-fast"));
        await service.ConnectAsync(Environment.CurrentDirectory, CodexTestModels.DefaultId);
        var before = service.Snapshot;
        await Assert.ThrowsAsync<NOF.Domain.DomainException>(() => service.ChangeModelAsync("unknown-model"));
        await Assert.ThrowsAsync<NOF.Domain.DomainException>(() => service.ChangeModelAsync(" "));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ChangeModelAsync("example-fast", cancelled.Token));
        Assert.Equal(before.SelectedModel, service.Snapshot.SelectedModel);
        Assert.Equal(before.Messages, service.Snapshot.Messages);
        await service.SendAsync("Active turn");
        await Assert.ThrowsAsync<NOF.Domain.DomainException>(() => service.ChangeModelAsync("example-fast"));
        await Assert.ThrowsAsync<NOF.Domain.DomainException>(() => service.StartNewThreadAsync());
        Assert.Equal(before.SelectedModel, service.Snapshot.SelectedModel);
        Assert.NotNull(service.Snapshot.ThreadId);
        Assert.Equal(1, client.ThreadStarts);
    }

    [Fact]
    public void TurnWireParametersCarryTheExistingThreadAndSelectedModel()
    {
        var payload = JsonSerializer.SerializeToElement(CodexAppServerClient.CreateTurnParameters("thread-existing", "Continue", "example-fast"));
        Assert.Equal("thread-existing", payload.GetProperty("threadId").GetString());
        Assert.Equal("example-fast", payload.GetProperty("model").GetString());
        Assert.Equal("Continue", payload.GetProperty("input")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task VoiceUsesTheSpeechCapabilityAndKeepsTheExistingConversation()
    {
        var main = new FakeClient();
        var transcription = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoker = new CatalogInvoker(transcription.Task);
        await using var service = new CodexRuntime(invoker, NullLoggerFactory.Instance, NullLogger<CodexRuntime>.Instance, clientFactory: () => main);
        await service.ConnectAsync(Path.GetTempPath(), CodexTestModels.DefaultId);
        Assert.Null(service.Snapshot.ThreadId);
        var sending = service.SendAsync(new CodexSendRequest { Prompt = "", Attachments = [new MobileAttachment { Name = "voice.wav", ContentType = "audio/wav", DataBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3 }) }] });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!service.Snapshot.IsBusy)
        {
            await Task.Delay(10, timeout.Token);
        }

        Assert.True(service.Snapshot.Messages.Last().IsTranscribing);
        Assert.Empty(main.Turns);
        Assert.Equal(0, main.ThreadStarts);
        transcription.SetResult("Recognized speech");
        await sending.WaitAsync(timeout.Token);
        Assert.NotNull(service.Snapshot.ThreadId);
        Assert.Equal(service.Snapshot.ThreadId, main.Turns.Single().ThreadId);
        Assert.Equal("Recognized speech", service.Snapshot.Messages.Last().Text);
        Assert.Equal("voice.wav", service.Snapshot.Messages.Last().Attachments.Single().Name);
        Assert.False(service.Snapshot.Messages.Last().IsTranscribing);
        Assert.Equal(1, main.ThreadStarts);
        Assert.Equal(1, invoker.SpeechCalls);
    }

    [Fact]
    public void MediaTurnUsesImageAndFileInputsWithoutChangingTheConversation()
    {
        var files = new[]
        {
            new CodexAttachmentFile
            {
                Name = "image.png",
                ContentType = "image/png",
                Path = "/selected/image.png",
                Size = 1
            },
            new CodexAttachmentFile
            {
                Name = "document.pdf",
                ContentType = "application/pdf",
                Path = "/selected/document.pdf",
                Size = 1
            }
        };
        var payload = JsonSerializer.SerializeToElement(CodexAppServerClient.CreateMediaTurnParameters("existing", "Compare", "model", files));
        Assert.Equal("existing", payload.GetProperty("threadId").GetString());
        Assert.Equal("localImage", payload.GetProperty("input")[1].GetProperty("type").GetString());
        Assert.Equal("mention", payload.GetProperty("input")[2].GetProperty("type").GetString());
        Assert.Equal("/selected/document.pdf", payload.GetProperty("input")[2].GetProperty("path").GetString());
    }

    [Theory]
    [InlineData("../outside.txt", "AQ==")]
    [InlineData("file.txt", "not base64")]
    public async Task AttachmentValidationFailsBeforeAnyModelTurn(string name, string encoded)
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await service.ConnectAsync(Path.GetTempPath(), CodexTestModels.DefaultId);
        await Assert.ThrowsAsync<NOF.Domain.DomainException>(() => service.SendAsync(new CodexSendRequest { Prompt = "", Attachments = [new MobileAttachment { Name = name, ContentType = "text/plain", DataBase64 = encoded }] }));
        Assert.Empty(client.Turns);
        Assert.Equal(0, client.ThreadStarts);
        Assert.False(service.Snapshot.IsBusy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypingEnablesSendImmediatelyAndClearingTheDraftDisablesIt(bool drawer)
    {
        var client = new FakeClient();
        await using var service = Service(client);
        await service.ConnectAsync(Environment.CurrentDirectory, CodexTestModels.DefaultId);
        var builder = Microsoft.Extensions.Hosting.Host.CreateEmptyApplicationBuilder(null);
        NOF.Hosting.NOFInfrastructureExtensions.AddNOFInfrastructure(builder);
        NOF.Hosting.NOFHostingExtensions.AddApplicationPart(builder, typeof(OfficialPlugins.Codex.CodexPlugin).Assembly);
        builder.Services.AddScoped<IRequestOutboundPipelineExecutor, NOF.Hosting.RequestOutboundPipelineExecutor>();
        builder.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        builder.Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        builder.Services.AddLocalization();
        OfficialPlugins.Codex.CodexPlugin.ConfigureServices(builder.Services);
        builder.Services.AddSingleton<ICodexRuntime>(service);
        builder.Services.AddSingleton<IComponentActivator, ComponentActivator>();
        builder.Services.AddSingleton<IJSRuntime, NoopJSRuntime>();
        builder.Services.AddSingleton<NavigationManager, TestNavigationManager>();
        await using var provider = builder.Services.BuildServiceProvider();
        await using var renderer = new ComposerRenderer(provider);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var id = await renderer.MountAsync(drawer);
            Assert.True(renderer.SendDisabled(id));
            await renderer.InputAsync(id, "正在输入");
            Assert.False(renderer.SendDisabled(id));
            await renderer.InputAsync(id, "");
            Assert.True(renderer.SendDisabled(id));
            await renderer.InputAsync(id, "Send the current draft");
            await renderer.ClickSendAsync(id);
            Assert.Equal("Send the current draft", Assert.Single(client.Turns).Prompt);
            Assert.True(renderer.SendDisabled(id));
        });
    }

    // Dispatch the actual Razor oninput event without a browser or model request.
    private sealed class ComposerRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Component rendering failed.", exception);
        public async Task<int> MountAsync(bool drawer)
        {
            var id = AssignRootComponentId(InstantiateComponent(drawer ? typeof(OfficialPlugins.Codex.UI.Pages.CodexDrawer) : typeof(OfficialPlugins.Codex.UI.Pages.Codex)));
            await RenderRootComponentAsync(id);
            if (drawer)
            {
                var open = Frames(id).First(frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "onclick");
                await DispatchEventAsync(open.AttributeEventHandlerId, null, new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
            }

            return id;
        }

        private RenderTreeFrame[] Frames(int id)
        {
            var frames = GetCurrentRenderTreeFrames(id);
            return frames.Array.Take(frames.Count).ToArray();
        }

        public Task InputAsync(int id, string value)
        {
            var input = Assert.Single(Frames(id), frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "oninput");
            return DispatchEventAsync(input.AttributeEventHandlerId, null, new ChangeEventArgs { Value = value });
        }

        public bool SendDisabled(int id)
        {
            var frames = Frames(id);
            var index = Array.FindIndex(frames, frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "class" && Equals(frame.AttributeValue, "send-button"));
            Assert.True(index >= 0);
            return frames.Skip(index + 1).TakeWhile(frame => frame.FrameType == RenderTreeFrameType.Attribute).Any(frame => frame.AttributeName == "disabled" && Equals(frame.AttributeValue, true));
        }

        public Task ClickSendAsync(int id)
        {
            var frames = Frames(id);
            var index = Array.FindIndex(frames, frame => frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "class" && Equals(frame.AttributeValue, "send-button"));
            var click = Assert.Single(frames.Skip(index + 1).TakeWhile(frame => frame.FrameType == RenderTreeFrameType.Attribute), frame => frame.AttributeName == "onclick");
            return DispatchEventAsync(click.AttributeEventHandlerId, null, new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        }
    }

    private sealed class ComponentActivator(IServiceProvider services) : IComponentActivator
    {
        public IComponent CreateInstance(Type type) => (IComponent)ActivatorUtilities.CreateInstance(services, type);
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }

    private sealed class NoopJSRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
    }

    private sealed class CatalogInvoker(Task<string>? transcript = null) : IQuantumRequester
    {
        public bool EnableInstallation { get; init; }
        public bool ApproveInstallation { get; set; }
        public bool InstallationFinished { get; set; }
        public QuantumInstallLocalPluginRequest? Package { get; private set; }
        public int SpeechCalls { get; private set; }

        public async Task<Result<QuantumRequestReport<T>>> RequestAsync<T>(string name, object payload, Context context, CancellationToken cancellationToken = default)
        {
            var result = await ReplyAsync<T>(name, payload, cancellationToken);
            return result.IsSuccess ? Result.Success(new QuantumRequestReport<T>([new("test.provider", QuantumRequestStatus.Handled, result.Value, null, null) { HasValue = true }], false)) : Result<QuantumRequestReport<T>>.From(result);
        }

        private async Task<Result<T>> ReplyAsync<T>(string rpcName, object payload, CancellationToken cancellationToken)
        {
            if (rpcName == "speech.transcribe")
            {
                SpeechCalls++;
                return (T)(object)await (transcript ?? Task.FromResult("Recognized speech")).WaitAsync(cancellationToken);
            }

            if (EnableInstallation)
            {
                if (rpcName == QuantumPluginInstallationExtensions.InstallLocalCapabilityName)
                {
                    Package = Assert.IsType<QuantumInstallLocalPluginRequest>(payload);
                    return ApproveInstallation ? (T)(object)new QuantumPluginInstallReceipt("operation", "pending", null) : Result<T>.From(Result.Fail("plugin_install_declined", "Installation declined."));
                }

                if (rpcName == QuantumPluginInstallationExtensions.GetPluginInstallStatusCapabilityName)
                {
                    Assert.Equal("operation", Assert.IsType<QuantumPluginInstallStatusRequest>(payload).OperationId);
                    return (T)(object)(InstallationFinished ? new QuantumPluginInstallReceipt("operation", "succeeded", "Host installed the plugin.") : new QuantumPluginInstallReceipt("operation", "pending", null));
                }

                return (T)(object)JsonSerializer.SerializeToElement(new { schemaVersion = 2, capabilities = new[] { new { kind = "request", providerPluginIds = new[] { "quantum.host" }, name = QuantumPluginInstallationExtensions.InstallLocalCapabilityName, requestSchema = new { type = "object" } } } });
            }

            return (T)(object)JsonSerializer.SerializeToElement(new { schemaVersion = 2, capabilities = new[] { new { kind = "request", providerPluginIds = new[] { "quantum.plugin.calendar" }, name = "calendar.list", description = "Read calendar", requestSchema = new { type = "object" } } } });
        }
    }

    private sealed class FakeClient : ICodexAppServerClient
    {
        public event Func<string, JsonElement, Task>? NotificationReceived;
        public event Action<Exception?>? ConnectionClosed
        {
            add
            {
            }

            remove
            {
            }
        }

        public event Func<CodexDynamicToolCall, CancellationToken, Task<CodexDynamicToolResult>>? DynamicToolCallReceived;
        public JsonArray? DynamicTools { get; private set; }

        public Task<CodexDynamicToolResult> InstallWebPluginAsync(JsonElement arguments) => DynamicToolCallReceived!(new CodexDynamicToolCall { CallId = Guid.NewGuid().ToString("N"), Namespace = "quantum", Tool = "build_and_install_web_plugin", Arguments = arguments }, CancellationToken.None);
        public TaskCompletionSource<string>? Transcript { get; init; }

        public Task<JsonElement> StartMediaTurnAsync(string threadId, string prompt, string model, CodexAttachmentFile[] files, CancellationToken token) => StartTurnAsync(threadId, prompt, model, token);
        public int ThreadStarts { get; private set; }
        public List<(string ThreadId, string Model, string Prompt)> Turns { get; } = [];
        public List<(string ThreadId, string Directory)> Resumes { get; } = [];
        public bool FailResume { get; set; }

        public Task ResumeThreadAsync(string threadId, string workingDirectory, string model, CancellationToken cancellationToken)
        {
            Resumes.Add((threadId, workingDirectory));
            return FailResume ? Task.FromException(new InvalidOperationException("Resume failed")) : Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<CodexModelOption>> ListModelsAsync(CancellationToken cancellationToken) => Task.FromResult(CodexTestModels.All);
        public TaskCompletionSource? ThreadCreationEntered { get; init; }
        public TaskCompletionSource? ThreadCreationRelease { get; init; }

        public async Task<string> StartThreadAsync(string workingDirectory, string model, JsonArray dynamicTools, CancellationToken cancellationToken)
        {
            ThreadStarts++;
            DynamicTools = dynamicTools;
            ThreadCreationEntered?.TrySetResult();
            if (ThreadCreationRelease is not null)
            {
                await ThreadCreationRelease.Task.WaitAsync(cancellationToken);
            }

            return $"thread-{Guid.NewGuid():N}";
        }

        public Task<JsonElement> StartTurnAsync(string threadId, string prompt, string model, CancellationToken cancellationToken)
        {
            Turns.Add((threadId, model, prompt));
            return Task.FromResult(JsonSerializer.SerializeToElement(new { }));
        }

        public Task CompleteTurn() => NotificationReceived!("turn/completed", JsonSerializer.SerializeToElement(new { turn = new { status = "completed" } }));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
