using Microsoft.Extensions.Logging;
using NOF.Contract;
using NOF.Domain;
using Quantum.OfficialPlugins.Codex.Application;
using Quantum.Plugin.Abstraction;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Quantum.OfficialPlugins.Codex;

internal sealed class CodexRuntime : ICodexRuntime, IAsyncDisposable
{
    private readonly IQuantumRequester _rpcInvoker;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<CodexRuntime> _logger;
    private readonly TimeSpan? _automaticConnectionTimeout;
    private readonly Func<ICodexAppServerClient>? _clientFactory;
    private readonly string? _sessionStatePath;
    private bool _sessionLoaded;
    private bool _hasSession;
    private static string DataDirectory
    {
        get
        {
            var configuredPath = Environment.GetEnvironmentVariable("QUANTUM_CODEX_DATA_PATH");
            return string.IsNullOrWhiteSpace(configuredPath) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quantum", "Codex") : Path.GetFullPath(configuredPath);
        }
    }

    internal static string DefaultWorkingDirectory => Path.Combine(DataDirectory, "workspace");

    internal const string DefaultModel = "";
    private readonly object _sync = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly List<CodexChatMessage> _messages = [];
    private readonly Dictionary<string, string> _pendingPluginInstalls = new(StringComparer.Ordinal);
    private Task? _installationMonitor;
    private TaskCompletionSource? _installationRequest;
    private readonly ConcurrentDictionary<string, byte> _activeToolCalls = new(StringComparer.Ordinal);
    private IReadOnlyList<QuantumRpcTool> _tools = [];
    private IReadOnlyList<CodexModelOption> _models = [];
    private string _selectedModel = DefaultModel;
    private ICodexAppServerClient? _client;
    private CodexConnectionState _connectionState;
    private string? _threadId;
    private bool _isBusy;
    private CancellationTokenSource? _sendingCancellation;
    private string _workingDirectory = DefaultWorkingDirectory;
    private string? _errorMessage;
    private bool _disposed;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _automaticConnection;
    private Task? _automaticConnectionTask;
    private bool _automaticConnectionSuppressed;
    public CodexRuntime(IQuantumRequester rpcInvoker, ILoggerFactory loggerFactory, ILogger<CodexRuntime> logger, TimeSpan? automaticConnectionTimeout = null, Func<ICodexAppServerClient>? clientFactory = null, string? sessionStatePath = null)
    {
        _rpcInvoker = rpcInvoker;
        _loggerFactory = loggerFactory;
        _logger = logger;
        _automaticConnectionTimeout = automaticConnectionTimeout;
        _clientFactory = clientFactory;
        _sessionStatePath = sessionStatePath ?? (clientFactory is null ? Path.Combine(DataDirectory, "session.json") : null);
    }

