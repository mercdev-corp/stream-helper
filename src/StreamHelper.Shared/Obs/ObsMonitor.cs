using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StreamHelper.Shared.Common;

namespace StreamHelper.Shared.Obs;

public sealed class ObsMonitor : IObsMonitor
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _sceneCheckLock = new(1, 1);
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _loopCts;
    private System.Threading.Timer? _reconnectTimer;
    private System.Threading.Timer? _pollTimer;

    private string _host = "127.0.0.1";
    private int _port = 4455;
    private string? _password;
    private int _retryTimeoutSeconds = 5;
    private int _skippedFramesThreshold = 50;
    private int _skippedFramesPeriodSeconds = ObsSettingsConstants.DefaultSkippedFramesPeriodSeconds;
    private string? _audioDeviceName;

    private bool _isRunning;
    private bool _isConnected;
    private bool _isStreaming;
    private bool _isRecording;
    private bool _isReconnecting;
    private bool _hasNetworkIssue;
    private bool _hasRenderIssue;
    private bool _isCaptureDeviceDisconnected;
    private bool _isCaptureDeviceMuted;
    private bool _isAudioSourceInCurrentScene = true;
    private string? _currentProgramSceneName;
    private readonly HashSet<string> _specialAudioInputs = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _availableAudioInputs = new();
    private bool _disposed;

    private readonly RollingFrameCounter _networkDroppedCounter;
    private readonly RollingFrameCounter _renderDroppedCounter;
    private readonly RollingFrameCounter _encoderDroppedCounter;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pendingRequests = new();

    public bool IsConnected => _isConnected;
    public bool IsStreaming => _isStreaming;
    public bool IsRecording => _isRecording;
    public bool IsReconnecting => _isReconnecting;
    public bool HasNetworkIssue => _hasNetworkIssue;
    public bool HasRenderIssue => _hasRenderIssue;
    public bool IsCaptureDeviceDisconnected => _isCaptureDeviceDisconnected;
    public bool IsCaptureDeviceMuted => _isCaptureDeviceMuted;
    public bool IsAudioSourceInCurrentScene => _isAudioSourceInCurrentScene;
    public int SkippedFramesPeriodSeconds
    {
        get
        {
            lock (_lock) return _skippedFramesPeriodSeconds;
        }
    }

    public IReadOnlyList<string> AvailableAudioInputs
    {
        get
        {
            lock (_lock)
            {
                return _availableAudioInputs.ToArray();
            }
        }
    }

    public ObsConnectionState ConnectionState
    {
        get
        {
            if (_isReconnecting) return ObsConnectionState.Reconnecting;
            if (_isConnected) return ObsConnectionState.Connected;
            return ObsConnectionState.Disconnected;
        }
    }

    public bool HasNetworkCongestion => _hasNetworkIssue;
    public bool HasRenderLag => _hasRenderIssue;
    public bool HasEncodingLag => false;
    public bool IsAudioSourceMissing => _isCaptureDeviceDisconnected;
    public bool IsAudioSourceMuted => _isCaptureDeviceMuted;
    public IReadOnlyList<string> GetAudioInputNames() => AvailableAudioInputs;

    public event Action<bool>? ConnectionChanged;
    public event Action? StateChanged;
    public event Action<ObsAudioMeterEventArgs>? AudioMeterReceived;
    public event Action<IReadOnlyList<string>>? AudioInputsChanged;

    public event Action<ObsConnectionState>? ConnectionStateChanged;
    public event Action? StatusUpdated;
    public event Action<double, double>? AudioMeterUpdated;

    public ObsMonitor(
        string host = "127.0.0.1",
        int port = 4455,
        string? password = null,
        int retryTimeoutSeconds = 5,
        int skippedFramesThreshold = 50,
        string? audioDeviceName = null,
        int skippedFramesPeriodSeconds = ObsSettingsConstants.DefaultSkippedFramesPeriodSeconds)
    {
        _host = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
        _port = port > 0 ? port : 4455;
        _password = password;
        _retryTimeoutSeconds = Math.Max(1, retryTimeoutSeconds);
        _skippedFramesThreshold = Math.Max(1, skippedFramesThreshold);
        _audioDeviceName = audioDeviceName;
        _skippedFramesPeriodSeconds = Math.Clamp(
            skippedFramesPeriodSeconds <= 0 ? ObsSettingsConstants.DefaultSkippedFramesPeriodSeconds : skippedFramesPeriodSeconds,
            ObsSettingsConstants.MinSkippedFramesPeriodSeconds,
            ObsSettingsConstants.MaxSkippedFramesPeriodSeconds);

        var window = TimeSpan.FromSeconds(_skippedFramesPeriodSeconds);
        _networkDroppedCounter = new RollingFrameCounter(window);
        _renderDroppedCounter = new RollingFrameCounter(window);
        _encoderDroppedCounter = new RollingFrameCounter(window);

        ConnectionChanged += _ => ConnectionStateChanged?.Invoke(ConnectionState);
        StateChanged += () =>
        {
            StatusUpdated?.Invoke();
            ConnectionStateChanged?.Invoke(ConnectionState);
        };
        AudioMeterReceived += e => AudioMeterUpdated?.Invoke(e.RmsDbfs, e.PeakDbfs);
    }

    public void ConnectAsync(string host, int port, string? password)
    {
        UpdateConfig(host, port, password, _retryTimeoutSeconds, _skippedFramesThreshold, _audioDeviceName, _skippedFramesPeriodSeconds);
        Start();
    }

    public void DisconnectAsync()
    {
        Stop();
    }

    public void SetAudioInputName(string? name)
    {
        UpdateConfig(_host, _port, _password, _retryTimeoutSeconds, _skippedFramesThreshold, name, _skippedFramesPeriodSeconds);
        if (_isConnected)
        {
            Task.Run(async () =>
            {
                try
                {
                    await CheckTargetAudioDeviceAsync().ConfigureAwait(false);
                    await CheckAudioSourceScenePresenceAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            });
        }
    }

    public void SetSkippedFramesThreshold(int threshold)
    {
        UpdateConfig(_host, _port, _password, _retryTimeoutSeconds, threshold, _audioDeviceName, _skippedFramesPeriodSeconds);
    }

    public void SetSkippedFramesPeriod(int seconds)
    {
        int clamped;
        lock (_lock)
        {
            clamped = Math.Clamp(
                seconds <= 0 ? ObsSettingsConstants.DefaultSkippedFramesPeriodSeconds : seconds,
                ObsSettingsConstants.MinSkippedFramesPeriodSeconds,
                ObsSettingsConstants.MaxSkippedFramesPeriodSeconds);
            if (_skippedFramesPeriodSeconds == clamped) return;
            _skippedFramesPeriodSeconds = clamped;
            var window = TimeSpan.FromSeconds(clamped);
            _networkDroppedCounter.Window = window;
            _renderDroppedCounter.Window = window;
            _encoderDroppedCounter.Window = window;
        }
        AppLogger.Info($"[ObsMonitor] Skipped frames evaluation period set to {clamped}s.");
    }

    public void UpdateConfig(
        string host,
        int port,
        string? password,
        int retryTimeoutSeconds,
        int skippedFramesThreshold,
        string? audioDeviceName,
        int skippedFramesPeriodSeconds)
    {
        bool reconnectNeeded = false;
        string newHost;
        int newPort;
        lock (_lock)
        {
            newHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host;
            newPort = port > 0 ? port : 4455;
            if (_host != newHost || _port != newPort || _password != password)
            {
                reconnectNeeded = true;
            }

            _host = newHost;
            _port = newPort;
            _password = password;
            _retryTimeoutSeconds = Math.Max(1, retryTimeoutSeconds);
            _skippedFramesThreshold = Math.Max(1, skippedFramesThreshold);
            _audioDeviceName = audioDeviceName;

            int clampedPeriod = Math.Clamp(
                skippedFramesPeriodSeconds <= 0 ? ObsSettingsConstants.DefaultSkippedFramesPeriodSeconds : skippedFramesPeriodSeconds,
                ObsSettingsConstants.MinSkippedFramesPeriodSeconds,
                ObsSettingsConstants.MaxSkippedFramesPeriodSeconds);
            if (_skippedFramesPeriodSeconds != clampedPeriod)
            {
                _skippedFramesPeriodSeconds = clampedPeriod;
                var window = TimeSpan.FromSeconds(clampedPeriod);
                _networkDroppedCounter.Window = window;
                _renderDroppedCounter.Window = window;
                _encoderDroppedCounter.Window = window;
            }
        }

        AppLogger.Info($"[ObsMonitor] Configuration updated: host={newHost}, port={newPort}, auth={(string.IsNullOrEmpty(password) ? "none" : "configured")}, timeout={_retryTimeoutSeconds}s, threshold={_skippedFramesThreshold}, period={_skippedFramesPeriodSeconds}s, audioDevice='{audioDeviceName ?? "None"}'. Reconnect needed: {reconnectNeeded}.");

        if (reconnectNeeded && _isRunning)
        {
            DisconnectAndScheduleReconnect();
        }
    }

    public void UpdateConfig(
        string host,
        int port,
        string? password,
        int retryTimeoutSeconds,
        int skippedFramesThreshold,
        string? audioDeviceName)
        => UpdateConfig(host, port, password, retryTimeoutSeconds, skippedFramesThreshold, audioDeviceName, _skippedFramesPeriodSeconds);

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _isRunning = true;
        }

        AppLogger.Info($"[ObsMonitor] Starting OBS monitor.");
        StartConnectLoop();
        StartPolling();
    }

    public void Stop()
    {
        lock (_lock)
        {
            _isRunning = false;
        }

        AppLogger.Info($"[ObsMonitor] Stopping OBS monitor.");
        StopTimers();
        DisconnectSocket();
        ResetState();
        ConnectionChanged?.Invoke(false);
        StateChanged?.Invoke();
    }

    private void StartPolling()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _pollTimer?.Dispose();
            _pollTimer = new System.Threading.Timer(OnPollTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
    }

    private void StopTimers()
    {
        lock (_lock)
        {
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;
            _pollTimer?.Dispose();
            _pollTimer = null;
        }
    }

    private void StartConnectLoop()
    {
        lock (_lock)
        {
            if (!_isRunning || _disposed || _isConnected) return;
        }

        _ = Task.Run(async () =>
        {
            while (true)
            {
                string host;
                int port;
                lock (_lock)
                {
                    if (!_isRunning || _disposed || _isConnected) return;
                    host = _host;
                    port = _port;
                }

                try
                {
                    AppLogger.Info($"[ObsMonitor] Connecting to OBS WebSocket at ws://{host}:{port}...");
                    _loopCts?.Dispose();
                    _loopCts = new CancellationTokenSource();
                    var ct = _loopCts.Token;

                    var ws = new ClientWebSocket();
                    lock (_lock)
                    {
                        _webSocket = ws;
                    }

                    var uri = new Uri($"ws://{host}:{port}");
                    using (var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, connectCts.Token))
                    {
                        await ws.ConnectAsync(uri, linked.Token).ConfigureAwait(false);
                    }

                    AppLogger.Info($"[ObsMonitor] WebSocket connected to ws://{host}:{port}. Receiving messages...");
                    await ReceiveLoopAsync(ws, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected on stop/cancel
                }
                catch (Exception ex)
                {
                    AppLogger.Debug($"[ObsMonitor] Connection attempt failed: {ex.Message}");
                }

                // If loop finished, cleanup connection and wait retry timeout
                CleanupConnection();

                int timeoutSec;
                lock (_lock)
                {
                    if (!_isRunning || _disposed) return;
                    timeoutSec = _retryTimeoutSeconds;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(timeoutSec)).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var ms = new MemoryStream();

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"[ObsMonitor] ReceiveAsync error: {ex.Message}");
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                AppLogger.Info($"[ObsMonitor] WebSocket closed by server.");
                break;
            }

            ms.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                var json = Encoding.UTF8.GetString(ms.ToArray());
                ms.SetLength(0);
                ProcessMessage(json);
            }
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("op", out var opProp)) return;
            var op = opProp.GetInt32();

            switch (op)
            {
                case 0: // Hello
                    HandleHello(root.GetProperty("d"));
                    break;
                case 2: // Identified
                    HandleIdentified();
                    break;
                case 5: // Event
                    HandleEvent(root.GetProperty("d"));
                    break;
                case 7: // RequestResponse
                    HandleRequestResponse(root.GetProperty("d"));
                    break;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"[ObsMonitor] Error parsing OBS message: {ex.Message}");
        }
    }

    private void HandleHello(JsonElement data)
    {
        string? authResponse = null;
        if (data.TryGetProperty("authentication", out var authProp))
        {
            string? password;
            lock (_lock)
            {
                password = _password;
            }

            if (!string.IsNullOrEmpty(password) &&
                authProp.TryGetProperty("challenge", out var challengeProp) &&
                authProp.TryGetProperty("salt", out var saltProp))
            {
                var challenge = challengeProp.GetString();
                var salt = saltProp.GetString();
                if (challenge != null && salt != null)
                {
                    authResponse = GenerateAuthResponse(password, salt, challenge);
                }
            }
        }

        AppLogger.Debug($"[ObsMonitor] Received Hello from OBS. Authentication required: {authResponse != null}. Sending Identify...");

        // EventSubscriptions:
        // General (1) | Scenes (4) | Inputs (8) | Outputs (64) | InputVolumeMeters (65536) = 65613
        int eventSubscriptions = 1 | 4 | 8 | 64 | 65536;

        var identifyData = new Dictionary<string, object>
        {
            ["rpcVersion"] = 1,
            ["eventSubscriptions"] = eventSubscriptions
        };

        if (authResponse != null)
        {
            identifyData["authentication"] = authResponse;
        }

        var identifyMsg = new Dictionary<string, object>
        {
            ["op"] = 1,
            ["d"] = identifyData
        };

        var json = JsonSerializer.Serialize(identifyMsg);
        _ = SendRawAsync(json);
    }

    private async void HandleIdentified()
    {
        lock (_lock)
        {
            _isConnected = true;
        }

        AppLogger.Info($"[ObsMonitor] Successfully identified and connected to OBS WebSocket.");

        ConnectionChanged?.Invoke(true);
        StateChanged?.Invoke();

        // Query initial states sequentially: first retrieve available audio inputs
        // and evaluate target device presence/mute, then poll stream/encoder metrics.
        try
        {
            await RefreshAudioInputsAsync().ConfigureAwait(false);
            await PollObsMetricsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[ObsMonitor] Error querying initial OBS state: {ex.Message}", ex);
        }
    }

    private void HandleEvent(JsonElement data)
    {
        if (!data.TryGetProperty("eventType", out var eventTypeProp)) return;
        var eventType = eventTypeProp.GetString();
        if (!data.TryGetProperty("eventData", out var eventData)) return;

        switch (eventType)
        {
            case "StreamStateChanged":
                if (eventData.TryGetProperty("outputActive", out var activeProp))
                {
                    _isStreaming = activeProp.GetBoolean();
                }
                if (eventData.TryGetProperty("outputReconnecting", out var reconProp))
                {
                    _isReconnecting = reconProp.GetBoolean();
                }
                if (!_isStreaming)
                {
                    _isReconnecting = false;
                    _networkDroppedCounter.Reset();
                    _hasNetworkIssue = false;
                }
                AppLogger.Info($"[ObsMonitor] StreamStateChanged: isStreaming={_isStreaming}, isReconnecting={_isReconnecting}");
                StateChanged?.Invoke();
                break;

            case "RecordStateChanged":
                if (eventData.TryGetProperty("outputActive", out var recActiveProp))
                {
                    _isRecording = recActiveProp.GetBoolean();
                }
                if (!_isStreaming && !_isRecording)
                {
                    _renderDroppedCounter.Reset();
                    _encoderDroppedCounter.Reset();
                    _hasRenderIssue = false;
                }
                AppLogger.Info($"[ObsMonitor] RecordStateChanged: isRecording={_isRecording}");
                StateChanged?.Invoke();
                break;

            case "CurrentProgramSceneChanged":
                if (eventData.TryGetProperty("sceneName", out var sceneNameProp))
                {
                    var newScene = sceneNameProp.GetString();
                    AppLogger.Info($"[ObsMonitor] CurrentProgramSceneChanged: '{_currentProgramSceneName}' -> '{newScene}'");
                    lock (_lock)
                    {
                        _currentProgramSceneName = newScene;
                    }
                    _ = CheckAudioSourceScenePresenceAsync();
                }
                break;

            case "InputMuteStateChanged":
                if (eventData.TryGetProperty("inputName", out var inputNameProp) &&
                    eventData.TryGetProperty("inputMuted", out var mutedProp))
                {
                    var inputName = inputNameProp.GetString();
                    string? targetDevice;
                    lock (_lock)
                    {
                        targetDevice = _audioDeviceName;
                    }

                    if (!string.IsNullOrEmpty(targetDevice) &&
                        string.Equals(inputName, targetDevice, StringComparison.OrdinalIgnoreCase))
                    {
                        bool wasMuted = _isCaptureDeviceMuted;
                        _isCaptureDeviceMuted = mutedProp.GetBoolean();
                        AppLogger.Info($"[ObsMonitor] InputMuteStateChanged for '{inputName}': {wasMuted} -> {_isCaptureDeviceMuted}");
                        StateChanged?.Invoke();
                    }
                }
                break;

            case "InputVolumeMeters":
                HandleVolumeMeters(eventData);
                break;

            case "InputCreated":
            case "InputRemoved":
            case "InputNameChanged":
                AppLogger.Debug($"[ObsMonitor] OBS audio input layout changed: {eventType}");
                _ = RefreshAudioInputsAsync();
                break;
        }
    }

    private void HandleVolumeMeters(JsonElement eventData)
    {
        if (!eventData.TryGetProperty("inputs", out var inputsArray)) return;

        string? targetDevice;
        lock (_lock)
        {
            targetDevice = _audioDeviceName;
        }

        if (string.IsNullOrEmpty(targetDevice)) return;

        bool found = false;
        foreach (var input in inputsArray.EnumerateArray())
        {
            if (input.TryGetProperty("inputName", out var nameProp) &&
                string.Equals(nameProp.GetString(), targetDevice, StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                if (input.TryGetProperty("inputLevelsMul", out var levelsArray))
                {
                    double maxPeak = 0.0;
                    double maxRms = 0.0;

                    foreach (var channel in levelsArray.EnumerateArray())
                    {
                        var ch = channel.EnumerateArray().ToArray();
                        if (ch.Length >= 2)
                        {
                            var rms = ch[0].GetDouble();
                            var peak = ch[1].GetDouble();
                            if (rms > maxRms) maxRms = rms;
                            if (peak > maxPeak) maxPeak = peak;
                        }
                    }

                    double peakDbfs = MulToDbfs(maxPeak);
                    double rmsDbfs = MulToDbfs(maxRms);

                    AudioMeterReceived?.Invoke(new ObsAudioMeterEventArgs(
                        targetDevice,
                        rmsDbfs,
                        peakDbfs,
                        DateTime.UtcNow));
                }
                break;
            }
        }

        if (!found)
        {
            AudioMeterReceived?.Invoke(new ObsAudioMeterEventArgs(
                targetDevice,
                -100.0,
                -100.0,
                DateTime.UtcNow));
        }
    }

    public static double MulToDbfs(double mul)
    {
        if (mul <= 0.00001) return -100.0;
        var db = 20.0 * Math.Log10(mul);
        return Math.Max(-100.0, db);
    }

    private void HandleRequestResponse(JsonElement data)
    {
        if (!data.TryGetProperty("requestId", out var reqIdProp)) return;
        var requestId = reqIdProp.GetString();
        if (string.IsNullOrEmpty(requestId)) return;

        if (_pendingRequests.TryRemove(requestId, out var tcs))
        {
            if (data.TryGetProperty("requestStatus", out var status) &&
                status.TryGetProperty("result", out var resultProp) &&
                resultProp.GetBoolean())
            {
                tcs.TrySetResult(data.TryGetProperty("responseData", out var respData) ? respData.Clone() : default);
            }
            else
            {
                tcs.TrySetException(new InvalidOperationException("OBS request returned failure status."));
            }
        }
    }

    public async Task<JsonElement> SendRequestAsync(string requestType, object? requestData = null, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = tcs;

        var d = new Dictionary<string, object?>
        {
            ["requestType"] = requestType,
            ["requestId"] = requestId
        };

        if (requestData != null)
        {
            d["requestData"] = requestData;
        }

        var requestPayload = new Dictionary<string, object?>
        {
            ["op"] = 6,
            ["d"] = d
        };

        var json = JsonSerializer.Serialize(requestPayload);
        await SendRawAsync(json).ConfigureAwait(false);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        linkedCts.Token.Register(() =>
        {
            if (_pendingRequests.TryRemove(requestId, out var pending))
            {
                pending.TrySetCanceled();
            }
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task SendRawAsync(string json)
    {
        ClientWebSocket? ws;
        lock (_lock)
        {
            ws = _webSocket;
        }

        if (ws == null || ws.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to send OBS raw message: {ex.Message}");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async void OnPollTick(object? state)
    {
        if (!_isRunning || _disposed || !_isConnected) return;

        try
        {
            await PollObsMetricsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OBS Poll error: {ex.Message}");
        }
    }

    private async Task PollObsMetricsAsync()
    {
        if (!_isConnected) return;

        bool needsInputs;
        lock (_lock)
        {
            needsInputs = _availableAudioInputs.Count == 0;
        }
        if (needsInputs)
        {
            await RefreshAudioInputsAsync().ConfigureAwait(false);
        }

        int threshold;
        lock (_lock)
        {
            threshold = _skippedFramesThreshold;
        }

        var now = DateTime.UtcNow;

        // Query Stream Status
        try
        {
            var streamStatus = await SendRequestAsync("GetStreamStatus").ConfigureAwait(false);
            if (streamStatus.ValueKind == JsonValueKind.Object)
            {
                _isStreaming = streamStatus.TryGetProperty("outputActive", out var active) && active.GetBoolean();
                _isReconnecting = streamStatus.TryGetProperty("outputReconnecting", out var recon) && recon.GetBoolean();

                long outputSkippedFrames = streamStatus.TryGetProperty("outputSkippedFrames", out var skipped) ? skipped.GetInt64() : 0;
                bool prevNet = _hasNetworkIssue;
                _hasNetworkIssue = _networkDroppedCounter.Update(outputSkippedFrames, threshold, now, _isStreaming);
                if (prevNet != _hasNetworkIssue)
                {
                    AppLogger.Warn($"[ObsMonitor] Network congestion alert changed: {_hasNetworkIssue} (skipped frames={outputSkippedFrames}, threshold={threshold}).");
                }
            }
        }
        catch
        {
            // Optional if output inactive
        }

        // Query Stats
        try
        {
            var stats = await SendRequestAsync("GetStats").ConfigureAwait(false);
            if (stats.ValueKind == JsonValueKind.Object)
            {
                long renderSkipped = stats.TryGetProperty("renderSkippedFrames", out var rSkipped) ? rSkipped.GetInt64() : 0;
                long encoderSkipped = stats.TryGetProperty("outputSkippedFrames", out var eSkipped) ? eSkipped.GetInt64() : 0;

                bool outputActive = _isStreaming || _isRecording;
                var renderAlert = _renderDroppedCounter.Update(renderSkipped, threshold, now, outputActive);
                var encoderAlert = _encoderDroppedCounter.Update(encoderSkipped, threshold, now, outputActive);
                bool prevRender = _hasRenderIssue;
                _hasRenderIssue = renderAlert || encoderAlert;
                if (prevRender != _hasRenderIssue)
                {
                    AppLogger.Warn($"[ObsMonitor] Render/encoder lag alert changed: {_hasRenderIssue} (render skipped={renderSkipped}, encoder skipped={encoderSkipped}, threshold={threshold}).");
                }
            }
        }
        catch
        {
            // Optional
        }

        // Check target audio device presence & mute status
        await CheckTargetAudioDeviceAsync().ConfigureAwait(false);

        // Check scene presence
        await CheckAudioSourceScenePresenceAsync().ConfigureAwait(false);

        StateChanged?.Invoke();
    }

    private async Task CheckTargetAudioDeviceAsync()
    {
        string? targetAudioDevice;
        bool isConnected;
        lock (_lock)
        {
            targetAudioDevice = _audioDeviceName;
            isConnected = _isConnected;
        }

        if (!isConnected)
        {
            return;
        }

        bool stateChanged = false;

        if (!string.IsNullOrEmpty(targetAudioDevice))
        {
            bool exists;
            lock (_lock)
            {
                exists = _availableAudioInputs.Contains(targetAudioDevice, StringComparer.OrdinalIgnoreCase);
            }

            if (_isCaptureDeviceDisconnected != !exists)
            {
                _isCaptureDeviceDisconnected = !exists;
                AppLogger.Warn($"[ObsMonitor] Target OBS audio input '{targetAudioDevice}' disconnected state changed: isDisconnected={_isCaptureDeviceDisconnected}.");
                stateChanged = true;
            }

            if (exists)
            {
                try
                {
                    var muteData = await SendRequestAsync("GetInputMute", new { inputName = targetAudioDevice }).ConfigureAwait(false);
                    if (muteData.ValueKind == JsonValueKind.Object && muteData.TryGetProperty("inputMuted", out var mutedProp))
                    {
                        bool muted = mutedProp.GetBoolean();
                        if (_isCaptureDeviceMuted != muted)
                        {
                            _isCaptureDeviceMuted = muted;
                            AppLogger.Info($"[ObsMonitor] Target OBS audio input '{targetAudioDevice}' mute state changed: isMuted={_isCaptureDeviceMuted}.");
                            stateChanged = true;
                        }
                    }
                }
                catch
                {
                }
            }
            else
            {
                if (_isCaptureDeviceMuted)
                {
                    _isCaptureDeviceMuted = false;
                    AppLogger.Info($"[ObsMonitor] Target OBS audio input '{targetAudioDevice}' missing; mute flag cleared.");
                    stateChanged = true;
                }
            }
        }
        else
        {
            if (_isCaptureDeviceDisconnected || _isCaptureDeviceMuted)
            {
                _isCaptureDeviceDisconnected = false;
                _isCaptureDeviceMuted = false;
                stateChanged = true;
            }
        }

        if (stateChanged)
        {
            StateChanged?.Invoke();
        }
    }

    private async Task CheckAudioSourceScenePresenceAsync()
    {
        string? targetAudioDevice;
        bool isConnected;
        string? programScene;
        lock (_lock)
        {
            targetAudioDevice = _audioDeviceName;
            isConnected = _isConnected;
            programScene = _currentProgramSceneName;
        }

        if (!isConnected || string.IsNullOrEmpty(targetAudioDevice))
        {
            if (!_isAudioSourceInCurrentScene)
            {
                _isAudioSourceInCurrentScene = true;
                StateChanged?.Invoke();
            }
            return;
        }

        bool isSpecial;
        lock (_lock)
        {
            isSpecial = _specialAudioInputs.Contains(targetAudioDevice);
        }

        if (isSpecial)
        {
            if (!_isAudioSourceInCurrentScene)
            {
                _isAudioSourceInCurrentScene = true;
                AppLogger.Info($"[ObsMonitor] Target OBS audio input '{targetAudioDevice}' is a global special input; treated as present in scene.");
                StateChanged?.Invoke();
            }
            return;
        }

        if (string.IsNullOrEmpty(programScene))
        {
            try
            {
                var sceneResp = await SendRequestAsync("GetCurrentProgramScene").ConfigureAwait(false);
                if (sceneResp.ValueKind == JsonValueKind.Object && sceneResp.TryGetProperty("currentProgramSceneName", out var curSceneProp))
                {
                    programScene = curSceneProp.GetString();
                    lock (_lock)
                    {
                        _currentProgramSceneName = programScene;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"[ObsMonitor] Failed to query current program scene: {ex.Message}");
                return;
            }
        }

        if (string.IsNullOrEmpty(programScene))
        {
            return;
        }

        if (!await _sceneCheckLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            bool isPresent = await IsSourceInSceneHierarchyAsync(programScene, targetAudioDevice).ConfigureAwait(false);
            if (_isAudioSourceInCurrentScene != isPresent)
            {
                _isAudioSourceInCurrentScene = isPresent;
                AppLogger.Info($"[ObsMonitor] Target OBS audio input '{targetAudioDevice}' scene presence in '{programScene}' changed: isPresent={isPresent}.");
                StateChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"[ObsMonitor] Error checking scene hierarchy presence: {ex.Message}");
        }
        finally
        {
            _sceneCheckLock.Release();
        }
    }

    private async Task<bool> IsSourceInSceneHierarchyAsync(string rootScene, string targetSource)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task<bool> SearchSceneAsync(string sceneName, int depth)
        {
            if (depth > 5 || !visited.Add(sceneName)) return false;

            try
            {
                var resp = await SendRequestAsync("GetSceneItemList", new { sceneName }).ConfigureAwait(false);
                if (resp.ValueKind == JsonValueKind.Object && resp.TryGetProperty("sceneItems", out var itemsArray))
                {
                    foreach (var item in itemsArray.EnumerateArray())
                    {
                        string? sourceName = item.TryGetProperty("sourceName", out var sn) ? sn.GetString() : null;
                        if (string.IsNullOrEmpty(sourceName)) continue;

                        if (string.Equals(sourceName, targetSource, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        bool isGroup = item.TryGetProperty("isGroup", out var ig) && ig.ValueKind == JsonValueKind.True;
                        if (isGroup)
                        {
                            if (await SearchGroupAsync(sourceName, depth + 1).ConfigureAwait(false))
                            {
                                return true;
                            }
                        }

                        string? sourceType = item.TryGetProperty("sourceType", out var st) ? st.GetString() : null;
                        if (string.Equals(sourceType, "OBS_SOURCE_TYPE_SCENE", StringComparison.OrdinalIgnoreCase))
                        {
                            if (await SearchSceneAsync(sourceName, depth + 1).ConfigureAwait(false))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"[ObsMonitor] Error querying scene item list for '{sceneName}': {ex.Message}");
            }

            return false;
        }

        async Task<bool> SearchGroupAsync(string groupName, int depth)
        {
            if (depth > 5 || !visited.Add(groupName)) return false;

            try
            {
                var resp = await SendRequestAsync("GetGroupSceneItemList", new { sceneName = groupName }).ConfigureAwait(false);
                if (resp.ValueKind == JsonValueKind.Object && resp.TryGetProperty("sceneItems", out var itemsArray))
                {
                    foreach (var item in itemsArray.EnumerateArray())
                    {
                        string? sourceName = item.TryGetProperty("sourceName", out var sn) ? sn.GetString() : null;
                        if (string.IsNullOrEmpty(sourceName)) continue;

                        if (string.Equals(sourceName, targetSource, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }

                        bool isGroup = item.TryGetProperty("isGroup", out var ig) && ig.ValueKind == JsonValueKind.True;
                        if (isGroup)
                        {
                            if (await SearchGroupAsync(sourceName, depth + 1).ConfigureAwait(false))
                            {
                                return true;
                            }
                        }

                        string? sourceType = item.TryGetProperty("sourceType", out var st) ? st.GetString() : null;
                        if (string.Equals(sourceType, "OBS_SOURCE_TYPE_SCENE", StringComparison.OrdinalIgnoreCase))
                        {
                            if (await SearchSceneAsync(sourceName, depth + 1).ConfigureAwait(false))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Debug($"[ObsMonitor] Error querying group scene item list for '{groupName}': {ex.Message}");
            }

            return false;
        }

        return await SearchSceneAsync(rootScene, 0).ConfigureAwait(false);
    }

    public async Task RefreshAudioInputsAsync()
    {
        if (!_isConnected) return;

        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Query standard inputs
            try
            {
                var result = await SendRequestAsync("GetInputList").ConfigureAwait(false);
                if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("inputs", out var inputsArray))
                {
                    foreach (var item in inputsArray.EnumerateArray())
                    {
                        if (item.TryGetProperty("inputName", out var nameProp))
                        {
                            var name = nameProp.GetString();
                            if (!string.IsNullOrEmpty(name))
                            {
                                names.Add(name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[ObsMonitor] Failed to get OBS input list: {ex.Message}", ex);
            }

            // 2. Also query special inputs (Desktop Audio, Mic/Aux, etc.)
            try
            {
                var specialResult = await SendRequestAsync("GetSpecialInputs").ConfigureAwait(false);
                if (specialResult.ValueKind == JsonValueKind.Object)
                {
                    string[] specialKeys = { "desktop1", "desktop2", "mic1", "mic2", "mic3", "mic4" };
                    var detectedSpecials = new List<string>();
                    foreach (var key in specialKeys)
                    {
                        if (specialResult.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
                        {
                            var sName = prop.GetString();
                            if (!string.IsNullOrWhiteSpace(sName))
                            {
                                names.Add(sName);
                                detectedSpecials.Add(sName);
                            }
                        }
                    }

                    lock (_lock)
                    {
                        _specialAudioInputs.Clear();
                        foreach (var s in detectedSpecials)
                        {
                            _specialAudioInputs.Add(s);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[ObsMonitor] Failed to get OBS special inputs: {ex.Message}", ex);
            }

            var sortedNames = names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

            bool inputsChanged = false;
            lock (_lock)
            {
                if (!_availableAudioInputs.SequenceEqual(sortedNames, StringComparer.OrdinalIgnoreCase))
                {
                    _availableAudioInputs = sortedNames;
                    inputsChanged = true;
                }
            }

            // Immediately re-check target device presence & mute status against updated inputs
            await CheckTargetAudioDeviceAsync().ConfigureAwait(false);
            await CheckAudioSourceScenePresenceAsync().ConfigureAwait(false);

            if (inputsChanged)
            {
                AppLogger.Info($"[ObsMonitor] Refreshed OBS audio inputs ({sortedNames.Count} found): [{string.Join(", ", sortedNames)}].");
                AudioInputsChanged?.Invoke(sortedNames.ToArray());
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[ObsMonitor] Failed to refresh OBS audio inputs: {ex.Message}", ex);
        }
    }

    private void DisconnectAndScheduleReconnect()
    {
        DisconnectSocket();
        CleanupConnection();
    }

    private void CleanupConnection()
    {
        bool wasConnected;
        bool hadInputs;
        lock (_lock)
        {
            wasConnected = _isConnected;
            _isConnected = false;
            hadInputs = _availableAudioInputs.Count > 0;
            _availableAudioInputs = new List<string>();
        }

        ResetState();

        if (wasConnected)
        {
            AppLogger.Warn($"[ObsMonitor] Disconnected from OBS WebSocket. Scheduling reconnect in {_retryTimeoutSeconds}s.");
            ConnectionChanged?.Invoke(false);
            if (hadInputs)
            {
                AudioInputsChanged?.Invoke(Array.Empty<string>());
            }
            StateChanged?.Invoke();
        }

        ScheduleReconnect();
    }

    private void ScheduleReconnect()
    {
        lock (_lock)
        {
            if (!_isRunning || _disposed) return;
            _reconnectTimer?.Dispose();
            AppLogger.Debug($"[ObsMonitor] Reconnect timer scheduled for {_retryTimeoutSeconds}s.");
            _reconnectTimer = new System.Threading.Timer(_ => StartConnectLoop(), null, TimeSpan.FromSeconds(_retryTimeoutSeconds), Timeout.InfiniteTimeSpan);
        }
    }

    private void DisconnectSocket()
    {
        lock (_lock)
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _loopCts = null;

            if (_webSocket != null)
            {
                try
                {
                    _webSocket.Dispose();
                }
                catch
                {
                }
                _webSocket = null;
            }
        }

        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingRequests.Clear();
    }

    private void ResetState()
    {
        _isStreaming = false;
        _isRecording = false;
        _isReconnecting = false;
        _hasNetworkIssue = false;
        _hasRenderIssue = false;
        _isCaptureDeviceDisconnected = false;
        _isCaptureDeviceMuted = false;
        _isAudioSourceInCurrentScene = true;
        _currentProgramSceneName = null;

        lock (_lock)
        {
            _availableAudioInputs.Clear();
            _specialAudioInputs.Clear();
        }

        _networkDroppedCounter.Reset();
        _renderDroppedCounter.Reset();
        _encoderDroppedCounter.Reset();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _isRunning = false;
        }

        StopTimers();
        DisconnectSocket();
        _sendLock.Dispose();
        _sceneCheckLock.Dispose();
    }

    private static string GenerateAuthResponse(string password, string salt, string challenge)
    {
        // 1. secret = base64(sha256(password + salt))
        var secretString = password + salt;
        var secretBytes = SHA256.HashData(Encoding.UTF8.GetBytes(secretString));
        var secretBase64 = Convert.ToBase64String(secretBytes);

        // 2. authResponse = base64(sha256(secret + challenge))
        var authString = secretBase64 + challenge;
        var authBytes = SHA256.HashData(Encoding.UTF8.GetBytes(authString));
        return Convert.ToBase64String(authBytes);
    }
}
