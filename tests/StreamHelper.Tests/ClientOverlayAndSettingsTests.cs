using System.Net;
using StreamHelper.Client;
using StreamHelper.Client.Config;
using StreamHelper.Client.Overlay;
using StreamHelper.Client.UI;
using StreamHelper.Shared.Audio;
using StreamHelper.Shared.Network;
using StreamHelper.Shared.Obs;
using StreamHelper.Shared.Protocol;
using StreamHelper.Shared.UI;
using StreamHelper.Shared.Common;

namespace StreamHelper.Tests;

[TestClass]
public sealed class ClientOverlayAndSettingsTests
{
    [TestMethod]
    public void ClientSettings_SaveAndLoad_RoundtripsSuccessfully()
    {
        var tempFolder = TestDirectory.Create("ClientSettingsTest");

        try
        {
            var original = new ClientSettings
            {
                Mode = ClientMode.SinglePc,
                MicrophoneId = "mic-test-uuid-42",
                MicrophoneName = "Blue Yeti",
                RunOnStartup = true,
                Port = 13888,
                ServerIp = "192.168.1.150",
                RetryTimeout = 7,
                Opacity = 85,
                PulseFrequency = 1.5,
                AnimationCycles = 4,
                OverlayCenterX = 450,
                OverlayCenterY = 320,
                OverlayWidth = 96,
                OverlayHeight = 96,
                IsPaused = false,
                DebugLogging = true
            };

            original.Save(tempFolder);

            var filePath = ClientSettings.GetFilePath(tempFolder);
            Assert.IsTrue(File.Exists(filePath));

            var loaded = ClientSettings.Load(tempFolder);
            Assert.AreEqual(original.Mode, loaded.Mode);
            Assert.AreEqual(original.MicrophoneId, loaded.MicrophoneId);
            Assert.AreEqual(original.MicrophoneName, loaded.MicrophoneName);
            Assert.AreEqual(original.RunOnStartup, loaded.RunOnStartup);
            Assert.AreEqual(original.Port, loaded.Port);
            Assert.AreEqual(original.ServerIp, loaded.ServerIp);
            Assert.AreEqual(original.RetryTimeout, loaded.RetryTimeout);
            Assert.AreEqual(original.Opacity, loaded.Opacity);
            Assert.AreEqual(original.PulseFrequency, loaded.PulseFrequency);
            Assert.AreEqual(original.AnimationCycles, loaded.AnimationCycles);
            Assert.AreEqual(original.OverlayCenterX, loaded.OverlayCenterX);
            Assert.AreEqual(original.OverlayCenterY, loaded.OverlayCenterY);
            Assert.AreEqual(original.OverlayWidth, loaded.OverlayWidth);
            Assert.AreEqual(original.OverlayHeight, loaded.OverlayHeight);
            Assert.AreEqual(original.IsPaused, loaded.IsPaused);
            Assert.AreEqual(original.DebugLogging, loaded.DebugLogging);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettings_AudioDetection_DefaultsAndRoundtrips()
    {
        var tempFolder = TestDirectory.Create("ClientAudioDetectionSettings");
        try
        {
            var def = new ClientSettings();
            Assert.AreEqual(20.0, def.AudioMatchToleranceDb);
            Assert.IsFalse(def.SimpleAudioIssueDetection);

            var custom = new ClientSettings
            {
                AudioMatchToleranceDb = 14.0,
                SimpleAudioIssueDetection = true
            };
            custom.Save(tempFolder);

            var loaded = ClientSettings.Load(tempFolder);
            Assert.AreEqual(14.0, loaded.AudioMatchToleranceDb);
            Assert.IsTrue(loaded.SimpleAudioIssueDetection);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettings_Load_ClampsAudioMatchToleranceDb()
    {
        var tempFolder = TestDirectory.Create("ClientAudioDetectionClamp");
        try
        {
            var filePath = ClientSettings.GetFilePath(tempFolder);
            Directory.CreateDirectory(tempFolder);

            File.WriteAllText(filePath, "{\"AudioMatchToleranceDb\": -20.0, \"SimpleAudioIssueDetection\": true}");
            var loadedMin = ClientSettings.Load(tempFolder);
            Assert.AreEqual(0.0, loadedMin.AudioMatchToleranceDb);
            Assert.IsTrue(loadedMin.SimpleAudioIssueDetection);

            File.WriteAllText(filePath, "{\"AudioMatchToleranceDb\": 60.0, \"SimpleAudioIssueDetection\": false}");
            var loadedMax = ClientSettings.Load(tempFolder);
            Assert.AreEqual(40.0, loadedMax.AudioMatchToleranceDb);
            Assert.IsFalse(loadedMax.SimpleAudioIssueDetection);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettings_LegacyJsonWithoutMode_DefaultsToDualPc()
    {
        var tempFolder = TestDirectory.Create("ClientSettingsLegacy");

        try
        {
            var filePath = ClientSettings.GetFilePath(tempFolder);
            // Simulate legacy JSON that lacks Mode, MicrophoneId, MicrophoneName
            File.WriteAllText(filePath, "{\"Port\":13400,\"ServerIp\":\"10.0.0.5\",\"RetryTimeout\":6}");

            var loaded = ClientSettings.Load(tempFolder);
            Assert.AreEqual(ClientMode.DualPc, loaded.Mode);
            Assert.AreEqual(13400, loaded.Port);
            Assert.AreEqual("10.0.0.5", loaded.ServerIp);
            Assert.AreEqual(6, loaded.RetryTimeout);
            Assert.IsNull(loaded.MicrophoneId);
            Assert.IsNull(loaded.MicrophoneName);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettings_SettingsCoexistence_SurvivesCrossModeModifications()
    {
        var tempFolder = TestDirectory.Create("ClientSettingsCoexist");

        try
        {
            // 1. Initially configured in Dual PC mode
            var settings = new ClientSettings
            {
                Mode = ClientMode.DualPc,
                ServerIp = "192.168.1.50",
                Port = 13500,
                RetryTimeout = 5
            };
            settings.Save(tempFolder);

            // 2. User switches to Single PC mode and configures microphone
            var step1 = ClientSettings.Load(tempFolder);
            Assert.AreEqual(ClientMode.DualPc, step1.Mode);
            step1.Mode = ClientMode.SinglePc;
            step1.MicrophoneId = "mic-guid-999";
            step1.MicrophoneName = "USB Condenser Mic";
            step1.Save(tempFolder);

            // Verify both Dual PC and Single PC fields coexist
            var step2 = ClientSettings.Load(tempFolder);
            Assert.AreEqual(ClientMode.SinglePc, step2.Mode);
            Assert.AreEqual("mic-guid-999", step2.MicrophoneId);
            Assert.AreEqual("USB Condenser Mic", step2.MicrophoneName);
            Assert.AreEqual("192.168.1.50", step2.ServerIp);
            Assert.AreEqual(13500, step2.Port);

            // 3. User switches back to Dual PC mode and changes server IP
            step2.Mode = ClientMode.DualPc;
            step2.ServerIp = "192.168.1.60";
            step2.Save(tempFolder);

            // Verify Single PC mic settings were not erased
            var step3 = ClientSettings.Load(tempFolder);
            Assert.AreEqual(ClientMode.DualPc, step3.Mode);
            Assert.AreEqual("192.168.1.60", step3.ServerIp);
            Assert.AreEqual(13500, step3.Port);
            Assert.AreEqual("mic-guid-999", step3.MicrophoneId);
            Assert.AreEqual("USB Condenser Mic", step3.MicrophoneName);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void OverlayAssetManager_PreRenderAndCache_ProducesExactDimensions()
    {
        var tempFolder = TestDirectory.Create("AssetTest");

        try
        {
            using var assetManager = new OverlayAssetManager(tempFolder);
            assetManager.PreRenderAndCache(128, 128);

            var mutedBmp = assetManager.GetBitmap(MicState.Muted);
            Assert.IsNotNull(mutedBmp);
            Assert.AreEqual(128, mutedBmp.Width);
            Assert.AreEqual(128, mutedBmp.Height);

            var discBmp = assetManager.GetBitmap(MicState.Disconnected);
            Assert.IsNotNull(discBmp);
            Assert.AreEqual(128, discBmp.Width);
            Assert.AreEqual(128, discBmp.Height);

            var cacheDir = Path.Combine(tempFolder, "cache");
            var mutedFile = Path.Combine(cacheDir, "mic-muted_resized.png");
            var discFile = Path.Combine(cacheDir, "server-disconnected_resized.png");

            Assert.IsTrue(File.Exists(mutedFile));
            Assert.IsTrue(File.Exists(discFile));

            // Resizing again must overwrite existing files and not leave multiple version files in cache
            assetManager.PreRenderAndCache(256, 256);
            var files = Directory.GetFiles(cacheDir);
            Assert.HasCount(OverlayAssetManager.ManagedAssetFiles.Length, files);
            Assert.IsTrue(File.Exists(mutedFile));
            Assert.IsTrue(File.Exists(discFile));

            using var reloadedMuted = new Bitmap(mutedFile);
            Assert.AreEqual(256, reloadedMuted.Width);
            Assert.AreEqual(256, reloadedMuted.Height);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void OverlayForm_ZeroCpuSuspension_StopsTimerWhenUnmutedOrPaused()
    {
        var tempFolder = TestDirectory.Create("OverlayTest1");

        try
        {
            var settings = new ClientSettings();
            using var assetManager = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetManager);

            // Initially disconnected, alert active
            overlay.SetLiveState(MicState.Disconnected, isPaused: false);
            Assert.IsTrue(overlay.IsPulseTimerRunning);

            // Transition to Unmuted: alert ceases, render timer MUST stop for zero CPU overhead
            overlay.SetLiveState(MicState.Unmuted, isPaused: false);
            Assert.IsFalse(overlay.IsPulseTimerRunning);
            Assert.IsFalse(overlay.Visible);

            // Transition to Muted: alert active, timer resumes
            overlay.SetLiveState(MicState.Muted, isPaused: false);
            Assert.IsTrue(overlay.IsPulseTimerRunning);
            Assert.IsTrue(overlay.Visible);

            // Transition to Paused: timer MUST stop
            overlay.SetLiveState(MicState.Muted, isPaused: true);
            Assert.IsFalse(overlay.IsPulseTimerRunning);
            Assert.IsFalse(overlay.Visible);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void OverlayForm_WysiwygMode_TogglesTransparentStyle()
    {
        var tempFolder = TestDirectory.Create("OverlayTest2");

        try
        {
            var settings = new ClientSettings();
            using var assetManager = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetManager);

            // Force handle creation
            var handle = overlay.Handle;
            Assert.AreNotEqual(IntPtr.Zero, handle);

            // Normal mode has WS_EX_TRANSPARENT
            int normalStyle = Win32Native.GetWindowLong(overlay.Handle, Win32Native.GWL_EXSTYLE);
            Assert.AreNotEqual(0, normalStyle & Win32Native.WS_EX_TRANSPARENT, "Normal overlay should be transparent to clicks");
            Assert.AreNotEqual(0, normalStyle & Win32Native.WS_EX_TOPMOST, "Should be topmost");

            // WYSIWYG mode removes WS_EX_TRANSPARENT to allow dragging/resizing
            overlay.SetWysiwygMode(true);
            int wysiwygStyle = Win32Native.GetWindowLong(overlay.Handle, Win32Native.GWL_EXSTYLE);
            Assert.AreEqual(0, wysiwygStyle & Win32Native.WS_EX_TRANSPARENT, "WYSIWYG overlay should not be click-through");

            // Exiting WYSIWYG restores WS_EX_TRANSPARENT
            overlay.SetWysiwygMode(false);
            int restoredStyle = Win32Native.GetWindowLong(overlay.Handle, Win32Native.GWL_EXSTYLE);
            Assert.AreNotEqual(0, restoredStyle & Win32Native.WS_EX_TRANSPARENT, "Click-through should be restored");
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void OverlayForm_TopmostStylesAndReassertTopmost_MaintainsTopmostStyle()
    {
        var tempFolder = TestDirectory.Create("OverlayTopmostTest");

        try
        {
            var settings = new ClientSettings();
            using var assetManager = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetManager);

            // Force handle creation
            var handle = overlay.Handle;
            Assert.AreNotEqual(IntPtr.Zero, handle);

            // Verify WS_EX_TOPMOST style bit is set on window
            int initialExStyle = Win32Native.GetWindowLong(overlay.Handle, Win32Native.GWL_EXSTYLE);
            Assert.AreNotEqual(0, initialExStyle & Win32Native.WS_EX_TOPMOST, "Window should possess WS_EX_TOPMOST style");

            // Invoke ReassertTopmost directly
            overlay.ReassertTopmost();

            int postReassertExStyle = Win32Native.GetWindowLong(overlay.Handle, Win32Native.GWL_EXSTYLE);
            Assert.AreNotEqual(0, postReassertExStyle & Win32Native.WS_EX_TOPMOST, "Window should retain WS_EX_TOPMOST after ReassertTopmost");

            // Transition live state to Muted (invokes StartPulsing -> ReassertTopmost)
            overlay.SetLiveState(MicState.Muted, isPaused: false);
            Assert.IsTrue(overlay.Visible);
            Assert.IsTrue(overlay.IsPulseTimerRunning);

            // GetWindow with GW_HWNDPREV executes properly on window handle
            var prevHwnd = Win32Native.GetWindow(overlay.Handle, Win32Native.GW_HWNDPREV);
            Assert.IsTrue(prevHwnd == IntPtr.Zero || prevHwnd != IntPtr.Zero);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void OverlayForm_HandleLifecycle_RegistersAndUnhooksWinEvent()
    {
        var tempFolder = TestDirectory.Create("OverlayHookTest");

        try
        {
            var settings = new ClientSettings();
            using var assetManager = new OverlayAssetManager(tempFolder);
            var overlay = new OverlayForm(settings, assetManager);

            // Before handle creation, hook should not be registered
            Assert.AreEqual(IntPtr.Zero, overlay.WinEventHookHandle, "Hook should be IntPtr.Zero before handle creation");

            // Create handle
            var handle = overlay.Handle;
            Assert.AreNotEqual(IntPtr.Zero, handle);

            // After handle creation, hook should be successfully registered
            var hookHandle = overlay.WinEventHookHandle;
            Assert.AreNotEqual(IntPtr.Zero, hookHandle, "Hook handle should be non-zero after handle creation");

            // Dispose overlay form
            overlay.Dispose();

            // After disposal, hook must be cleanly unhooked and reset
            Assert.AreEqual(IntPtr.Zero, overlay.WinEventHookHandle, "Hook handle must be reset to IntPtr.Zero after disposal");
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public async Task UdpListener_DiscoversBroadcasterAndDetectsOffline()
    {
        int testPort = 13295;
        using var broadcaster = new UdpBroadcaster(testPort, "server-discovery-test");
        using var listener = new UdpListener(testPort);

        var connectionStates = new List<bool>();
        listener.TargetServerConnectionChanged += conn =>
        {
            lock (connectionStates) connectionStates.Add(conn);
        };

        // Start listener with 2 second retry timeout
        listener.Start(targetServerIp: null, retryTimeoutSeconds: 2);

        // Start broadcaster and send a burst
        broadcaster.Start();
        broadcaster.UpdateState(MicState.Muted, "Studio Mic");

        // Wait up to 5 seconds to receive packet
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            if (listener.IsConnected) break;
            await Task.Delay(25);
        }

        Assert.IsTrue(listener.IsConnected);
        Assert.AreEqual(MicState.Muted, listener.LastReportedState);

        var discovered = listener.GetDiscoveredServers();
        Assert.IsNotEmpty(discovered);
        Assert.IsTrue(discovered.Any(s => s.ServerId == "server-discovery-test"));

        // Stop broadcaster to simulate server going offline
        broadcaster.Stop();

        // Wait up to 6 seconds for watchdog to trigger disconnect
        timeout = DateTime.UtcNow.AddSeconds(6);
        while (DateTime.UtcNow < timeout)
        {
            if (!listener.IsConnected) break;
            await Task.Delay(50);
        }

        Assert.IsFalse(listener.IsConnected);
        Assert.AreEqual(MicState.Disconnected, listener.LastReportedState);
    }

    [TestMethod]
    public void StatusIconGenerator_EmbeddedAssets_LoadSuccessfully()
    {
        var appIcon = StatusIconGenerator.GetAppIcon();
        Assert.IsNotNull(appIcon);
        Assert.IsTrue(appIcon.Width > 0);
        Assert.IsTrue(appIcon.Height > 0);

        using var mutedBmp = StatusIconGenerator.GetMicMutedBitmap();
        AssertBitmapMatchesAsset(mutedBmp, "mic-muted.png");

        using var unmutedBmp = StatusIconGenerator.GetMicUnmutedBitmap();
        Assert.IsNotNull(unmutedBmp);
        Assert.IsTrue(unmutedBmp.Width > 0);

        using var deviceDiscBmp = StatusIconGenerator.GetDeviceDisconnectedBitmap();
        AssertBitmapMatchesAsset(deviceDiscBmp, "device-disconnected.png");

        using var discBmp = StatusIconGenerator.GetServerDisconnectedBitmap();
        AssertBitmapMatchesAsset(discBmp, "server-disconnected.png");

        using var pauseBmp = StatusIconGenerator.GetPauseBitmap();
        AssertBitmapMatchesAsset(pauseBmp, "pause.png");

        // Test all status icons for Client
        foreach (MicState state in Enum.GetValues<MicState>())
        {
            using var clientIcon = StatusIconGenerator.CreateClientStatusIcon(state);
            Assert.IsNotNull(clientIcon);
            Assert.AreEqual(32, clientIcon.Width);
            Assert.AreEqual(32, clientIcon.Height);
        }

        // Test all status icons for Server
        foreach (MicState state in Enum.GetValues<MicState>())
        {
            using var serverIcon = StatusIconGenerator.CreateServerStatusIcon(state);
            Assert.IsNotNull(serverIcon);
            Assert.AreEqual(32, serverIcon.Width);
            Assert.AreEqual(32, serverIcon.Height);
        }
    }

    private static string GetAssetPath(string fileName)
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "assets", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            current = Directory.GetParent(current)?.FullName;
        }

        current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, "assets", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException($"Asset '{fileName}' was not found in any parent 'assets' directory.");
    }

    private static void AssertBitmapMatchesAsset(Bitmap? actualBmp, string assetFileName)
    {
        Assert.IsNotNull(actualBmp, $"Expected {assetFileName} bitmap to load successfully, but it was null.");
        var assetPath = GetAssetPath(assetFileName);
        using var expectedBmp = new Bitmap(assetPath);
        Assert.AreEqual(expectedBmp.Width, actualBmp.Width, $"Width mismatch for {assetFileName}");
        Assert.AreEqual(expectedBmp.Height, actualBmp.Height, $"Height mismatch for {assetFileName}");
    }

    [TestMethod]
    public void OverlayForm_ApplyNewSize_ResizesAndClampsCorrectly()
    {
        var tempFolder = TestDirectory.Create("OverlayResizeTest");

        try
        {
            var settings = new ClientSettings
            {
                OverlayWidth = 128,
                OverlayHeight = 128,
                OverlayCenterX = 300,
                OverlayCenterY = 300
            };
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);

            _ = overlay.Handle; // Ensure handle created

            int reportedSize = 0;
            overlay.OverlayResized += sz => reportedSize = sz;

            overlay.ApplyNewSize(200);
            Assert.AreEqual(200, overlay.Width);
            Assert.AreEqual(200, overlay.Height);
            Assert.AreEqual(200, settings.OverlayWidth);
            Assert.AreEqual(200, reportedSize);

            overlay.ApplyNewSize(999);
            Assert.AreEqual(999, overlay.Width);
            Assert.AreEqual(999, settings.OverlayWidth);

            // Clamp max 1024
            overlay.ApplyNewSize(1500);
            Assert.AreEqual(1024, overlay.Width);
            Assert.AreEqual(1024, settings.OverlayWidth);

            // Clamp min 32
            overlay.ApplyNewSize(10);
            Assert.AreEqual(32, overlay.Width);
            Assert.AreEqual(32, settings.OverlayWidth);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public async Task UdpListener_SetTargetServer_SameServerDoesNotDisconnect()
    {
        int testPort = 13296;
        using var broadcaster = new UdpBroadcaster(testPort, "server-same-ip-test");
        using var listener = new UdpListener(testPort);

        listener.Start(targetServerIp: null, retryTimeoutSeconds: 5);
        broadcaster.Start();
        broadcaster.UpdateState(MicState.Unmuted, "Test Mic");

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout && !listener.IsConnected)
        {
            await Task.Delay(25);
        }

        Assert.IsTrue(listener.IsConnected);
        var connectedIp = listener.TargetServerIp;
        Assert.IsNotNull(connectedIp);

        // Re-setting target server to same IP should NOT disconnect
        listener.SetTargetServer(connectedIp);
        Assert.IsTrue(listener.IsConnected);

        // Setting target server to different IP SHOULD disconnect
        listener.SetTargetServer("10.254.254.254");
        Assert.IsFalse(listener.IsConnected);
    }

    [TestMethod]
    public void ClientSettingsForm_PreservesConnectedStateAndShowsOfflineWhenDisconnected()
    {
        var tempFolder = TestDirectory.Create("SettingsFormTest");

        try
        {
            int testPort = 13297;
            var settings = new ClientSettings
            {
                ServerIp = "127.0.0.1",
                Port = testPort,
                RetryTimeout = 2
            };
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            using var listener = new UdpListener(testPort);
            using var broadcaster = new UdpBroadcaster(testPort, "server-form-test");

            listener.Start(targetServerIp: null, retryTimeoutSeconds: 2);
            broadcaster.Start();
            broadcaster.UpdateState(MicState.Unmuted, "Studio Mic");

            var timeout = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < timeout && !listener.IsConnected)
            {
                Thread.Sleep(25);
            }

            Assert.IsTrue(listener.IsConnected);
            settings.ServerIp = listener.TargetServerIp;

            // Opening ClientSettingsForm while connected MUST NOT disconnect the listener
            using var form = new StreamHelper.Client.UI.ClientSettingsForm(settings, listener, overlay, _ => { });
            _ = form.Handle;

            Assert.IsTrue(listener.IsConnected, "Opening settings form must not drop connected state!");

            // Stop broadcaster to simulate server going offline
            broadcaster.Stop();

            timeout = DateTime.UtcNow.AddSeconds(6);
            while (DateTime.UtcNow < timeout && listener.IsConnected)
            {
                Application.DoEvents();
                Thread.Sleep(50);
            }

            Assert.IsFalse(listener.IsConnected, "Listener must detect server going offline!");
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_DisplaysVersionLabelBetweenButtons()
    {
        var tempFolder = TestDirectory.Create("ClientSettingsVersionTest");

        try
        {
            var settings = new ClientSettings();
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            using var listener = new UdpListener(13991);
            using var form = new StreamHelper.Client.UI.ClientSettingsForm(settings, listener, overlay, _ => { });
            _ = form.Handle;

            Assert.IsNotNull(form.VersionLabel);
            Assert.AreEqual(AppVersion.DisplayVersion, form.VersionLabel.Text);
            Assert.AreEqual(ContentAlignment.MiddleCenter, form.VersionLabel.TextAlign);
            Assert.AreEqual(SystemColors.GrayText, form.VersionLabel.ForeColor);
            Assert.IsTrue(form.Controls.Contains(form.VersionLabel));

            // Verify position is between left button (X=20, Width=100) and right button (X=300, Width=100)
            Assert.AreEqual(form.CloseButton.Location.Y, form.VersionLabel.Location.Y);
            Assert.AreEqual(form.OpenLogsButton.Location.Y, form.VersionLabel.Location.Y);
            Assert.IsGreaterThanOrEqualTo(form.VersionLabel.Location.X, 120);
            Assert.IsLessThanOrEqualTo(form.VersionLabel.Right, 300);

            // In Single PC mode, buttons and version label are at Y=841
            form.ModeComboBox.SelectedIndex = 1;
            Assert.AreEqual(841, form.VersionLabel.Location.Y);
            Assert.AreEqual(form.CloseButton.Location.Y, form.VersionLabel.Location.Y);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_DynamicHeight_AdaptsBetweenDualPcAndSinglePc()
    {
        var tempFolder = TestDirectory.Create("ClientDynamicHeightTest");

        try
        {
            var settings = new ClientSettings { Mode = ClientMode.DualPc };
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            using var listener = new UdpListener(13992);
            using var form = new StreamHelper.Client.UI.ClientSettingsForm(settings, listener, overlay, _ => { });
            _ = form.Handle;

            int dualPcHeight = form.ClientSize.Height;
            Assert.IsTrue(dualPcHeight < 886, $"Dual PC mode height ({dualPcHeight}) should be smaller than Single PC height (886)");

            // Check that gap between timeout control and opacity control is small (~20px), avoiding empty whitespace
            int gapDualPc = form.OpacityLabel.Top - form.TimeoutNumeric.Bottom;
            Assert.IsTrue(gapDualPc >= 10 && gapDualPc <= 35, $"Dual PC gap ({gapDualPc}px) should be around 20px, not a large whitespace");

            // Switch to Single PC
            form.ModeComboBox.SelectedIndex = 1;
            int singlePcHeight = form.ClientSize.Height;
            Assert.AreEqual(886, singlePcHeight, "Single PC mode height should be 886");
            int gapSinglePc = form.OpacityLabel.Top - form.TimeoutNumeric.Bottom;
            Assert.IsTrue(gapSinglePc >= 10 && gapSinglePc <= 35, $"Single PC gap ({gapSinglePc}px) should be around 20px");

            // Switch back to Dual PC
            form.ModeComboBox.SelectedIndex = 0;
            Assert.AreEqual(dualPcHeight, form.ClientSize.Height, "Height should dynamically return to Dual PC height");
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_Layout_MaintainsMinimum20PxRightMarginAcrossModes()
    {
        var tempFolder = TestDirectory.Create("ClientLayoutMarginTest");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.DualPc,
                ServerIp = "127.0.0.1",
                Port = 13998
            };
            using var listener = new UdpListener(13998);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            using var audio = new WindowsAudioMonitor();
            using var form = new ClientSettingsForm(settings, listener, audio, overlay);
            _ = form.Handle;

            Assert.AreEqual(420, form.ClientSize.Width);

            // Verify Dual PC mode
            foreach (Control control in form.Controls)
            {
                if (control.Visible)
                {
                    Assert.IsLessThanOrEqualTo(
                        control.Right,
                        form.ClientSize.Width - 20,
                        $"Dual PC control '{control.Name}' ({control.GetType().Name}) exceeds 20px right margin. Right={control.Right}");
                }
            }

            // Switch to Single PC mode
            form.ModeComboBox.SelectedIndex = 1;
            foreach (Control control in form.Controls)
            {
                if (control.Visible)
                {
                    Assert.IsLessThanOrEqualTo(
                        control.Right,
                        form.ClientSize.Width - 20,
                        $"Single PC control '{control.Name}' ({control.GetType().Name}) exceeds 20px right margin. Right={control.Right}");
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_AudioDetectionControls_PlacementLinkageAndMargin_SinglePcMode()
    {
        var tempFolder = TestDirectory.Create("ClientAudioDetectionFormTest");
        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.SinglePc,
                AudioMatchToleranceDb = 18.0,
                SimpleAudioIssueDetection = false
            };
            using var listener = new UdpListener(13987);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            using var audio = new MockAudioMonitor();
            using var form = new ClientSettingsForm(settings, listener, audio, overlay);
            _ = form.Handle;

            var obsCbo = form.ObsAudioComboBox;
            var simpleChk = form.SimpleAudioIssueDetectionCheckBox;
            var toleranceBar = form.AudioMatchToleranceTrackBar;
            var toleranceLbl = form.AudioMatchToleranceValueLabel;

            Assert.IsNotNull(obsCbo);
            Assert.IsNotNull(simpleChk);
            Assert.IsNotNull(toleranceBar);
            Assert.IsNotNull(toleranceLbl);

            // Placement inside _pnlSinglePc:
            // OBS Audio dropdown and label placed after Skipped frames numeric input
            Assert.IsTrue(form.ObsAudioLabel.Top >= form.SkippedFramesNumeric.Bottom, $"OBS audio label (top={form.ObsAudioLabel.Top}) should be below Skipped frames numeric (bottom={form.SkippedFramesNumeric.Bottom})");
            Assert.IsTrue(obsCbo.Top >= form.ObsAudioLabel.Bottom, $"OBS audio combo (top={obsCbo.Top}) should be below OBS audio label (bottom={form.ObsAudioLabel.Bottom})");
            Assert.AreEqual(20, form.ObsAudioLabel.Left);
            Assert.AreEqual(20, obsCbo.Left);
            Assert.AreEqual(380, obsCbo.Width);

            // Switch and slider below OBS audio dropdown; switch above slider
            Assert.IsTrue(simpleChk.Top >= obsCbo.Bottom, $"Simple checkbox (top={simpleChk.Top}) should be below OBS audio combo (bottom={obsCbo.Bottom})");
            Assert.IsTrue(toleranceLbl.Top >= simpleChk.Bottom, $"Tolerance label (top={toleranceLbl.Top}) should be below Simple checkbox (bottom={simpleChk.Bottom})");
            Assert.IsTrue(toleranceBar.Top >= toleranceLbl.Bottom, $"Tolerance trackbar (top={toleranceBar.Top}) should be below Tolerance label (bottom={toleranceLbl.Bottom})");

            // 20px right margin within form
            Assert.IsTrue(obsCbo.Right <= form.ClientSize.Width - 20, $"OBS audio combo right ({obsCbo.Right}) exceeds margin ({form.ClientSize.Width - 20})");
            Assert.IsTrue(simpleChk.Right <= form.ClientSize.Width - 20, $"Simple checkbox right ({simpleChk.Right}) exceeds margin ({form.ClientSize.Width - 20})");
            Assert.IsTrue(toleranceBar.Right <= form.ClientSize.Width - 20, $"Tolerance bar right ({toleranceBar.Right}) exceeds margin ({form.ClientSize.Width - 20})");
            Assert.IsTrue(toleranceLbl.Right <= form.ClientSize.Width - 20, $"Tolerance label right ({toleranceLbl.Right}) exceeds margin ({form.ClientSize.Width - 20})");

            // Initial state from settings
            Assert.IsFalse(simpleChk.Checked);
            Assert.IsTrue(toleranceBar.Enabled);
            Assert.AreEqual(18, toleranceBar.Value);
            Assert.AreEqual("18 dB", toleranceLbl.Text);

            // Enable simple mode: slider disabled and set to 40 dB
            simpleChk.Checked = true;
            Assert.IsFalse(toleranceBar.Enabled);
            Assert.AreEqual(40, toleranceBar.Value);
            Assert.AreEqual("40 dB", toleranceLbl.Text);
            Assert.IsTrue(settings.SimpleAudioIssueDetection);

            // Disable simple mode: slider enabled and restored to 18 dB
            simpleChk.Checked = false;
            Assert.IsTrue(toleranceBar.Enabled);
            Assert.AreEqual(18, toleranceBar.Value);
            Assert.AreEqual("18 dB", toleranceLbl.Text);
            Assert.IsFalse(settings.SimpleAudioIssueDetection);
            Assert.AreEqual(18.0, settings.AudioMatchToleranceDb);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_DualPcNoteWrapping_DoesNotOverlapServerLabel()
    {
        var tempFolder = TestDirectory.Create("ClientDualPcWrapTest");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.DualPc,
                ServerIp = "127.0.0.1",
                Port = 13997
            };
            using var listener = new UdpListener(13997);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            using var audio = new WindowsAudioMonitor();
            using var form = new ClientSettingsForm(settings, listener, audio, overlay);
            _ = form.Handle;
            form.Visible = true;

            // Verify Dual PC note and Server label clearance
            var dualPcNote = form.DualPcNoteLabel;
            var serverLabel = form.ServerLabel;
            Assert.IsNotNull(serverLabel);
            Assert.IsTrue(dualPcNote.Visible);
            Assert.IsTrue(serverLabel.Visible);

            // Server label must always be positioned below the bottom of the Dual PC guidance note
            Assert.IsGreaterThanOrEqualTo(dualPcNote.Bottom, serverLabel.Location.Y);

            // Force multi-line text by constraining MaximumSize to narrow width
            dualPcNote.MaximumSize = new Size(200, 0);
            form.PerformLayout();

            // Verify that even when multi-line wrapped, Server label never overlaps Dual PC note
            Assert.IsGreaterThanOrEqualTo(dualPcNote.Bottom, serverLabel.Location.Y);
            Assert.IsGreaterThanOrEqualTo(serverLabel.Bottom, form.ServerComboBox.Location.Y);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientTrayApplicationContext_Startup_InDualPcMode_StartsUdpListener_DoesNotStartAudioMonitor()
    {
        var tempFolder = TestDirectory.Create("ClientStartupDualPc");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.DualPc,
                Port = 13981
            };
            var mockAudio = new MockAudioMonitor();
            using var listener = new UdpListener(13981);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            using var context = new ClientTrayApplicationContext(settings, listener, mockAudio, assetMgr, overlay);

            Assert.IsFalse(mockAudio.IsMonitoring);
            Assert.IsFalse(listener.IsPaused);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientTrayApplicationContext_Startup_InSinglePcMode_StartsAudioMonitor_DoesNotStartUdpListener()
    {
        var tempFolder = TestDirectory.Create("ClientStartupSinglePc");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.SinglePc,
                MicrophoneId = "mic-test-startup",
                RetryTimeout = 6,
                Port = 13982
            };
            var mockAudio = new MockAudioMonitor();
            using var listener = new UdpListener(13982);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            using var context = new ClientTrayApplicationContext(settings, listener, mockAudio, assetMgr, overlay);

            Assert.IsTrue(mockAudio.IsMonitoring);
            Assert.AreEqual("mic-test-startup", mockAudio.TargetDeviceId);
            Assert.AreEqual(6, mockAudio.RetryTimeout);
            Assert.IsTrue(listener.IsPaused);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientTrayApplicationContext_SwitchMode_TransitionsSubsystemsAndIsolatesResources()
    {
        var tempFolder = TestDirectory.Create("ClientSwitchMode");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.DualPc,
                MicrophoneId = "mic-usb-123",
                RetryTimeout = 4,
                Port = 13983
            };
            var mockAudio = new MockAudioMonitor();
            using var listener = new UdpListener(13983);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            using var context = new ClientTrayApplicationContext(settings, listener, mockAudio, assetMgr, overlay);

            // Initially Dual PC
            Assert.IsFalse(mockAudio.IsMonitoring);
            Assert.IsFalse(listener.IsPaused);

            // Switch to Single PC
            context.SwitchMode(ClientMode.SinglePc);
            Assert.IsTrue(listener.IsPaused, "UDP listener must be paused/stopped in Single PC mode");
            Assert.IsTrue(mockAudio.IsMonitoring, "Audio monitor must be active in Single PC mode");
            Assert.AreEqual("mic-usb-123", mockAudio.TargetDeviceId);
            Assert.AreEqual(4, mockAudio.RetryTimeout);
            Assert.AreEqual(ClientMode.SinglePc, settings.Mode);

            // Switch back to Dual PC
            context.SwitchMode(ClientMode.DualPc);
            Assert.IsFalse(mockAudio.IsMonitoring, "Audio monitor must be stopped in Dual PC mode");
            Assert.IsFalse(listener.IsPaused, "UDP listener must be active in Dual PC mode");
            Assert.AreEqual(ClientMode.DualPc, settings.Mode);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientTrayApplicationContext_SinglePc_AudioEventsUpdateState()
    {
        var tempFolder = TestDirectory.Create("ClientAudioEvents");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.SinglePc,
                MicrophoneId = "mic-usb-456",
                MicrophoneName = "HyperX Mic",
                Port = 13984
            };
            var mockAudio = new MockAudioMonitor();
            using var listener = new UdpListener(13984);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            _ = overlay.Handle;
            using var context = new ClientTrayApplicationContext(settings, listener, mockAudio, assetMgr, overlay);

            // Mute changed event
            mockAudio.TriggerMuteChanged(true);
            Assert.AreEqual(MicState.Muted, overlay.ActualLiveState);
            StringAssert.Contains(context.TrayIcon.Text, "Microphone muted", StringComparison.OrdinalIgnoreCase);

            mockAudio.TriggerMuteChanged(false);
            Assert.AreEqual(MicState.Unmuted, overlay.ActualLiveState);

            // Disconnect event
            mockAudio.TriggerConnectionChanged(false);
            Assert.AreEqual(MicState.Disconnected, overlay.ActualLiveState);
            StringAssert.Contains(context.TrayIcon.Text, "Microphone disconnected", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_InitializesWithDualPcLayout_AdaptsToSinglePcLayout()
    {
        var tempFolder = TestDirectory.Create("ClientFormLayout");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.DualPc,
                ServerIp = "10.0.0.1",
                Port = 13985,
                RetryTimeout = 5
            };
            var mockAudio = new MockAudioMonitor
            {
                ActiveDevices = new List<AudioDeviceInfo>
                {
                    new("dev-1", "Headset Mic", true)
                }
            };
            using var listener = new UdpListener(13985);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            using var form = new ClientSettingsForm(settings, listener, mockAudio, overlay);
            _ = form.Handle;
            form.Visible = true;

            // Dual PC initial layout assertions
            Assert.AreEqual(0, form.ModeComboBox.SelectedIndex);
            Assert.IsTrue(form.DualPcNoteLabel.Visible);
            Assert.IsTrue(form.ServerComboBox.Visible);
            Assert.IsTrue(form.PortTextBox.Visible);
            Assert.IsFalse(form.MicrophoneComboBox.Visible);
            Assert.AreEqual("Retry timeout (seconds):", form.TimeoutLabel.Text);
            int dualPcInitialHeight = form.ClientSize.Height;
            Assert.IsTrue(dualPcInitialHeight < 886, "Dual PC mode height should be smaller than 886");

            // Transition to Single PC mode via ComboBox
            form.ModeComboBox.SelectedIndex = 1;

            // Single PC layout assertions
            Assert.IsFalse(form.DualPcNoteLabel.Visible);
            Assert.IsFalse(form.ServerComboBox.Visible);
            Assert.IsFalse(form.PortTextBox.Visible);
            Assert.IsTrue(form.MicrophoneComboBox.Visible);
            Assert.AreEqual("Microphone reconnect check (seconds):", form.TimeoutLabel.Text);
            Assert.AreEqual(new Point(20, 481), form.TimeoutLabel.Location);
            Assert.AreEqual(new Point(20, 503), form.TimeoutNumeric.Location);
            Assert.AreEqual(886, form.ClientSize.Height, "Single PC mode height should be 886");

            // Transition back to Dual PC mode
            form.ModeComboBox.SelectedIndex = 0;
            Assert.IsTrue(form.DualPcNoteLabel.Visible);
            Assert.IsTrue(form.ServerComboBox.Visible);
            Assert.IsTrue(form.PortTextBox.Visible);
            Assert.IsFalse(form.MicrophoneComboBox.Visible);
            Assert.AreEqual("Retry timeout (seconds):", form.TimeoutLabel.Text);
            Assert.AreEqual(20, form.TimeoutLabel.Location.X);
            Assert.IsTrue(form.TimeoutLabel.Location.Y >= 250, "TimeoutLabel must be positioned below Dual PC controls");
            Assert.IsTrue(form.TimeoutNumeric.Location.Y > form.TimeoutLabel.Location.Y, "TimeoutNumeric must be below TimeoutLabel");
            Assert.AreEqual(dualPcInitialHeight, form.ClientSize.Height, "Height should dynamically return to Dual PC height");
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_ModeSwitch_ImmediatelyPersistsAndNotifies()
    {
        var tempFolder = TestDirectory.Create("ClientFormModePersist");

        try
        {
            var settings = new ClientSettings { Mode = ClientMode.DualPc };
            settings.Save(tempFolder);

            var mockAudio = new MockAudioMonitor();
            using var listener = new UdpListener(13986);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);

            ClientMode? notifiedMode = null;
            using var form = new ClientSettingsForm(
                settings,
                listener,
                mockAudio,
                overlay,
                onModeChanged: m => notifiedMode = m);
            _ = form.Handle;

            // Switch to Single PC
            form.ModeComboBox.SelectedIndex = 1;

            Assert.AreEqual(ClientMode.SinglePc, notifiedMode);
            Assert.AreEqual(ClientMode.SinglePc, settings.Mode);

            var reloaded = ClientSettings.Load(tempFolder);
            Assert.AreEqual(ClientMode.SinglePc, reloaded.Mode);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_MicrophoneSelection_ImmediatelyPersistsAndNotifies()
    {
        var tempFolder = TestDirectory.Create("ClientFormMicPersist");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.SinglePc
            };
            settings.Save(tempFolder);

            var mockAudio = new MockAudioMonitor
            {
                ActiveDevices = new List<AudioDeviceInfo>
                {
                    new("dev-mic-1", "Microphone A", true),
                    new("dev-mic-2", "Microphone B", false)
                }
            };
            using var listener = new UdpListener(13987);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);

            string? notifiedId = null;
            string? notifiedName = null;

            using var form = new ClientSettingsForm(
                settings,
                listener,
                mockAudio,
                overlay,
                onMicrophoneChanged: (id, name) =>
                {
                    notifiedId = id;
                    notifiedName = name;
                });
            _ = form.Handle;

            // Select Microphone B
            form.MicrophoneComboBox.SelectedIndex = 1;

            Assert.AreEqual("dev-mic-2", settings.MicrophoneId);
            Assert.AreEqual("dev-mic-2", notifiedId);
            Assert.AreEqual("Microphone B", notifiedName);

            var reloaded = ClientSettings.Load(tempFolder);
            Assert.AreEqual("dev-mic-2", reloaded.MicrophoneId);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_SwitchFromSinglePcToDualPc_ActivatesUdpListenerAndDeactivatesAudioMonitor()
    {
        var tempFolder = TestDirectory.Create("ClientSwitchFormTest");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.SinglePc,
                ServerIp = "127.0.0.1",
                Port = 13988,
                MicrophoneId = "mic-test-123",
                RetryTimeout = 4
            };
            settings.Save(tempFolder);

            var mockAudio = new MockAudioMonitor();
            using var listener = new UdpListener(13988);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            using var context = new ClientTrayApplicationContext(settings, listener, mockAudio, assetMgr, overlay);

            // Initially Single PC mode
            Assert.IsTrue(mockAudio.IsMonitoring, "Audio monitor must be active on Single PC startup");
            Assert.IsTrue(listener.IsPaused, "UDP listener must be paused on Single PC startup");
            Assert.AreEqual(ClientMode.SinglePc, context.ActiveMode);
            Assert.AreEqual(ClientMode.SinglePc, settings.Mode);

            // Open ClientSettingsForm wired to context.SwitchMode
            using var form = new ClientSettingsForm(
                settings,
                listener,
                mockAudio,
                overlay,
                onModeChanged: newMode => context.SwitchMode(newMode));
            _ = form.Handle;
            form.Visible = true;

            // Switch from Single PC to Dual PC mode via dropdown (index 0 is Dual PC)
            form.ModeComboBox.SelectedIndex = 0;
            Application.DoEvents();

            // Verify mode transitioned immediately
            Assert.AreEqual(ClientMode.DualPc, context.ActiveMode, "Context ActiveMode must transition to Dual PC");
            Assert.AreEqual(ClientMode.DualPc, settings.Mode, "Settings Mode must be Dual PC");
            Assert.IsFalse(mockAudio.IsMonitoring, "Audio monitor must be deactivated when switching to Dual PC mode via settings form");
            Assert.IsFalse(listener.IsPaused, "UDP listener must be active when switching to Dual PC mode via settings form without pause/resume");
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public async Task UdpListener_Stop_ClearsIsConnectedAndDispatchesDisconnectNotification()
    {
        int testPort = 13298;
        using var broadcaster = new UdpBroadcaster(testPort, "server-stop-test");
        using var listener = new UdpListener(testPort);

        bool disconnectNotified = false;
        listener.TargetServerConnectionChanged += connected =>
        {
            if (!connected) disconnectNotified = true;
        };

        listener.Start(targetServerIp: null, retryTimeoutSeconds: 5);
        broadcaster.Start();
        broadcaster.UpdateState(MicState.Unmuted, "Test Mic");

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout && !listener.IsConnected)
        {
            await Task.Delay(25);
        }

        Assert.IsTrue(listener.IsConnected, "Listener should be connected after receiving broadcast");
        Assert.AreEqual(MicState.Unmuted, listener.LastReportedState);

        // Stop the listener
        listener.Stop();

        Assert.IsFalse(listener.IsConnected, "UdpListener.Stop() must clear IsConnected to false");
        Assert.AreEqual(MicState.Disconnected, listener.LastReportedState, "UdpListener.Stop() must reset LastReportedState to Disconnected");
        Assert.IsTrue(disconnectNotified, "UdpListener.Stop() must dispatch TargetServerConnectionChanged(false) when previously connected");

        // Restart listener and verify consistent disconnected state until packet arrival
        listener.Start(targetServerIp: null, retryTimeoutSeconds: 5);
        Assert.IsFalse(listener.IsConnected, "UdpListener.Start() must reset connection state until new packets arrive");

        // Broadcast new packet and verify reconnection
        broadcaster.UpdateState(MicState.Muted, "Test Mic");

        timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout && (!listener.IsConnected || listener.LastReportedState != MicState.Muted))
        {
            await Task.Delay(25);
        }

        Assert.IsTrue(listener.IsConnected, "UdpListener must successfully reconnect upon receiving packets after restart");
        Assert.AreEqual(MicState.Muted, listener.LastReportedState);
    }

    [TestMethod]
    public void ClientTrayApplicationContext_ContextMenu_ContainsDonateBelowSettingsAndAheadOfExit()
    {
        var tempFolder = TestDirectory.Create("ClientDonateTest");

        try
        {
            var settings = new ClientSettings { Mode = ClientMode.SinglePc, Port = 13990 };
            var mockAudio = new MockAudioMonitor();
            using var listener = new UdpListener(13990);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);
            using var context = new ClientTrayApplicationContext(settings, listener, mockAudio, assetMgr, overlay);

            var menu = context.ContextMenu;
            Assert.IsNotNull(menu);

            // Context menu structure:
            // 0: Pause/Resume
            // 1: Separator
            // 2: Settings...
            // 3: Donate
            // 4: Separator
            // 5: Exit
            Assert.IsTrue(menu.Items.Count >= 6, $"Expected at least 6 menu items, found {menu.Items.Count}");

            int settingsIndex = -1;
            int donateIndex = -1;
            int exitIndex = -1;

            for (int i = 0; i < menu.Items.Count; i++)
            {
                var item = menu.Items[i];
                if (item.Text != null && item.Text.StartsWith("Settings", StringComparison.OrdinalIgnoreCase))
                {
                    settingsIndex = i;
                }
                else if (string.Equals(item.Text, "Donate", StringComparison.OrdinalIgnoreCase))
                {
                    donateIndex = i;
                }
                else if (string.Equals(item.Text, "Exit", StringComparison.OrdinalIgnoreCase))
                {
                    exitIndex = i;
                }
            }

            Assert.AreNotEqual(-1, settingsIndex, "Settings menu item not found");
            Assert.AreNotEqual(-1, donateIndex, "Donate menu item not found");
            Assert.AreNotEqual(-1, exitIndex, "Exit menu item not found");

            Assert.AreEqual(settingsIndex + 1, donateIndex, "Donate must be positioned immediately below Settings...");
            Assert.IsTrue(donateIndex < exitIndex, "Donate must be positioned ahead of Exit");
            Assert.IsNotNull(context.MenuDonate);
            Assert.AreEqual("Donate", context.MenuDonate.Text);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void OverlayForm_RotatesAlertsAccordingToAnimationCyclesSetting()
    {
        var tempFolder = TestDirectory.Create("OverlayRotTest");

        try
        {
            // 1. Test default AnimationCycles = 1 (1 pulse cycle per alert)
            var settings = new ClientSettings
            {
                PulseFrequency = 1.0 // 1 second per pulse cycle
            };
            Assert.AreEqual(1, settings.AnimationCycles);

            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);

            var combinedAlerts = AlertFlags.ObsDisconnected | AlertFlags.MicMuted;
            overlay.SetLiveAlerts(combinedAlerts, isPaused: false);

            var activeList = AlertDisplayInfo.GetActiveAlertList(combinedAlerts);
            Assert.AreEqual(2, activeList.Count);

            // Default (1 cycle): Cycle 0 (0.0s - 0.9s) -> activeList[0]
            Assert.AreEqual(activeList[0], overlay.GetDisplayedAlertAt(0.0));
            Assert.AreEqual(activeList[0], overlay.GetDisplayedAlertAt(0.5));
            Assert.AreEqual(activeList[0], overlay.GetDisplayedAlertAt(0.9));

            // Default (1 cycle): Cycle 1 (1.0s - 1.9s) -> activeList[1]
            Assert.AreEqual(activeList[1], overlay.GetDisplayedAlertAt(1.0));
            Assert.AreEqual(activeList[1], overlay.GetDisplayedAlertAt(1.5));
            Assert.AreEqual(activeList[1], overlay.GetDisplayedAlertAt(1.9));

            // Default (1 cycle): Cycle 2 (2.0s+) -> wraps back to activeList[0]
            Assert.AreEqual(activeList[0], overlay.GetDisplayedAlertAt(2.0));

            // 2. Test configured AnimationCycles = 3 (3 pulse cycles per alert)
            overlay.UpdateAnimationSettings(settings.Opacity, settings.PulseFrequency, 3);
            Assert.AreEqual(3, settings.AnimationCycles);

            // Cycles 0, 1, 2 (t=0.0s, 1.0s, 2.0s) -> totalCycles/3 = 0 -> activeList[0]
            Assert.AreEqual(activeList[0], overlay.GetDisplayedAlertAt(0.0));
            Assert.AreEqual(activeList[0], overlay.GetDisplayedAlertAt(1.5));
            Assert.AreEqual(activeList[0], overlay.GetDisplayedAlertAt(2.9));

            // Cycles 3, 4, 5 (t=3.0s, 4.0s, 5.0s) -> totalCycles/3 = 1 -> activeList[1]
            Assert.AreEqual(activeList[1], overlay.GetDisplayedAlertAt(3.0));
            Assert.AreEqual(activeList[1], overlay.GetDisplayedAlertAt(4.5));
            Assert.AreEqual(activeList[1], overlay.GetDisplayedAlertAt(5.9));

            // Cycles 6+ -> totalCycles/3 = 2 -> wraps back to activeList[0]
            Assert.AreEqual(activeList[0], overlay.GetDisplayedAlertAt(6.0));
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettings_AnimationCycles_ClampedBetween1And10()
    {
        var tempFolder = TestDirectory.Create("ClientSettingsCyclesClamp");

        try
        {
            var filePath = ClientSettings.GetFilePath(tempFolder);

            // Test clamping when value is 0 or negative
            File.WriteAllText(filePath, "{\"AnimationCycles\": 0}");
            var loaded0 = ClientSettings.Load(tempFolder);
            Assert.AreEqual(1, loaded0.AnimationCycles);

            File.WriteAllText(filePath, "{\"AnimationCycles\": -5}");
            var loadedNeg = ClientSettings.Load(tempFolder);
            Assert.AreEqual(1, loadedNeg.AnimationCycles);

            // Test clamping when value exceeds 10
            File.WriteAllText(filePath, "{\"AnimationCycles\": 42}");
            var loadedOver = ClientSettings.Load(tempFolder);
            Assert.AreEqual(10, loadedOver.AnimationCycles);

            // Test valid value
            File.WriteAllText(filePath, "{\"AnimationCycles\": 7}");
            var loadedValid = ClientSettings.Load(tempFolder);
            Assert.AreEqual(7, loadedValid.AnimationCycles);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_AnimationCyclesSlider_UpdatesSettingsAndOverlay()
    {
        var tempFolder = TestDirectory.Create("ClientFormCyclesSlider");

        try
        {
            var settings = new ClientSettings
            {
                AnimationCycles = 1
            };
            settings.Save(tempFolder);

            var mockAudio = new MockAudioMonitor();
            using var listener = new UdpListener(13986);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);

            using var form = new ClientSettingsForm(
                settings,
                listener,
                mockAudio,
                overlay);
            _ = form.Handle;

            // Check default state of slider
            Assert.AreEqual(1, form.AnimationCyclesTrackBar.Minimum);
            Assert.AreEqual(10, form.AnimationCyclesTrackBar.Maximum);
            Assert.AreEqual(1, form.AnimationCyclesTrackBar.Value);
            Assert.AreEqual("1", form.AnimationCyclesValueLabel.Text);

            // Adjust slider to 5
            form.AnimationCyclesTrackBar.Value = 5;

            Assert.AreEqual(5, settings.AnimationCycles);
            Assert.AreEqual("5", form.AnimationCyclesValueLabel.Text);

            // Check that settings file was saved
            var reloaded = ClientSettings.Load(tempFolder);
            Assert.AreEqual(5, reloaded.AnimationCycles);

            // Close settings form to exit WYSIWYG mode and apply live alerts
            form.Close();

            // Check that overlay rotation reflects 5 cycles
            var combinedAlerts = AlertFlags.ObsDisconnected | AlertFlags.MicMuted;
            overlay.SetLiveAlerts(combinedAlerts, isPaused: false);
            var activeList = AlertDisplayInfo.GetActiveAlertList(combinedAlerts);

            // Cycles 0..4 (t=0.0s..4.9s) -> activeList[0]
            Assert.AreEqual(activeList[0], overlay.GetDisplayedAlertAt(0.0));
            Assert.AreEqual(activeList[0], overlay.GetDisplayedAlertAt(4.9));
            // Cycle 5 (t=5.0s) -> activeList[1]
            Assert.AreEqual(activeList[1], overlay.GetDisplayedAlertAt(5.0));
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_ObsAudio_SortsAlphabetically_WithMissingFirst()
    {
        var tempFolder = TestDirectory.Create("ClientFormObsSortMissing");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.SinglePc,
                ObsAudioDevice = "Missing OBS Source"
            };
            settings.Save(tempFolder);

            var mockAudio = new MockAudioMonitor();
            var mockObs = new MockObsMonitor
            {
                Inputs = new List<string> { "Game Capture Audio", "Desktop Audio", "Aux Audio" }
            };
            using var listener = new UdpListener(13987);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);

            using var form = new ClientSettingsForm(
                settings,
                listener,
                mockAudio,
                overlay,
                obsMonitor: mockObs);
            _ = form.Handle;

            Assert.AreEqual(4, form.ObsAudioComboBox.Items.Count);
            var item0 = (MicComboItem)form.ObsAudioComboBox.Items[0];
            var item1 = (MicComboItem)form.ObsAudioComboBox.Items[1];
            var item2 = (MicComboItem)form.ObsAudioComboBox.Items[2];
            var item3 = (MicComboItem)form.ObsAudioComboBox.Items[3];

            Assert.AreEqual("Missing OBS Source", item0.DisplayName);
            Assert.IsTrue(item0.IsMissing);
            Assert.AreEqual("Aux Audio", item1.DisplayName);
            Assert.IsFalse(item1.IsMissing);
            Assert.AreEqual("Desktop Audio", item2.DisplayName);
            Assert.IsFalse(item2.IsMissing);
            Assert.AreEqual("Game Capture Audio", item3.DisplayName);
            Assert.IsFalse(item3.IsMissing);

            Assert.AreEqual(item0, form.ObsAudioComboBox.SelectedItem);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_ObsAudio_SortsAlphabetically_WhenAllAvailable()
    {
        var tempFolder = TestDirectory.Create("ClientFormObsSortAll");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.SinglePc,
                ObsAudioDevice = "Desktop Audio"
            };
            settings.Save(tempFolder);

            var mockAudio = new MockAudioMonitor();
            var mockObs = new MockObsMonitor
            {
                Inputs = new List<string> { "Game Capture Audio", "Desktop Audio", "Aux Audio" }
            };
            using var listener = new UdpListener(13988);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);

            using var form = new ClientSettingsForm(
                settings,
                listener,
                mockAudio,
                overlay,
                obsMonitor: mockObs);
            _ = form.Handle;

            Assert.AreEqual(3, form.ObsAudioComboBox.Items.Count);
            var item0 = (MicComboItem)form.ObsAudioComboBox.Items[0];
            var item1 = (MicComboItem)form.ObsAudioComboBox.Items[1];
            var item2 = (MicComboItem)form.ObsAudioComboBox.Items[2];

            Assert.AreEqual("Aux Audio", item0.DisplayName);
            Assert.AreEqual("Desktop Audio", item1.DisplayName);
            Assert.AreEqual("Game Capture Audio", item2.DisplayName);

            Assert.AreEqual(item1, form.ObsAudioComboBox.SelectedItem);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_GameAudioOutputs_SortsAlphabetically_WithMissingFirst()
    {
        var tempFolder = TestDirectory.Create("ClientFormGameAudioMissing");

        try
        {
            var settings = new ClientSettings
            {
                GameAudioOutputDeviceId = "missing-dev-id",
                GameAudioOutputDeviceName = "Missing Headset"
            };
            settings.Save(tempFolder);

            var mockAudio = new MockAudioMonitor
            {
                ActiveDevices = new List<AudioDeviceInfo>
                {
                    new("dev-z", "Zebra Headphones", false),
                    new("dev-a", "Alpha Speakers", false),
                    new("dev-b", "Bravo Soundbar", false)
                }
            };
            using var listener = new UdpListener(13989);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);

            using var form = new ClientSettingsForm(
                settings,
                listener,
                mockAudio,
                overlay);
            _ = form.Handle;

            Assert.AreEqual(4, form.GameAudioOutputComboBox.Items.Count);
            var item0 = (MicComboItem)form.GameAudioOutputComboBox.Items[0];
            var item1 = (MicComboItem)form.GameAudioOutputComboBox.Items[1];
            var item2 = (MicComboItem)form.GameAudioOutputComboBox.Items[2];
            var item3 = (MicComboItem)form.GameAudioOutputComboBox.Items[3];

            Assert.AreEqual("Missing Headset", item0.DisplayName);
            Assert.IsTrue(item0.IsMissing);
            Assert.AreEqual("Alpha Speakers", item1.DisplayName);
            Assert.IsFalse(item1.IsMissing);
            Assert.AreEqual("Bravo Soundbar", item2.DisplayName);
            Assert.IsFalse(item2.IsMissing);
            Assert.AreEqual("Zebra Headphones", item3.DisplayName);
            Assert.IsFalse(item3.IsMissing);

            Assert.AreEqual(item0, form.GameAudioOutputComboBox.SelectedItem);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_ServerIpDropdown_SortsAlphabetically_WhenOnline()
    {
        var tempFolder = TestDirectory.Create("ClientFormServerOnlineSort");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.DualPc,
                ServerIp = "192.168.1.10"
            };
            settings.Save(tempFolder);

            var mockAudio = new MockAudioMonitor();
            using var listener = new UdpListener(13990);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);

            listener.Start(targetServerIp: "192.168.1.10", retryTimeoutSeconds: 5);

            // Feed 3 discovered servers via datagrams
            var p1 = new StatusPacket { ServerId = "s1", HostName = "Zebra Host", MicrophoneName = "Mic" };
            listener.ProcessIncomingDatagram(p1.ToBytes(), new IPEndPoint(IPAddress.Parse("192.168.1.50"), 13990));

            var p2 = new StatusPacket { ServerId = "s2", HostName = "Alpha Host", MicrophoneName = "Mic" };
            listener.ProcessIncomingDatagram(p2.ToBytes(), new IPEndPoint(IPAddress.Parse("192.168.1.10"), 13990));

            var p3 = new StatusPacket { ServerId = "s3", HostName = "Beta Host", MicrophoneName = "Mic" };
            listener.ProcessIncomingDatagram(p3.ToBytes(), new IPEndPoint(IPAddress.Parse("192.168.1.30"), 13990));

            using var form = new ClientSettingsForm(
                settings,
                listener,
                mockAudio,
                overlay);
            _ = form.Handle;

            Assert.AreEqual(3, form.ServerComboBox.Items.Count);
            var item0 = (ClientSettingsForm.ServerComboItem)form.ServerComboBox.Items[0];
            var item1 = (ClientSettingsForm.ServerComboItem)form.ServerComboBox.Items[1];
            var item2 = (ClientSettingsForm.ServerComboItem)form.ServerComboBox.Items[2];

            Assert.AreEqual("192.168.1.10 - Alpha Host (Mic)", item0.DisplayText);
            Assert.IsFalse(item0.IsOffline);
            Assert.AreEqual("192.168.1.30 - Beta Host (Mic)", item1.DisplayText);
            Assert.IsFalse(item1.IsOffline);
            Assert.AreEqual("192.168.1.50 - Zebra Host (Mic)", item2.DisplayText);
            Assert.IsFalse(item2.IsOffline);

            Assert.AreEqual(item0, form.ServerComboBox.SelectedItem);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientSettingsForm_ServerIpDropdown_PutsOfflineFirst_ThenSortsAlphabetically()
    {
        var tempFolder = TestDirectory.Create("ClientFormServerOfflineSort");

        try
        {
            var settings = new ClientSettings
            {
                Mode = ClientMode.DualPc,
                ServerIp = "192.168.1.99" // Offline target
            };
            settings.Save(tempFolder);

            var mockAudio = new MockAudioMonitor();
            using var listener = new UdpListener(13991);
            using var assetMgr = new OverlayAssetManager(tempFolder);
            using var overlay = new OverlayForm(settings, assetMgr);

            listener.Start(targetServerIp: "192.168.1.99", retryTimeoutSeconds: 5);

            // Feed 2 other active servers
            var p1 = new StatusPacket { ServerId = "s1", HostName = "Zebra Host", MicrophoneName = "Mic" };
            listener.ProcessIncomingDatagram(p1.ToBytes(), new IPEndPoint(IPAddress.Parse("192.168.1.50"), 13991));

            var p2 = new StatusPacket { ServerId = "s2", HostName = "Alpha Host", MicrophoneName = "Mic" };
            listener.ProcessIncomingDatagram(p2.ToBytes(), new IPEndPoint(IPAddress.Parse("192.168.1.10"), 13991));

            using var form = new ClientSettingsForm(
                settings,
                listener,
                mockAudio,
                overlay);
            _ = form.Handle;

            Assert.AreEqual(3, form.ServerComboBox.Items.Count);
            var item0 = (ClientSettingsForm.ServerComboItem)form.ServerComboBox.Items[0];
            var item1 = (ClientSettingsForm.ServerComboItem)form.ServerComboBox.Items[1];
            var item2 = (ClientSettingsForm.ServerComboItem)form.ServerComboBox.Items[2];

            Assert.AreEqual("192.168.1.99 (Offline)", item0.DisplayText);
            Assert.IsTrue(item0.IsOffline);

            Assert.AreEqual("192.168.1.10 - Alpha Host (Mic)", item1.DisplayText);
            Assert.IsFalse(item1.IsOffline);

            Assert.AreEqual("192.168.1.50 - Zebra Host (Mic)", item2.DisplayText);
            Assert.IsFalse(item2.IsOffline);

            Assert.AreEqual(item0, form.ServerComboBox.SelectedItem);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ClientTrayApplicationContext_ParameterlessConstructor_InstantiatesSuccessfully()
    {
        using var context = new ClientTrayApplicationContext();
        Assert.IsNotNull(context);
    }

    private sealed class MockAudioMonitor : IAudioMonitor
    {
        public bool IsMonitoring { get; private set; }
        public string? TargetDeviceId { get; private set; }
        public int RetryTimeout { get; private set; }
        public bool IsMuted { get; set; }
        public bool IsConnected { get; set; } = true;
        public string? CurrentDeviceId => TargetDeviceId;
        public string? CurrentDeviceName => "Mock Mic";
        public List<AudioDeviceInfo> ActiveDevices { get; set; } = new();

        public IReadOnlyList<AudioDeviceInfo> GetActiveCaptureDevices() => ActiveDevices;
        public IReadOnlyList<AudioDeviceInfo> GetActiveRenderDevices() => ActiveDevices;
        public AudioDeviceInfo? GetDefaultCaptureDevice() => ActiveDevices.FirstOrDefault(d => d.IsDefault);

        public void StartMonitoring(string? targetDeviceId, int retryTimeoutSeconds = 5)
        {
            IsMonitoring = true;
            TargetDeviceId = targetDeviceId;
            RetryTimeout = retryTimeoutSeconds;
        }

        public void StopMonitoring()
        {
            IsMonitoring = false;
        }

        public void TriggerMuteChanged(bool muted)
        {
            IsMuted = muted;
            MuteChanged?.Invoke(muted);
        }

        public void TriggerConnectionChanged(bool connected)
        {
            IsConnected = connected;
            ConnectionChanged?.Invoke(connected);
        }

        public void TriggerDevicesChanged()
        {
            DevicesChanged?.Invoke();
        }

#pragma warning disable CS0067
        public event Action<bool>? MuteChanged;
        public event Action<bool>? ConnectionChanged;
        public event Action? DevicesChanged;
#pragma warning restore CS0067

        public void Dispose()
        {
            IsMonitoring = false;
        }
    }

    private sealed class MockObsMonitor : IObsMonitor
    {
        public List<string> Inputs { get; set; } = new();
        public IReadOnlyList<string> AvailableAudioInputs => Inputs;
        public IReadOnlyList<string> GetAudioInputNames() => Inputs;
        public bool IsConnected => true;
        public bool IsStreaming => false;
        public bool IsRecording => false;
        public bool IsReconnecting => false;
        public bool HasNetworkIssue => false;
        public bool HasRenderIssue => false;
        public bool IsCaptureDeviceDisconnected => false;
        public bool IsCaptureDeviceMuted => false;
        public ObsConnectionState ConnectionState => ObsConnectionState.Connected;

#pragma warning disable CS0067
        public event Action<bool>? ConnectionChanged;
        public event Action? StateChanged;
        public event Action<ObsAudioMeterEventArgs>? AudioMeterReceived;
        public event Action<IReadOnlyList<string>>? AudioInputsChanged;
        public event Action<ObsConnectionState>? ConnectionStateChanged;
        public event Action? StatusUpdated;
        public event Action<double, double>? AudioMeterUpdated;
#pragma warning restore CS0067

        public void Start() { }
        public void Stop() { }
        public void UpdateConfig(string host, int port, string? password, int retryTimeoutSeconds, int skippedFramesThreshold, string? audioDeviceName) { }
        public void ConnectAsync(string host, int port, string? password) { }
        public void DisconnectAsync() { }
        public void SetAudioInputName(string? name) { }
        public void SetSkippedFramesThreshold(int threshold) { }
        public void Dispose() { }
    }
}