    public Task EnsureAutoConnectedAsync()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_automaticConnectionTask is not null)
            {
                return _automaticConnectionTask;
            }

            if (_automaticConnectionSuppressed || _connectionState != CodexConnectionState.Disconnected)
            {
                return Task.CompletedTask;
            }

            _automaticConnectionTask = AutoConnectAsync();
            return _automaticConnectionTask;
        }
    }

    private async Task AutoConnectAsync()
    {
        // First interactive render runs after the host publishes the RPC registry.
        // Yield before raising Changed so no UI callbacks execute under _sync.
        await Task.Yield();
        CancellationToken token;
        lock (_sync)
        {
            if (_disposed || _automaticConnectionSuppressed || _connectionState != CodexConnectionState.Disconnected)
            {
                return;
            }

            _automaticConnection = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _automaticConnection.CancelAfter(_automaticConnectionTimeout ?? TimeSpan.FromSeconds(30));
            token = _automaticConnection.Token;
        }

        try
        {
            var snapshot = Snapshot;
            await ConnectAsync(snapshot.WorkingDirectory, snapshot.SelectedModel, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            SetAutomaticFailure("Automatic connection timed out. Check the local Codex installation and sign-in, then retry.");
        }
        catch (Exception exception)
        {
            SetAutomaticFailure(exception.Message);
        }
    }

    private void SetAutomaticFailure(string message)
    {
        lock (_sync)
        {
            if (_disposed || _automaticConnectionSuppressed || _connectionState is CodexConnectionState.Connected or CodexConnectionState.Connecting)
            {
                return;
            }

            _connectionState = CodexConnectionState.Failed;
            _errorMessage = message;
        }

        NotifyChanged();
    }

    public event Action? Changed;
    public CodexIntegrationSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                return new CodexIntegrationSnapshot
                {
                    ConnectionState = _connectionState,
                    ThreadId = _threadId,
                    IsBusy = _isBusy,
                    ToolCount = _tools.Count,
                    WorkingDirectory = _workingDirectory,
                    Models = _models,
                    SelectedModel = _selectedModel,
                    Messages = _messages.ToArray(),
                    ErrorMessage = _errorMessage
                };
            }
        }
    }

    public async Task ConnectAsync(string workingDirectory, string model, CancellationToken cancellationToken = default) => await ConnectCoreAsync(workingDirectory, model, false, cancellationToken).ConfigureAwait(false);
    private async Task ConnectCoreAsync(string workingDirectory, string model, bool newThread, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ICodexAppServerClient? staleClient = null;
            lock (_sync)
            {
                if (_client is not null && _connectionState is CodexConnectionState.Connected or CodexConnectionState.Connecting)
                {
                    return;
                }

                if (_client is not null)
                {
                    staleClient = _client;
                    _client = null;
                }
            }

            if (staleClient is not null)
            {
                staleClient.NotificationReceived -= HandleNotificationAsync;
                staleClient.DynamicToolCallReceived -= HandleDynamicToolCallAsync;
                staleClient.ConnectionClosed -= HandleConnectionClosed;
                await staleClient.DisposeAsync().ConfigureAwait(false);
            }

            SetConnectionState(CodexConnectionState.Connecting, null);
            try
            {
                LoadSession();
            }
            catch (Exception exception) when (newThread && exception is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "Replacing an unreadable saved Codex conversation.");
                _sessionLoaded = true;
            }

            var normalizedDirectory = Path.GetFullPath(_hasSession || string.IsNullOrWhiteSpace(workingDirectory) ? _workingDirectory : workingDirectory.Trim());
            if (normalizedDirectory == DefaultWorkingDirectory)
            {
                Directory.CreateDirectory(normalizedDirectory);
            }

            if (!Directory.Exists(normalizedDirectory))
            {
                throw new DomainException("codex_invalid_request", $"Working directory '{normalizedDirectory}' does not exist.");
            }

            var catalogResult = await _rpcInvoker.RequestSingleAsync<JsonElement>(QuantumCapabilityCatalog.CapabilityName, new { }, Context.Empty, cancellationToken).ConfigureAwait(false);
            if (!catalogResult.IsSuccess)
            {
                throw new DomainException("codex_invalid_state", $"Could not read Quantum RPC catalog: {catalogResult.ErrorCode}: {catalogResult.Message}");
            }

            var tools = QuantumRpcToolCatalog.Parse(catalogResult.Value);
            var selectedModel = _hasSession && !newThread ? _selectedModel : model;
            var client = _clientFactory?.Invoke() ?? new CodexAppServerClient(_loggerFactory.CreateLogger<CodexAppServerClient>());
            client.NotificationReceived += HandleNotificationAsync;
            client.DynamicToolCallReceived += HandleDynamicToolCallAsync;
            client.ConnectionClosed += HandleConnectionClosed;
            try
            {
                await client.StartAsync(cancellationToken).ConfigureAwait(false);
                var availableModels = await client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
                if (availableModels.Count == 0)
                {
                    throw new DomainException("codex_invalid_state", "The local Codex installation returned no available models.");
                }

                selectedModel = ResolveModel(string.IsNullOrWhiteSpace(selectedModel) ? availableModels[0].Id : selectedModel, availableModels);
                var threadId = newThread ? null : _threadId;
                if (threadId is not null)
                {
                    await client.ResumeThreadAsync(threadId, normalizedDirectory, selectedModel, cancellationToken).ConfigureAwait(false);
                }

                lock (_sync)
                {
                    _client = client;
                    _tools = tools;
                    _models = availableModels;
                    _selectedModel = selectedModel;
                    _workingDirectory = normalizedDirectory;
                    _threadId = threadId;
                    _hasSession = true;
                    _connectionState = CodexConnectionState.Connected;
                    _errorMessage = null;
                    if (newThread)
                    {
                        _messages.Clear();
                    }

                    _messages.Add(new CodexChatMessage { Id = Guid.NewGuid(), Role = CodexMessageRole.System, Text = $"Codex connected with {selectedModel}. {tools.Count} Quantum RPC tools are available.", CreatedAt = DateTimeOffset.Now });
                    SaveSession();
                }

                NotifyChanged();
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            SetConnectionState(CodexConnectionState.Disconnected, null);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not connect the Quantum Codex integration.");
            SetConnectionState(CodexConnectionState.Failed, exception.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task StartNewThreadAsync(string? model = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Snapshot.ConnectionState is CodexConnectionState.Failed or CodexConnectionState.Disconnected)
        {
            await ConnectCoreAsync(_workingDirectory, model ?? _selectedModel, true, cancellationToken).ConfigureAwait(false);
            return;
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_client is null || _connectionState != CodexConnectionState.Connected)
                {
                    throw new DomainException("codex_invalid_state", "Codex is not connected.");
                }

                if (_isBusy)
                {
                    throw new DomainException("codex_invalid_state", "Wait for the current Codex turn to finish.");
                }

                _selectedModel = ResolveModel(model ?? _selectedModel, _models);
                _threadId = null;
                _errorMessage = null;
                _messages.Clear();
                SaveSession();
            }

            NotifyChanged();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task ChangeModelAsync(string model, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_connectionState != CodexConnectionState.Connected)
                {
                    throw new DomainException("codex_invalid_state", "Codex is not connected.");
                }

                if (_isBusy)
                {
                    throw new DomainException("codex_invalid_state", "Wait for the current Codex turn to finish.");
                }

                _selectedModel = ResolveModel(model, _models);
                SaveSession();
            }

            NotifyChanged();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task SendAsync(string prompt, CancellationToken cancellationToken = default) => SendAsync(new CodexSendRequest { Prompt = prompt }, cancellationToken);
    public async Task SendAsync(CodexSendRequest request, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Attachments is null || string.IsNullOrWhiteSpace(request.Prompt) && request.Attachments.Length == 0)
        {
            throw new DomainException("codex_invalid_request", "A prompt or attachment is required.");
        }

        lock (_sync)
        {
            if (_client is null || _connectionState != CodexConnectionState.Connected)
            {
                throw new DomainException("codex_invalid_state", "Codex is not connected.");
            }

            if (_isBusy)
            {
                throw new DomainException("codex_invalid_state", "Wait for the current Codex turn to finish.");
            }
        }

        var files = await CodexAttachmentStore.StageAsync(request.Attachments, cancellationToken);
        ICodexAppServerClient client;
        string? threadId;
        string model;
        CodexChatMessage message;
        CancellationToken sendingToken;
        lock (_sync)
        {
            client = _client ?? throw new DomainException("codex_invalid_state", "Codex is not connected.");
            threadId = _threadId;
            if (_connectionState != CodexConnectionState.Connected)
            {
                throw new DomainException("codex_invalid_state", "Codex is not connected.");
            }

            if (_isBusy)
            {
                throw new DomainException("codex_invalid_state", "Wait for the current Codex turn to finish.");
            }

            model = _selectedModel;
            _sendingCancellation?.Dispose();
            _sendingCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            sendingToken = _sendingCancellation.Token;
            _isBusy = true;
            _errorMessage = null;
            message = new()
            {
                Id = Guid.NewGuid(),
                Role = CodexMessageRole.User,
                Text = request.Prompt?.Trim() ?? "",
                CreatedAt = DateTimeOffset.Now,
                IsTranscribing = files.Any(f => f.ContentType.StartsWith("audio/", StringComparison.Ordinal)),
                Attachments = files.Select(f => new CodexAttachmentInfo { Name = f.Name, ContentType = f.ContentType, Size = f.Size }).ToArray()
            };
            _messages.Add(message);
            SaveSession(throwOnFailure: false);
        }

        NotifyChanged();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sendingToken);
        await SendPreparedAsync(client, threadId, model, message, files, request.SpeechLanguage, operation.Token);
    }

    private async Task SendPreparedAsync(ICodexAppServerClient client, string? threadId, string model, CodexChatMessage message, CodexAttachmentFile[] files, string? speechLanguage, CancellationToken cancellationToken)
    {
        try
        {
            var prompt = message.Text;
            foreach (var audio in files.Where(f => f.ContentType.StartsWith("audio/", StringComparison.Ordinal)))
            {
                var result = await _rpcInvoker.RequestSingleAsync<string>("speech.transcribe", new CodexTranscribeRequest { Audio = new MobileAttachment { Name = audio.Name, ContentType = audio.ContentType, DataBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(audio.Path, cancellationToken)) }, Language = speechLanguage }, Context.Empty, cancellationToken);
                if (!result.IsSuccess)
                {
                    throw new DomainException(result.ErrorCode, result.Message);
                }

                var transcript = result.Value;
                if (string.IsNullOrWhiteSpace(transcript))
                {
                    throw new DomainException("codex_speech_empty", "No speech was recognized. Try recording again.");
                }

                prompt = string.IsNullOrEmpty(prompt) ? transcript : prompt + "\n" + transcript;
            }

            await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (_sync)
                {
                    if (_client != client || _threadId != threadId)
                    {
                        return;
                    }

                    message.Text = prompt;
                    message.IsTranscribing = false;
                }

                // Codex does not persist an unused thread. Create it only when a turn is ready.
                threadId ??= await client.StartThreadAsync(_workingDirectory, model, QuantumRpcToolCatalog.ToDynamicTools(_tools), cancellationToken).ConfigureAwait(false);
                lock (_sync)
                {
                    _threadId = threadId;
                    SaveSession();
                }
            }
            finally
            {
                _operationGate.Release();
            }

            NotifyChanged();
            await client.StartMediaTurnAsync(threadId, prompt, model, files.Where(f => !f.ContentType.StartsWith("audio/", StringComparison.Ordinal)).ToArray(), cancellationToken);
        }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (_client == client && _threadId == threadId)
                {
                    _isBusy = false;
                    message.IsTranscribing = false;
                    _errorMessage = exception.Message;
                }
            }

            NotifyChanged();
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? automaticConnection;
        lock (_sync)
        {
            _sendingCancellation?.Cancel();
            _automaticConnectionSuppressed = true;
            automaticConnection = _automaticConnection;
        }

        if (automaticConnection is not null)
        {
            await automaticConnection.CancelAsync().ConfigureAwait(false);
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ICodexAppServerClient? client;
            lock (_sync)
            {
                client = _client;
                _client = null;
                CompleteStreamingMessage();
                SaveSession(throwOnFailure: false);
                _isBusy = false;
                _connectionState = CodexConnectionState.Disconnected;
                _errorMessage = null;
            }

            if (client is not null)
            {
                client.NotificationReceived -= HandleNotificationAsync;
                client.DynamicToolCallReceived -= HandleDynamicToolCallAsync;
                client.ConnectionClosed -= HandleConnectionClosed;
                await client.DisposeAsync().ConfigureAwait(false);
            }

            NotifyChanged();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? automaticTask;
        Task? installationMonitor;
        Task? installationRequest;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _automaticConnectionSuppressed = true;
            automaticTask = _automaticConnectionTask;
            installationMonitor = _installationMonitor;
            installationRequest = _installationRequest?.Task;
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
        if (installationRequest is not null)
        {
            await installationRequest.ConfigureAwait(false);
        }

        if (automaticTask is not null)
        {
            await automaticTask.ConfigureAwait(false);
        }

        if (installationMonitor is not null)
        {
            await installationMonitor.ConfigureAwait(false);
        }

        await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
        _automaticConnection?.Dispose();
        _lifetime.Dispose();
        _operationGate.Dispose();
        _sendingCancellation?.Dispose();
    }

    private Task HandleNotificationAsync(string method, JsonElement parameters)
    {
        lock (_sync)
        {
            switch (method)
            {
                case "item/agentMessage/delta":
                    AppendAssistantDelta(parameters.GetProperty("delta").GetString() ?? string.Empty);
                    break;
                case "turn/completed":
                    _isBusy = false;
                    CompleteStreamingMessage();
                    if (parameters.TryGetProperty("turn", out var turn) && turn.TryGetProperty("status", out var status) && status.GetString() == "failed")
                    {
                        _errorMessage = ReadTurnError(turn) ?? "The Codex turn failed.";
                    }

                    SaveSession(throwOnFailure: false);
                    break;
                case "error":
                    _errorMessage = CodexErrorMessages.Normalize(parameters.TryGetProperty("message", out var message) ? message.GetString() : parameters.GetRawText());
                    _isBusy = false;
                    CompleteStreamingMessage();
                    break;
            }
        }

        NotifyChanged();
        return Task.CompletedTask;
    }

    private void HandleConnectionClosed(Exception? exception)
    {
        lock (_sync)
        {
            if (_connectionState == CodexConnectionState.Disconnected)
            {
                return;
            }

            _connectionState = CodexConnectionState.Failed;
            _isBusy = false;
            _errorMessage = exception?.Message ?? "The Codex app-server connection closed.";
            CompleteStreamingMessage();
        }

        NotifyChanged();
    }

    private async Task<CodexDynamicToolResult> HandleDynamicToolCallAsync(CodexDynamicToolCall call, CancellationToken cancellationToken)
    {
        if (call.Namespace is not null && !string.Equals(call.Namespace, QuantumRpcToolCatalog.Namespace, StringComparison.Ordinal))
        {
            return new CodexDynamicToolResult
            {
                Success = false,
                Text = $"Unknown tool namespace '{call.Namespace}'."
            };
        }

        QuantumRpcTool? tool;
        lock (_sync)
        {
            tool = _tools.FirstOrDefault(candidate => string.Equals(candidate.ToolName, call.Tool, StringComparison.Ordinal));
        }

        if (tool is null)
        {
            return new CodexDynamicToolResult
            {
                Success = false,
                Text = $"Unknown Quantum tool '{call.Tool}'."
            };
        }

        if (!_activeToolCalls.TryAdd(call.CallId, 0))
        {
            return new CodexDynamicToolResult
            {
                Success = false,
                Text = "A duplicate Quantum RPC call id was received."
            };
        }

        try
        {
            var result = await InvokeQuantumRpcAsync(tool, call.Arguments, cancellationToken).ConfigureAwait(false);
            if (!IsInstallationRequest(tool.RpcName))
            {
                AddToolMessage($"Quantum RPC {tool.RpcName}: {(result.Success ? "completed" : "failed")}.");
            }
            else if (!result.Success)
            {
                AddToolMessage(result.Text);
            }

            return result;
        }
        finally
        {
            _activeToolCalls.TryRemove(call.CallId, out _);
        }
    }

    private async Task<CodexDynamicToolResult> InvokeQuantumRpcAsync(QuantumRpcTool tool, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (IsInstallationRequest(tool.RpcName))
        {
            return await RequestPluginInstallationAsync(tool.RpcName, arguments, cancellationToken).ConfigureAwait(false);
        }

        var result = await _rpcInvoker.RequestAsync<JsonElement?>(tool.RpcName, arguments, Context.Empty, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Failure(result.ErrorCode, result.Message);
        }

        var report = result.Value!;
        var outcomes = report.Outcomes.Where(outcome => outcome.Status != QuantumRequestStatus.Pass).ToArray();
        return outcomes switch
        {
            [] => Failure("rpc_not_found", $"No provider handled '{tool.RpcName}'."),
            [{ Status: QuantumRequestStatus.Handled } answer] => new CodexDynamicToolResult
            {
                Success = true,
                Text = answer.Value?.GetRawText() ?? "{\"isSuccess\":true}"
            },
            [var failure] => Failure(failure.ErrorCode!, failure.Message!),
            _ => new CodexDynamicToolResult
            {
                Success = outcomes.Any(outcome => outcome.Status == QuantumRequestStatus.Handled),
                Text = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            }
        };
    }

    private static bool IsInstallationRequest(string rpcName) => rpcName is QuantumPluginInstallationExtensions.InstallLocalCapabilityName or QuantumPluginInstallationExtensions.InstallMarketplaceCapabilityName;
    private async Task<CodexDynamicToolResult> RequestPluginInstallationAsync(string rpcName, JsonElement arguments, CancellationToken cancellationToken)
    {
        TaskCompletionSource request;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_installationRequest is not null)
            {
                return Failure("codex_install_busy", "Another installation request is awaiting confirmation.");
            }

            request = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _installationRequest = request;
        }

        try
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            return await SubmitPluginInstallationAsync(rpcName, arguments, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync)
            {
                _installationRequest = null;
                request.SetResult();
            }
        }
    }

    private async Task<CodexDynamicToolResult> SubmitPluginInstallationAsync(string rpcName, JsonElement arguments, CancellationToken cancellationToken)
    {
        object payload = arguments;
        var label = "Marketplace plugin";
        if (rpcName == QuantumPluginInstallationExtensions.InstallLocalCapabilityName)
        {
            try
            {
                var package = CodexWebPluginPackage.Create(arguments);
                payload = package;
                label = Path.GetFileNameWithoutExtension(package.ArchiveFileName);
            }
            catch (Exception exception) when (exception is DomainException or DomainValidationException)
            {
                return Failure("codex_invalid_request", exception.Message);
            }
        }
        else if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("pluginId", out var id) && id.ValueKind == JsonValueKind.String)
        {
            label = id.GetString() ?? label;
        }

        var result = await _rpcInvoker.RequestSingleAsync<QuantumPluginInstallReceipt>(rpcName, payload, Context.Empty, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return Failure(result.ErrorCode, result.Message);
        }

        lock (_sync)
        {
            _pendingPluginInstalls[result.Value.OperationId] = label;
            _messages.Add(CreateToolMessage($"Quantum accepted the installation of {label}. Waiting for the result."));
            SaveSession(throwOnFailure: false);
            StartInstallationMonitor();
        }

        NotifyChanged();
        return new CodexDynamicToolResult
        {
            Success = true,
            Text = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        };
    }

    // Called under _sync. Poll independently of the model turn so a runtime reload can interrupt that turn.
    private void StartInstallationMonitor()
    {
        if (!_disposed && _pendingPluginInstalls.Count > 0)
        {
            _installationMonitor ??= MonitorPluginInstallationsAsync(_lifetime.Token);
        }
    }

    private async Task MonitorPluginInstallationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                KeyValuePair<string, string>[] pending;
                lock (_sync)
                {
                    if (_pendingPluginInstalls.Count == 0)
                    {
                        _installationMonitor = null;
                        return;
                    }

                    pending = _pendingPluginInstalls.ToArray();
                }

                foreach (var (id, label) in pending)
                {
                    Result<QuantumPluginInstallReceipt> result;
                    try
                    {
                        result = await _rpcInvoker.GetPluginInstallStatusAsync(id, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        _logger.LogWarning(exception, "Could not query plugin installation {OperationId}.", id);
                        result = Result<QuantumPluginInstallReceipt>.From(Result.Fail("codex_install_status_failed", "Quantum could not read the installation status."));
                    }

                    if (result.IsSuccess && result.Value.Status == "pending")
                    {
                        continue;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    var message = !result.IsSuccess ? $"Could not verify installation of {label}: {result.Message}" : result.Value.Status == "succeeded" ? $"Installed {label}. {result.Value.Message}" : $"Installation of {label} failed. {result.Value.Message}";
                    lock (_sync)
                    {
                        if (_disposed)
                        {
                            return;
                        }

                        _pendingPluginInstalls.Remove(id);
                        _messages.Add(CreateToolMessage(message));
                        SaveSession(throwOnFailure: false);
                    }

                    NotifyChanged();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static CodexDynamicToolResult Failure(string errorCode, string message) => new()
    {
        Success = false,
        Text = JsonSerializer.Serialize(new { isSuccess = false, errorCode, message })
    };
    internal static string ResolveModel(string model, IReadOnlyList<CodexModelOption> availableModels)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new DomainException("codex_invalid_request", "Model is required.");
        }

        var match = availableModels.FirstOrDefault(option => string.Equals(option.Id, model.Trim(), StringComparison.OrdinalIgnoreCase));
        return match?.Id ?? throw new DomainException("codex_invalid_state", $"Codex model '{model}' is not available.");
    }

    private void AppendAssistantDelta(string delta)
    {
        var index = _messages.FindLastIndex(static message => message.Role == CodexMessageRole.Assistant && message.IsStreaming);
        if (index < 0)
        {
            _messages.Add(new CodexChatMessage { Id = Guid.NewGuid(), Role = CodexMessageRole.Assistant, Text = delta, CreatedAt = DateTimeOffset.Now, IsStreaming = true });
            return;
        }

        _messages[index] = _messages[index] with
        {
            Text = _messages[index].Text + delta
        };
    }

    private void CompleteStreamingMessage()
    {
        var index = _messages.FindLastIndex(static message => message.IsStreaming);
        if (index >= 0)
        {
            _messages[index] = _messages[index] with
            {
                IsStreaming = false
            };
        }
    }

    private void AddToolMessage(string text)
    {
        lock (_sync)
        {
            _messages.Add(CreateToolMessage(text));
        }

        NotifyChanged();
    }

    private static CodexChatMessage CreateToolMessage(string text) => new()
    {
        Id = Guid.NewGuid(),
        Role = CodexMessageRole.Tool,
        Text = text,
        CreatedAt = DateTimeOffset.Now
    };
    private static string? ReadTurnError(JsonElement turn) => turn.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null && error.TryGetProperty("message", out var message) ? CodexErrorMessages.Normalize(message.GetString()) : null;
    private void SetConnectionState(CodexConnectionState state, string? errorMessage)
    {
        lock (_sync)
        {
            _connectionState = state;
            _errorMessage = errorMessage;
        }

        NotifyChanged();
    }

    private void LoadSession()
    {
        if (_sessionLoaded)
        {
            return;
        }

        if (_sessionStatePath is not null && File.Exists(_sessionStatePath))
        {
            var state = JsonSerializer.Deserialize<CodexSessionState>(File.ReadAllText(_sessionStatePath)) ?? throw new InvalidOperationException("The saved Codex conversation could not be read.");
            if (state.ThreadId is not null && string.IsNullOrWhiteSpace(state.ThreadId) || !Path.IsPathFullyQualified(state.WorkingDirectory))
            {
                throw new InvalidOperationException("The saved Codex conversation is invalid.");
            }

            lock (_sync)
            {
                // Older versions saved IDs for unused threads that Codex never wrote to disk.
                _threadId = state.Messages.Any(message => message.Role == CodexMessageRole.User) ? state.ThreadId : null;
                _hasSession = true;
                _workingDirectory = state.WorkingDirectory;
                _selectedModel = state.Model;
                _messages.Clear();
                _messages.AddRange(state.Messages.Select(message => message with { IsStreaming = false, IsTranscribing = false }));
                foreach (var (id, label) in state.PendingPluginInstalls)
                {
                    _pendingPluginInstalls[id] = label;
                }

                StartInstallationMonitor();
            }
        }

        _sessionLoaded = true;
    }

    // Called under _sync; replace atomically so a shutdown cannot leave a partial session file.
    private void SaveSession(bool throwOnFailure = true)
    {
        if (_sessionStatePath is null || !_hasSession)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_sessionStatePath))!);
            var temporaryPath = _sessionStatePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new CodexSessionState { ThreadId = _threadId, WorkingDirectory = _workingDirectory, Model = _selectedModel, Messages = _messages.ToArray(), PendingPluginInstalls = _pendingPluginInstalls }));
            File.Move(temporaryPath, _sessionStatePath, overwrite: true);
        }
        catch (Exception exception) when (!throwOnFailure && exception is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(exception, "Could not save the Quantum Codex conversation.");
            _errorMessage = "Could not save the Codex conversation: " + exception.Message;
        }
    }

    private void NotifyChanged() => Changed?.Invoke();
}
