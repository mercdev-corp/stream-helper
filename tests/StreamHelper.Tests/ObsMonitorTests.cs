using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StreamHelper.Shared.Obs;

namespace StreamHelper.Tests;

[TestClass]
public sealed class ObsMonitorTests
{
    [TestMethod]
    public void RollingFrameCounter_WithinThreshold_NoAlert()
    {
        var counter = new RollingFrameCounter(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5));
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        // 10 dropped frames over 30s with threshold 50 -> no alert
        Assert.IsFalse(counter.Update(0, 50, start, true));
        Assert.IsFalse(counter.Update(10, 50, start.AddSeconds(30), true));
        Assert.AreEqual(10, counter.CurrentDelta);
        Assert.IsFalse(counter.IsAlertActive);
    }

    [TestMethod]
    public void RollingFrameCounter_ExceedsThreshold_TriggersAlertAndHysteresis()
    {
        var counter = new RollingFrameCounter(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5));
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        counter.Update(0, 50, start, true);
        // Exceeds threshold at t=10s (delta = 60 > 50)
        var alert = counter.Update(60, 50, start.AddSeconds(10), true);
        Assert.IsTrue(alert);
        Assert.IsTrue(counter.IsAlertActive);

        // At t=75s (oldest sample at t=0s is purged, remaining sample at t=10s is 60), total is 70 -> delta = 10 <= 50
        // Hysteresis starts at t=75s
        alert = counter.Update(70, 50, start.AddSeconds(75), true);
        Assert.IsTrue(alert, "Should remain active during hysteresis period");

        // At t=78s (3s into hysteresis) -> still active
        alert = counter.Update(70, 50, start.AddSeconds(78), true);
        Assert.IsTrue(alert, "Still active at 3 seconds");

        // At t=80s (5s into hysteresis) -> recovers!
        alert = counter.Update(70, 50, start.AddSeconds(80), true);
        Assert.IsFalse(alert, "Should recover after 5 seconds of remaining below threshold");
        Assert.IsFalse(counter.IsAlertActive);
    }

    [TestMethod]
    public void RollingFrameCounter_OutputInactive_ClearsImmediately()
    {
        var counter = new RollingFrameCounter(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5));
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        counter.Update(0, 50, start, true);
        counter.Update(100, 50, start.AddSeconds(10), true);
        Assert.IsTrue(counter.IsAlertActive);

        // Streaming stops
        var alert = counter.Update(100, 50, start.AddSeconds(11), false);
        Assert.IsFalse(alert);
        Assert.IsFalse(counter.IsAlertActive);
    }

    [TestMethod]
    public void RollingFrameCounter_DefaultWindow_IsFiveSeconds()
    {
        var counter = new RollingFrameCounter();
        Assert.AreEqual(TimeSpan.FromSeconds(5), counter.Window);
    }

    [TestMethod]
    public void RollingFrameCounter_DynamicWindowConfiguration_PrunesOldSamples()
    {
        var counter = new RollingFrameCounter(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        counter.Update(0, 50, start, true);
        counter.Update(10, 50, start.AddSeconds(4), true);
        counter.Update(20, 50, start.AddSeconds(8), true);

        // Window = 10s. At t=8s, cutoff is 8 - 10 = -2. Sample at t=0s is kept. Delta = 20 - 0 = 20.
        Assert.AreEqual(20, counter.CurrentDelta);

        // Dynamically shrink window to 5s.
        counter.Window = TimeSpan.FromSeconds(5);
        Assert.AreEqual(TimeSpan.FromSeconds(5), counter.Window);

        // Next update at t=9s: cutoff is 9 - 5 = 4s.
        // Sample at t=0s is pruned (< 4s). Sample at t=4s (value 10) is kept.
        // Current value = 25. Delta = 25 - 10 = 15.
        counter.Update(25, 50, start.AddSeconds(9), true);
        Assert.AreEqual(15, counter.CurrentDelta);
    }

    [TestMethod]
    public void RollingFrameCounter_SetWindow_ThrowsOnNonPositive()
    {
        var counter = new RollingFrameCounter();
        Assert.Throws<ArgumentOutOfRangeException>(() => counter.SetWindow(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => counter.SetWindow(TimeSpan.FromSeconds(-1)));
    }

    [TestMethod]
    public void ObsMonitor_MulToDbfs_CalculatesAccurately()
    {
        Assert.AreEqual(-100.0, ObsMonitor.MulToDbfs(0.0));
        Assert.AreEqual(-100.0, ObsMonitor.MulToDbfs(-1.0));
        Assert.AreEqual(0.0, ObsMonitor.MulToDbfs(1.0), 0.001);
        Assert.AreEqual(-6.02, ObsMonitor.MulToDbfs(0.5), 0.01);
        Assert.AreEqual(-20.0, ObsMonitor.MulToDbfs(0.1), 0.01);
    }

    [TestMethod]
    public void ObsMonitor_SkippedFramesPeriod_DefaultsAndConfiguration()
    {
        using var monitor = new ObsMonitor();
        Assert.AreEqual(5, monitor.SkippedFramesPeriodSeconds);
        Assert.IsTrue(monitor.IsAudioSourceInCurrentScene);

        // Update via SetSkippedFramesPeriod
        monitor.SetSkippedFramesPeriod(15);
        Assert.AreEqual(15, monitor.SkippedFramesPeriodSeconds);

        // Clamping on SetSkippedFramesPeriod
        monitor.SetSkippedFramesPeriod(0);
        Assert.AreEqual(5, monitor.SkippedFramesPeriodSeconds);

        monitor.SetSkippedFramesPeriod(500);
        Assert.AreEqual(300, monitor.SkippedFramesPeriodSeconds);

        // Update via UpdateConfig
        monitor.UpdateConfig("127.0.0.1", 4455, null, 5, 50, "Game Audio", 25);
        Assert.AreEqual(25, monitor.SkippedFramesPeriodSeconds);

        // Constructor with explicit period
        using var customMonitor = new ObsMonitor(skippedFramesPeriodSeconds: 10);
        Assert.AreEqual(10, customMonitor.SkippedFramesPeriodSeconds);
    }

    [TestMethod]
    public async Task ObsMonitor_ConnectsAndAuthenticatesAgainstMockServer()
    {
        int port = 14455;
        string password = "secret_obs_password";
        using var mockServer = new MockObsWebSocketServer(port, password);
        mockServer.Start();

        using var monitor = new ObsMonitor("127.0.0.1", port, password, retryTimeoutSeconds: 1);

        var connectedTcs = new TaskCompletionSource<bool>();
        monitor.ConnectionChanged += connected =>
        {
            if (connected) connectedTcs.TrySetResult(true);
        };

        monitor.Start();

        var timeout = Task.Delay(5000);
        var completed = await Task.WhenAny(connectedTcs.Task, timeout);
        Assert.AreEqual(connectedTcs.Task, completed, "Monitor failed to connect and authenticate within timeout.");

        Assert.IsTrue(monitor.IsConnected);
    }

    [TestMethod]
    public async Task ObsMonitor_ReceivesVolumeMetersAndParsesDbfs()
    {
        int port = 14456;
        using var mockServer = new MockObsWebSocketServer(port, password: null);
        mockServer.Start();

        using var monitor = new ObsMonitor("127.0.0.1", port, password: null, retryTimeoutSeconds: 1, audioDeviceName: "Desktop Audio");

        var meterTcs = new TaskCompletionSource<ObsAudioMeterEventArgs>();
        monitor.AudioMeterReceived += e =>
        {
            if (e.InputName == "Desktop Audio")
            {
                meterTcs.TrySetResult(e);
            }
        };

        monitor.Start();

        // Wait for connection
        var connectTimeout = DateTime.UtcNow.AddSeconds(5);
        while (!monitor.IsConnected && DateTime.UtcNow < connectTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsTrue(monitor.IsConnected);

        // Send mock volume meter event
        await mockServer.BroadcastVolumeMeterEventAsync("Desktop Audio", 0.5, 0.25);

        var completed = await Task.WhenAny(meterTcs.Task, Task.Delay(3000));
        Assert.AreEqual(meterTcs.Task, completed, "Audio meter event was not received within timeout.");

        var meter = await meterTcs.Task;
        Assert.AreEqual("Desktop Audio", meter.InputName);
        Assert.IsTrue(meter.PeakDbfs > -10.0 && meter.PeakDbfs < -5.0); // 0.5 ~ -6.02 dBFS
        Assert.IsTrue(meter.RmsDbfs > -15.0 && meter.RmsDbfs < -10.0); // 0.25 ~ -12.04 dBFS
    }

    [TestMethod]
    public async Task ObsMonitor_RefreshAudioInputsAsync_SortsInputsAlphabetically()
    {
        var port = 14568;
        using var mockServer = new MockObsWebSocketServer(port, null);
        mockServer.SetInputs(new[]
        {
            new { inputName = "Zebra Microphone", inputKind = "wasapi_input_capture" },
            new { inputName = "Alpha Capture Device", inputKind = "wasapi_output_capture" },
            new { inputName = "bravo chat audio", inputKind = "wasapi_output_capture" }
        });
        mockServer.Start();

        using var monitor = new ObsMonitor("127.0.0.1", port, password: null, retryTimeoutSeconds: 1);
        monitor.Start();

        var connectTimeout = DateTime.UtcNow.AddSeconds(5);
        while (!monitor.IsConnected && DateTime.UtcNow < connectTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsTrue(monitor.IsConnected);

        await monitor.RefreshAudioInputsAsync();

        var inputs = monitor.GetAudioInputNames();
        Assert.AreEqual(3, inputs.Count);
        Assert.AreEqual("Alpha Capture Device", inputs[0]);
        Assert.AreEqual("bravo chat audio", inputs[1]);
        Assert.AreEqual("Zebra Microphone", inputs[2]);
    }

    [TestMethod]
    public async Task ObsMonitor_WhenObsStartsAfterServer_LoadsAudioInputsAndUpdatesDeviceStatus()
    {
        int port = 14580;
        using var monitor = new ObsMonitor("127.0.0.1", port, password: null, retryTimeoutSeconds: 1, audioDeviceName: "Game Audio");

        // Start monitor BEFORE OBS server starts
        monitor.Start();

        // While OBS is not running:
        Assert.IsFalse(monitor.IsConnected);
        Assert.AreEqual(0, monitor.GetAudioInputNames().Count);
        Assert.IsFalse(monitor.IsCaptureDeviceDisconnected, "Should not report capture device missing while OBS is disconnected");

        // Now start mock OBS server
        using var mockServer = new MockObsWebSocketServer(port, null);
        mockServer.SetInputs(new[]
        {
            new { inputName = "Game Audio", inputKind = "wasapi_output_capture" },
            new { inputName = "Mic/Aux", inputKind = "wasapi_input_capture" }
        });
        mockServer.Start();

        // Wait for connection
        var connectTimeout = DateTime.UtcNow.AddSeconds(5);
        while (!monitor.IsConnected && DateTime.UtcNow < connectTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsTrue(monitor.IsConnected, "Monitor should successfully connect after OBS launches");

        // Wait for inputs to be loaded
        var inputsTimeout = DateTime.UtcNow.AddSeconds(3);
        while (monitor.GetAudioInputNames().Count < 2 && DateTime.UtcNow < inputsTimeout)
        {
            await Task.Delay(50);
        }

        var inputs = monitor.GetAudioInputNames();
        Assert.IsTrue(inputs.Count >= 2);
        CollectionAssert.Contains(inputs.ToList(), "Game Audio");

        // Selected capture device must be detected as connected (not disconnected) and unmuted
        Assert.IsFalse(monitor.IsCaptureDeviceDisconnected);
        Assert.IsFalse(monitor.IsCaptureDeviceMuted);

        // Switch to a missing device
        monitor.SetAudioInputName("NonExistent Source");
        var missingTimeout = DateTime.UtcNow.AddSeconds(3);
        while (!monitor.IsCaptureDeviceDisconnected && DateTime.UtcNow < missingTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsTrue(monitor.IsCaptureDeviceDisconnected, "Missing device should be detected as disconnected");

        // Switch back to existing device
        monitor.SetAudioInputName("Game Audio");
        var recoverTimeout = DateTime.UtcNow.AddSeconds(3);
        while (monitor.IsCaptureDeviceDisconnected && DateTime.UtcNow < recoverTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsFalse(monitor.IsCaptureDeviceDisconnected, "Existing device should be marked as connected");
    }

    [TestMethod]
    public async Task ObsMonitor_WhenTargetDeviceMutedInObs_DetectsMutedState()
    {
        int port = 14581;
        using var mockServer = new MockObsWebSocketServer(port, null);
        mockServer.SetInputs(new[]
        {
            new { inputName = "Game Audio", inputKind = "wasapi_output_capture" }
        });
        mockServer.SetInputMuted(true);
        mockServer.Start();

        using var monitor = new ObsMonitor("127.0.0.1", port, password: null, retryTimeoutSeconds: 1, audioDeviceName: "Game Audio");
        monitor.Start();

        var connectTimeout = DateTime.UtcNow.AddSeconds(5);
        while (!monitor.IsConnected && DateTime.UtcNow < connectTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsTrue(monitor.IsConnected);

        var muteTimeout = DateTime.UtcNow.AddSeconds(3);
        while (!monitor.IsCaptureDeviceMuted && DateTime.UtcNow < muteTimeout)
        {
            await Task.Delay(50);
        }

        Assert.IsFalse(monitor.IsCaptureDeviceDisconnected);
        Assert.IsTrue(monitor.IsCaptureDeviceMuted);

        // Broadcast unmute event
        await mockServer.BroadcastInputMuteChangedEventAsync("Game Audio", false);
        var unmuteTimeout = DateTime.UtcNow.AddSeconds(3);
        while (monitor.IsCaptureDeviceMuted && DateTime.UtcNow < unmuteTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsFalse(monitor.IsCaptureDeviceMuted);
    }

    [TestMethod]
    public async Task ObsMonitor_SpecialAudioInputs_AlwaysReportedAsInScene()
    {
        int port = 14583;
        using var mockServer = new MockObsWebSocketServer(port, null);
        mockServer.SetInputs(new[]
        {
            new { inputName = "Desktop Audio", inputKind = "wasapi_output_capture" }
        });
        mockServer.SetSpecialInputs(new { desktop1 = "Desktop Audio" });
        mockServer.SetCurrentProgramScene("Scene Without Audio");
        mockServer.SetSceneItems("Scene Without Audio", Array.Empty<object>());
        mockServer.Start();

        using var monitor = new ObsMonitor("127.0.0.1", port, password: null, retryTimeoutSeconds: 1, audioDeviceName: "Desktop Audio");
        monitor.Start();

        var connectTimeout = DateTime.UtcNow.AddSeconds(5);
        while (!monitor.IsConnected && DateTime.UtcNow < connectTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsTrue(monitor.IsConnected);

        // Allow poll / check
        await Task.Delay(200);
        Assert.IsTrue(monitor.IsAudioSourceInCurrentScene, "Special inputs must always be reported as present in scene");
    }

    [TestMethod]
    public async Task ObsMonitor_NormalAudioSource_DetectsPresenceAndRespondsToSceneChange()
    {
        int port = 14584;
        using var mockServer = new MockObsWebSocketServer(port, null);
        mockServer.SetInputs(new[]
        {
            new { inputName = "Game Audio", inputKind = "wasapi_output_capture" }
        });
        mockServer.SetSpecialInputs(new { });
        mockServer.SetCurrentProgramScene("Game Scene");
        mockServer.SetSceneItems("Game Scene", new object[]
        {
            new { sourceName = "Game Audio", isGroup = false, sourceType = "OBS_SOURCE_TYPE_INPUT" }
        });
        mockServer.SetSceneItems("BRB Scene", new object[]
        {
            new { sourceName = "BRB Text", isGroup = false, sourceType = "OBS_SOURCE_TYPE_INPUT" }
        });
        mockServer.Start();

        using var monitor = new ObsMonitor("127.0.0.1", port, password: null, retryTimeoutSeconds: 1, audioDeviceName: "Game Audio");
        monitor.Start();

        var connectTimeout = DateTime.UtcNow.AddSeconds(5);
        while (!monitor.IsConnected && DateTime.UtcNow < connectTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsTrue(monitor.IsConnected);

        // Wait for presence detection in Game Scene
        var presenceTimeout = DateTime.UtcNow.AddSeconds(3);
        while (!monitor.IsAudioSourceInCurrentScene && DateTime.UtcNow < presenceTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsTrue(monitor.IsAudioSourceInCurrentScene, "Audio source should be present in Game Scene");

        // Switch scene to BRB Scene
        await mockServer.BroadcastCurrentProgramSceneChangedAsync("BRB Scene");
        var sceneChangeTimeout = DateTime.UtcNow.AddSeconds(3);
        while (monitor.IsAudioSourceInCurrentScene && DateTime.UtcNow < sceneChangeTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsFalse(monitor.IsAudioSourceInCurrentScene, "Audio source should not be present in BRB Scene");

        // Switch back to Game Scene
        await mockServer.BroadcastCurrentProgramSceneChangedAsync("Game Scene");
        var restoreTimeout = DateTime.UtcNow.AddSeconds(3);
        while (!monitor.IsAudioSourceInCurrentScene && DateTime.UtcNow < restoreTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsTrue(monitor.IsAudioSourceInCurrentScene, "Audio source should be present again after returning to Game Scene");
    }

    [TestMethod]
    public async Task ObsMonitor_GroupAndNestedSceneHierarchy_DetectsPresenceAndHandlesCircularReferences()
    {
        int port = 14585;
        using var mockServer = new MockObsWebSocketServer(port, null);
        mockServer.SetInputs(new[]
        {
            new { inputName = "Game Audio", inputKind = "wasapi_output_capture" }
        });
        mockServer.SetSpecialInputs(new { });
        mockServer.SetCurrentProgramScene("Main Scene");

        // Main Scene contains group "Audio Group" and nested scene "Sub Scene"
        mockServer.SetSceneItems("Main Scene", new object[]
        {
            new { sourceName = "Audio Group", isGroup = true, sourceType = "OBS_SOURCE_TYPE_SCENE" },
            new { sourceName = "Sub Scene", isGroup = false, sourceType = "OBS_SOURCE_TYPE_SCENE" }
        });
        // Sub Scene circularly references Main Scene
        mockServer.SetSceneItems("Sub Scene", new object[]
        {
            new { sourceName = "Main Scene", isGroup = false, sourceType = "OBS_SOURCE_TYPE_SCENE" }
        });
        // Audio Group contains Game Audio
        mockServer.SetGroupSceneItems("Audio Group", new object[]
        {
            new { sourceName = "Game Audio", isGroup = false, sourceType = "OBS_SOURCE_TYPE_INPUT" }
        });
        mockServer.Start();

        using var monitor = new ObsMonitor("127.0.0.1", port, password: null, retryTimeoutSeconds: 1, audioDeviceName: "Game Audio");
        monitor.Start();

        var connectTimeout = DateTime.UtcNow.AddSeconds(5);
        while (!monitor.IsConnected && DateTime.UtcNow < connectTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsTrue(monitor.IsConnected);

        // Wait for presence detection through group
        var presenceTimeout = DateTime.UtcNow.AddSeconds(3);
        while (!monitor.IsAudioSourceInCurrentScene && DateTime.UtcNow < presenceTimeout)
        {
            await Task.Delay(50);
        }
        Assert.IsTrue(monitor.IsAudioSourceInCurrentScene, "Audio source should be found inside Audio Group despite circular reference between scenes");
    }
}

internal sealed class MockObsWebSocketServer : IDisposable
{
    private readonly System.Net.Sockets.TcpListener _listener;
    private readonly int _port;
    private readonly string? _password;
    private WebSocket? _activeSocket;
    private readonly CancellationTokenSource _cts = new();
    private object? _customInputs;
    private object? _customSpecialInputs;
    private bool _inputMuted;
    private string _currentProgramSceneName = "Scene 1";
    private readonly Dictionary<string, object> _scenes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _groups = new(StringComparer.OrdinalIgnoreCase);

    public void SetInputs(object inputs) => _customInputs = inputs;
    public void SetSpecialInputs(object specialInputs) => _customSpecialInputs = specialInputs;
    public void SetInputMuted(bool muted) => _inputMuted = muted;
    public void SetCurrentProgramScene(string sceneName) => _currentProgramSceneName = sceneName;
    public void SetSceneItems(string sceneName, object items) => _scenes[sceneName] = items;
    public void SetGroupSceneItems(string groupName, object items) => _groups[groupName] = items;

    public MockObsWebSocketServer(int port, string? password)
    {
        _port = port;
        _password = password;
        _listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, _port);
        _listener.Server.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReuseAddress, true);
    }

    public void Start()
    {
        _listener.Start();
        Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                _ = ProcessTcpClientAsync(tcpClient);
            }
            catch
            {
                break;
            }
        }
    }

    private async Task ProcessTcpClientAsync(System.Net.Sockets.TcpClient tcpClient)
    {
        try
        {
            var stream = tcpClient.GetStream();
            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, _cts.Token).ConfigureAwait(false);
            var requestText = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            var keyMatch = System.Text.RegularExpressions.Regex.Match(requestText, @"Sec-WebSocket-Key:\s*([^\r\n]+)");
            if (!keyMatch.Success) return;

            var key = keyMatch.Groups[1].Value.Trim();
            var acceptKey = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

            var response = "HTTP/1.1 101 Switching Protocols\r\n" +
                           "Upgrade: websocket\r\n" +
                           "Connection: Upgrade\r\n" +
                           $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

            var responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length, _cts.Token).ConfigureAwait(false);

            var ws = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30));
            _activeSocket = ws;

            // Step 1: Send Hello (OpCode 0)
            string salt = "test_salt_12345";
            string challenge = "test_challenge_67890";

            var helloData = new Dictionary<string, object>
            {
                ["obsWebSocketVersion"] = "5.0.0",
                ["rpcVersion"] = 1
            };

            if (!string.IsNullOrEmpty(_password))
            {
                helloData["authentication"] = new Dictionary<string, object>
                {
                    ["challenge"] = challenge,
                    ["salt"] = salt
                };
            }

            var helloMsg = new Dictionary<string, object>
            {
                ["op"] = 0,
                ["d"] = helloData
            };

            await SendMessageAsync(JsonSerializer.Serialize(helloMsg)).ConfigureAwait(false);

            // Step 2: Receive loop
            var recvBuffer = new byte[8192];
            while (!_cts.IsCancellationRequested && _activeSocket.State == WebSocketState.Open)
            {
                var result = await _activeSocket.ReceiveAsync(new ArraySegment<byte>(recvBuffer), _cts.Token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(recvBuffer, 0, result.Count);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var op = root.GetProperty("op").GetInt32();

                if (op == 1) // Identify
                {
                    var d = root.GetProperty("d");
                    if (!string.IsNullOrEmpty(_password))
                    {
                        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(_password + salt)));
                        var expectedAuth = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));

                        var clientAuth = d.GetProperty("authentication").GetString();
                        if (clientAuth != expectedAuth)
                        {
                            await _activeSocket.CloseAsync(WebSocketCloseStatus.ProtocolError, "Auth failed", CancellationToken.None);
                            return;
                        }
                    }

                    // Send Identified (OpCode 2)
                    var identifiedMsg = new Dictionary<string, object>
                    {
                        ["op"] = 2,
                        ["d"] = new Dictionary<string, object>
                        {
                            ["negotiatedRpcVersion"] = 1
                        }
                    };
                    await SendMessageAsync(JsonSerializer.Serialize(identifiedMsg)).ConfigureAwait(false);
                }
                else if (op == 6) // Request
                {
                    var d = root.GetProperty("d");
                    var reqType = d.GetProperty("requestType").GetString();
                    var reqId = d.GetProperty("requestId").GetString();

                    object respData = new { };
                    if (reqType == "GetStreamStatus")
                    {
                        respData = new { outputActive = true, outputReconnecting = false, outputSkippedFrames = 10 };
                    }
                    else if (reqType == "GetStats")
                    {
                        respData = new { renderSkippedFrames = 5, outputSkippedFrames = 2 };
                    }
                    else if (reqType == "GetInputList")
                    {
                        respData = new { inputs = _customInputs ?? (object)new[] { new { inputName = "Desktop Audio", inputKind = "wasapi_output_capture" } } };
                    }
                    else if (reqType == "GetInputMute")
                    {
                        respData = new { inputMuted = _inputMuted };
                    }
                    else if (reqType == "GetSpecialInputs")
                    {
                        respData = _customSpecialInputs ?? (object)new { };
                    }
                    else if (reqType == "GetCurrentProgramScene")
                    {
                        respData = new { currentProgramSceneName = _currentProgramSceneName };
                    }
                    else if (reqType == "GetSceneItemList")
                    {
                        string sceneName = d.TryGetProperty("requestData", out var rd) && rd.TryGetProperty("sceneName", out var sn)
                            ? sn.GetString() ?? "" : "";
                        var items = _scenes.TryGetValue(sceneName, out var scItems) ? scItems : Array.Empty<object>();
                        respData = new { sceneItems = items };
                    }
                    else if (reqType == "GetGroupSceneItemList")
                    {
                        string groupName = d.TryGetProperty("requestData", out var rd) && rd.TryGetProperty("sceneName", out var gn)
                            ? gn.GetString() ?? "" : "";
                        var items = _groups.TryGetValue(groupName, out var grpItems) ? grpItems : Array.Empty<object>();
                        respData = new { sceneItems = items };
                    }

                    var respMsg = new Dictionary<string, object>
                    {
                        ["op"] = 7,
                        ["d"] = new Dictionary<string, object>
                        {
                            ["requestType"] = reqType!,
                            ["requestId"] = reqId!,
                            ["requestStatus"] = new { result = true, code = 100 },
                            ["responseData"] = respData
                        }
                    };
                    await SendMessageAsync(JsonSerializer.Serialize(respMsg)).ConfigureAwait(false);
                }
            }
        }
        catch
        {
        }
    }

    public async Task BroadcastVolumeMeterEventAsync(string inputName, double peak, double rms)
    {
        var evtMsg = new Dictionary<string, object>
        {
            ["op"] = 5,
            ["d"] = new Dictionary<string, object>
            {
                ["eventType"] = "InputVolumeMeters",
                ["eventData"] = new
                {
                    inputs = new[]
                    {
                        new
                        {
                            inputName = inputName,
                            inputLevelsMul = new[]
                            {
                                new[] { rms, peak, peak }
                            }
                        }
                    }
                }
            }
        };

        await SendMessageAsync(JsonSerializer.Serialize(evtMsg)).ConfigureAwait(false);
    }

    public async Task BroadcastInputMuteChangedEventAsync(string inputName, bool muted)
    {
        var evtMsg = new Dictionary<string, object>
        {
            ["op"] = 5,
            ["d"] = new Dictionary<string, object>
            {
                ["eventType"] = "InputMuteStateChanged",
                ["eventData"] = new
                {
                    inputName = inputName,
                    inputMuted = muted
                }
            }
        };

        await SendMessageAsync(JsonSerializer.Serialize(evtMsg)).ConfigureAwait(false);
    }

    public async Task BroadcastCurrentProgramSceneChangedAsync(string sceneName)
    {
        _currentProgramSceneName = sceneName;
        var evtMsg = new Dictionary<string, object>
        {
            ["op"] = 5,
            ["d"] = new Dictionary<string, object>
            {
                ["eventType"] = "CurrentProgramSceneChanged",
                ["eventData"] = new
                {
                    sceneName = sceneName
                }
            }
        };

        await SendMessageAsync(JsonSerializer.Serialize(evtMsg)).ConfigureAwait(false);
    }

    private async Task SendMessageAsync(string json)
    {
        if (_activeSocket != null && _activeSocket.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await _activeSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _activeSocket?.Dispose();
        _listener.Stop();
    }
}
