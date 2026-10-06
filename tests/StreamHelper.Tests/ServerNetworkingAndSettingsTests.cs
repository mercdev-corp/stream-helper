using System.Net;
using System.Net.Sockets;
using StreamHelper.Server;
using StreamHelper.Server.Config;
using StreamHelper.Server.UI;
using StreamHelper.Shared.Audio;
using StreamHelper.Shared.Network;
using StreamHelper.Shared.Protocol;
using StreamHelper.Shared.UI;
using StreamHelper.Shared.Common;
using StreamHelper.Shared.Obs;

namespace StreamHelper.Tests;

[TestClass]
public sealed class ServerNetworkingAndSettingsTests
{
    [TestMethod]
    public void ServerSettings_SaveAndLoad_RoundtripsSuccessfully()
    {
        var tempFolder = TestDirectory.Create("ServerSettingsTest");

        try
        {
            var original = new ServerSettings
            {
                RunOnStartup = true,
                MicrophoneId = "mic-12345",
                MicrophoneName = "HyperX QuadCast",
                Port = 13999,
                RetryTimeout = 8,
                IsPaused = true,
                DebugLogging = true,
                SkippedFramesPeriodSeconds = 12
            };

            original.Save(tempFolder);

            var filePath = ServerSettings.GetFilePath(tempFolder);
            Assert.IsTrue(File.Exists(filePath));

            var loaded = ServerSettings.Load(tempFolder);
            Assert.AreEqual(original.RunOnStartup, loaded.RunOnStartup);
            Assert.AreEqual(original.MicrophoneId, loaded.MicrophoneId);
            Assert.AreEqual(original.MicrophoneName, loaded.MicrophoneName);
            Assert.AreEqual(original.Port, loaded.Port);
            Assert.AreEqual(original.RetryTimeout, loaded.RetryTimeout);
            Assert.AreEqual(original.IsPaused, loaded.IsPaused);
            Assert.AreEqual(original.DebugLogging, loaded.DebugLogging);
            Assert.AreEqual(original.SkippedFramesPeriodSeconds, loaded.SkippedFramesPeriodSeconds);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ServerSettings_SkippedFramesPeriod_DefaultsAndClamps()
    {
        var tempFolder = TestDirectory.Create("ServerSkippedFramesPeriodClamp");
        try
        {
            var def = new ServerSettings();
            Assert.AreEqual(5, def.SkippedFramesPeriodSeconds);

            var filePath = ServerSettings.GetFilePath(tempFolder);
            Directory.CreateDirectory(tempFolder);

            // Test non-positive falls back to default 5
            File.WriteAllText(filePath, "{\"SkippedFramesPeriodSeconds\": 0}");
            var loadedZero = ServerSettings.Load(tempFolder);
            Assert.AreEqual(5, loadedZero.SkippedFramesPeriodSeconds);

            File.WriteAllText(filePath, "{\"SkippedFramesPeriodSeconds\": -10}");
            var loadedNeg = ServerSettings.Load(tempFolder);
            Assert.AreEqual(5, loadedNeg.SkippedFramesPeriodSeconds);

            // Test clamping above maximum (300)
            File.WriteAllText(filePath, "{\"SkippedFramesPeriodSeconds\": 999}");
            var loadedMax = ServerSettings.Load(tempFolder);
            Assert.AreEqual(300, loadedMax.SkippedFramesPeriodSeconds);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ServerSettings_AudioDetection_DefaultsAndRoundtrips()
    {
        var tempFolder = TestDirectory.Create("ServerAudioDetectionSettings");
        try
        {
            var def = new ServerSettings();
            Assert.AreEqual(20.0, def.AudioMatchToleranceDb);
            Assert.IsFalse(def.SimpleAudioIssueDetection);

            var custom = new ServerSettings
            {
                AudioMatchToleranceDb = 12.0,
                SimpleAudioIssueDetection = true
            };
            custom.Save(tempFolder);

            var loaded = ServerSettings.Load(tempFolder);
            Assert.AreEqual(12.0, loaded.AudioMatchToleranceDb);
            Assert.IsTrue(loaded.SimpleAudioIssueDetection);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void ServerSettings_Load_ClampsAudioMatchToleranceDb()
    {
        var tempFolder = TestDirectory.Create("ServerAudioDetectionClamp");
        try
        {
            var filePath = ServerSettings.GetFilePath(tempFolder);
            Directory.CreateDirectory(tempFolder);

            File.WriteAllText(filePath, "{\"AudioMatchToleranceDb\": -15.5, \"SimpleAudioIssueDetection\": true}");
            var loadedMin = ServerSettings.Load(tempFolder);
            Assert.AreEqual(0.0, loadedMin.AudioMatchToleranceDb);
            Assert.IsTrue(loadedMin.SimpleAudioIssueDetection);

            File.WriteAllText(filePath, "{\"AudioMatchToleranceDb\": 55.0, \"SimpleAudioIssueDetection\": false}");
            var loadedMax = ServerSettings.Load(tempFolder);
            Assert.AreEqual(40.0, loadedMax.AudioMatchToleranceDb);
            Assert.IsFalse(loadedMax.SimpleAudioIssueDetection);
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }

    [TestMethod]
    public void NetworkUtils_DetectsBoundUdpPort()
    {
        // Bind an ephemeral UDP port
        using var testSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        testSocket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var boundPort = ((IPEndPoint)testSocket.LocalEndPoint!).Port;

        // Port should be detected as in use
        var inUse = NetworkUtils.IsUdpPortInUse(boundPort);
        Assert.IsTrue(inUse);
    }

    [TestMethod]
    public async Task UdpBroadcaster_StateChange_EmitsBurstOfThreePackets()
    {
        int testPort = 13290;
        using var broadcaster = new UdpBroadcaster(testPort);

        var packetsSent = new List<StatusPacket>();
        broadcaster.PacketSent += packet =>
        {
            lock (packetsSent)
            {
                packetsSent.Add(packet);
            }
        };

        // Trigger state transition
        broadcaster.UpdateState(MicState.Muted, "Test Mic");

        // Wait up to 3 seconds for 3 burst packets
        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < timeout)
        {
            lock (packetsSent)
            {
                if (packetsSent.Count >= 3) break;
            }
            await Task.Delay(20);
        }

        lock (packetsSent)
        {
            Assert.IsGreaterThanOrEqualTo(3, packetsSent.Count);
            Assert.IsTrue(packetsSent.All(p => p.State == MicState.Muted));
            Assert.IsTrue(packetsSent.All(p => p.Type == PacketType.StateChange));
        }
    }

    [TestMethod]
    public async Task UdpBroadcaster_SetPaused_EmitsPausedPacket()
    {
        int testPort = 13291;
        using var broadcaster = new UdpBroadcaster(testPort);

        var packetsSent = new List<StatusPacket>();
        broadcaster.PacketSent += packet =>
        {
            lock (packetsSent)
            {
                packetsSent.Add(packet);
            }
        };

        broadcaster.SetPaused(true);

        var timeout = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < timeout)
        {
            lock (packetsSent)
            {
                if (packetsSent.Any(p => p.State == MicState.Paused)) break;
            }
            await Task.Delay(20);
        }

        lock (packetsSent)
        {
            Assert.IsTrue(packetsSent.Any(p => p.State == MicState.Paused));
            Assert.IsTrue(broadcaster.IsPaused);
        }
    }

    [TestMethod]
    public void ServerSettingsForm_CanOpenAndHandleCreated_AfterIconDisposed()
    {
        // Simulate tray icon disposing the application icon
        var previousIcon = StatusIconGenerator.GetAppIcon();
        previousIcon.Dispose();

        var settings = new ServerSettings();
        using var audioMonitor = new WindowsAudioMonitor();
        using var form = new ServerSettingsForm(settings, audioMonitor, _ => {}, _ => {}, _ => {});

        _ = form.Handle;
        Assert.IsTrue(form.IsHandleCreated);
        Assert.IsNotNull(form.Icon);
        Assert.AreEqual(32, form.Icon.Width);
    }

    [TestMethod]
    public void ServerSettingsForm_DisplaysVersionLabelBetweenButtons()
    {
        var settings = new ServerSettings();
        using var audioMonitor = new WindowsAudioMonitor();
        using var form = new ServerSettingsForm(settings, audioMonitor, _ => {}, _ => {}, _ => {});
        _ = form.Handle;

        Assert.IsNotNull(form.VersionLabel);
        Assert.AreEqual(AppVersion.DisplayVersion, form.VersionLabel.Text);
        Assert.AreEqual(ContentAlignment.MiddleCenter, form.VersionLabel.TextAlign);
        Assert.AreEqual(SystemColors.GrayText, form.VersionLabel.ForeColor);
        Assert.IsTrue(form.Controls.Contains(form.VersionLabel));

        // Verify position is between left button (X=20, Width=100) and right button (X=320, Width=80) at Y=510
        Assert.AreEqual(510, form.VersionLabel.Location.Y);
        Assert.IsGreaterThanOrEqualTo(form.VersionLabel.Location.X, 120);
        Assert.IsLessThanOrEqualTo(form.VersionLabel.Right, 320);
    }

    [TestMethod]
    public void ServerSettingsForm_Layout_MaintainsMinimum20PxRightMarginForAllControls()
    {
        var settings = new ServerSettings();
        using var audioMonitor = new WindowsAudioMonitor();
        using var form = new ServerSettingsForm(settings, audioMonitor, _ => {}, _ => {}, _ => {});
        _ = form.Handle;

        Assert.AreEqual(420, form.ClientSize.Width);

        foreach (Control control in form.Controls)
        {
            if (control.Visible)
            {
                Assert.IsLessThanOrEqualTo(
                    control.Right,
                    form.ClientSize.Width - 20,
                    $"Control '{control.Name}' ({control.GetType().Name}) exceeds the 20px right margin constraint. Right={control.Right}, ClientWidth={form.ClientSize.Width}");
            }
        }
    }

    [TestMethod]
    public void ServerSettingsForm_AudioDetectionControls_PlacementLinkageAndMargin()
    {
        var settings = new ServerSettings
        {
            AudioMatchToleranceDb = 15.0,
            SimpleAudioIssueDetection = false
        };
        using var audioMonitor = new WindowsAudioMonitor();
        using var form = new ServerSettingsForm(settings, audioMonitor, _ => {}, _ => {}, _ => {});
        _ = form.Handle;

        var obsCbo = form.ObsAudioComboBox;
        var simpleChk = form.SimpleAudioIssueDetectionCheckBox;
        var toleranceBar = form.AudioMatchToleranceTrackBar;
        var toleranceLbl = form.AudioMatchToleranceValueLabel;

        Assert.IsNotNull(obsCbo);
        Assert.IsNotNull(simpleChk);
        Assert.IsNotNull(toleranceBar);
        Assert.IsNotNull(toleranceLbl);

        // Control order/placement: Switch and slider below OBS audio dropdown; switch above slider
        Assert.IsTrue(simpleChk.Top >= obsCbo.Bottom, $"Simple checkbox (top={simpleChk.Top}) should be below OBS audio combo (bottom={obsCbo.Bottom})");
        Assert.IsTrue(toleranceLbl.Top >= simpleChk.Bottom, $"Tolerance label (top={toleranceLbl.Top}) should be below Simple checkbox (bottom={simpleChk.Bottom})");
        Assert.IsTrue(toleranceBar.Top >= toleranceLbl.Bottom, $"Tolerance trackbar (top={toleranceBar.Top}) should be below Tolerance label (bottom={toleranceLbl.Bottom})");

        // 20px right margin
        Assert.IsTrue(simpleChk.Right <= form.ClientSize.Width - 20, $"Simple checkbox right ({simpleChk.Right}) exceeds margin ({form.ClientSize.Width - 20})");
        Assert.IsTrue(toleranceBar.Right <= form.ClientSize.Width - 20, $"Tolerance bar right ({toleranceBar.Right}) exceeds margin ({form.ClientSize.Width - 20})");
        Assert.IsTrue(toleranceLbl.Right <= form.ClientSize.Width - 20, $"Tolerance label right ({toleranceLbl.Right}) exceeds margin ({form.ClientSize.Width - 20})");

        // Initial state from settings
        Assert.IsFalse(simpleChk.Checked);
        Assert.IsTrue(toleranceBar.Enabled);
        Assert.AreEqual(15, toleranceBar.Value);
        Assert.AreEqual("15 dB", toleranceLbl.Text);

        // Enable simple mode: slider disabled and set to 40 dB
        simpleChk.Checked = true;
        Assert.IsFalse(toleranceBar.Enabled);
        Assert.AreEqual(40, toleranceBar.Value);
        Assert.AreEqual("40 dB", toleranceLbl.Text);
        Assert.IsTrue(settings.SimpleAudioIssueDetection);

        // Disable simple mode: slider enabled and restored to 15 dB
        simpleChk.Checked = false;
        Assert.IsTrue(toleranceBar.Enabled);
        Assert.AreEqual(15, toleranceBar.Value);
        Assert.AreEqual("15 dB", toleranceLbl.Text);
        Assert.IsFalse(settings.SimpleAudioIssueDetection);
        Assert.AreEqual(15.0, settings.AudioMatchToleranceDb);
    }

    [TestMethod]
    public void MicrophoneSelectionController_Populate_WithActiveDevice_SelectsDevice()
    {
        var fakeMonitor = new FakeAudioMonitor
        {
            Devices = new List<AudioDeviceInfo>
            {
                new("dev-1", "Microphone 1", true),
                new("dev-2", "Microphone 2", false)
            }
        };

        using var cbo = new ComboBox();
        MicComboItem? changedItem = null;
        var controller = new MicrophoneSelectionController(cbo, fakeMonitor, item => changedItem = item);

        controller.Populate("dev-2", "Microphone 2");

        Assert.AreEqual(2, cbo.Items.Count);
        Assert.IsNotNull(controller.SelectedItem);
        Assert.AreEqual("dev-2", controller.SelectedId);
        Assert.IsFalse(controller.SelectedItem.IsMissing);
    }

    [TestMethod]
    public void MicrophoneSelectionController_Populate_WithMissingDevice_InsertsMissingItemFirst()
    {
        var fakeMonitor = new FakeAudioMonitor
        {
            Devices = new List<AudioDeviceInfo>
            {
                new("dev-1", "Microphone 1", true)
            }
        };

        using var cbo = new ComboBox();
        MicComboItem? changedItem = null;
        var controller = new MicrophoneSelectionController(cbo, fakeMonitor, item => changedItem = item);

        controller.Populate("dev-lost", "Lost Microphone");

        Assert.AreEqual(2, cbo.Items.Count);
        Assert.IsNotNull(controller.SelectedItem);
        Assert.AreEqual("dev-lost", controller.SelectedId);
        Assert.IsTrue(controller.SelectedItem.IsMissing);
        Assert.AreEqual("Lost Microphone", controller.SelectedItem.DisplayName);
    }

    [TestMethod]
    public void MicrophoneSelectionController_Populate_NoSavedDevice_SelectsFirstAndTriggersCallback()
    {
        var fakeMonitor = new FakeAudioMonitor
        {
            Devices = new List<AudioDeviceInfo>
            {
                new("dev-1", "Microphone 1", true),
                new("dev-2", "Microphone 2", false)
            }
        };

        using var cbo = new ComboBox();
        MicComboItem? changedItem = null;
        var controller = new MicrophoneSelectionController(cbo, fakeMonitor, item => changedItem = item);

        controller.Populate(null, null);

        Assert.AreEqual(2, cbo.Items.Count);
        Assert.IsNotNull(controller.SelectedItem);
        Assert.AreEqual("dev-1", controller.SelectedId);
        Assert.IsNotNull(changedItem);
        Assert.AreEqual("dev-1", changedItem.Id);
    }

    [TestMethod]
    public void MicrophoneSelectionController_Populate_SortsActiveDevicesAlphabetically()
    {
        var fakeMonitor = new FakeAudioMonitor
        {
            Devices = new List<AudioDeviceInfo>
            {
                new("dev-z", "Zebra Mic", false),
                new("dev-a", "Alpha Mic", false),
                new("dev-m", "Middle Mic", false)
            }
        };

        using var cbo = new ComboBox();
        var controller = new MicrophoneSelectionController(cbo, fakeMonitor);

        controller.Populate(null, null);

        Assert.AreEqual(3, cbo.Items.Count);
        Assert.AreEqual("Alpha Mic", ((MicComboItem)cbo.Items[0]).DisplayName);
        Assert.AreEqual("Middle Mic", ((MicComboItem)cbo.Items[1]).DisplayName);
        Assert.AreEqual("Zebra Mic", ((MicComboItem)cbo.Items[2]).DisplayName);
    }

    [TestMethod]
    public void MicrophoneSelectionController_Populate_WithMissingDevice_PutsMissingFirstAndSortsActiveAlphabetically()
    {
        var fakeMonitor = new FakeAudioMonitor
        {
            Devices = new List<AudioDeviceInfo>
            {
                new("dev-z", "Zebra Mic", false),
                new("dev-a", "Alpha Mic", false)
            }
        };

        using var cbo = new ComboBox();
        var controller = new MicrophoneSelectionController(cbo, fakeMonitor);

        controller.Populate("dev-missing", "Missing Mic");

        Assert.AreEqual(3, cbo.Items.Count);
        Assert.AreEqual("Missing Mic", ((MicComboItem)cbo.Items[0]).DisplayName);
        Assert.IsTrue(((MicComboItem)cbo.Items[0]).IsMissing);
        Assert.AreEqual("Alpha Mic", ((MicComboItem)cbo.Items[1]).DisplayName);
        Assert.IsFalse(((MicComboItem)cbo.Items[1]).IsMissing);
        Assert.AreEqual("Zebra Mic", ((MicComboItem)cbo.Items[2]).DisplayName);
        Assert.IsFalse(((MicComboItem)cbo.Items[2]).IsMissing);
    }

    [TestMethod]
    public void MicrophoneSelectionController_Populate_DistinctDefaultRoles_BadgesConsoleAndCommsSeparately()
    {
        var fakeMonitor = new FakeAudioMonitor
        {
            Devices = new List<AudioDeviceInfo>
            {
                new("dev-console", "Microphone (Realtek)", IsDefault: true, IsDefaultConsole: true, IsDefaultCommunications: false),
                new("dev-comms", "Wave Cast", IsDefault: false, IsDefaultConsole: false, IsDefaultCommunications: true),
                new("dev-aux", "Line In", IsDefault: false, IsDefaultConsole: false, IsDefaultCommunications: false)
            }
        };

        using var cbo = new ComboBox();
        var controller = new MicrophoneSelectionController(cbo, fakeMonitor);

        controller.Populate(null, null);

        Assert.AreEqual(3, cbo.Items.Count);
        var item0 = (MicComboItem?)cbo.Items[0];
        var item1 = (MicComboItem?)cbo.Items[1];
        var item2 = (MicComboItem?)cbo.Items[2];
        Assert.IsNotNull(item0);
        Assert.IsNotNull(item1);
        Assert.IsNotNull(item2);

        Assert.AreEqual("dev-aux", item0.Id);
        Assert.AreEqual("Line In", item0.DisplayName);

        Assert.AreEqual("dev-console", item1.Id);
        Assert.AreEqual("Microphone (Realtek) (Default)", item1.DisplayName);

        Assert.AreEqual("dev-comms", item2.Id);
        Assert.AreEqual("Wave Cast (Default Communications)", item2.DisplayName);
    }

    [TestMethod]
    public void MicrophoneSelectionController_Populate_SameDeviceBothRoles_BadgesOnlyDefault()
    {
        var fakeMonitor = new FakeAudioMonitor
        {
            Devices = new List<AudioDeviceInfo>
            {
                new("dev-primary", "HyperX QuadCast", IsDefault: true, IsDefaultConsole: true, IsDefaultCommunications: true),
                new("dev-secondary", "Aux Mic", IsDefault: false, IsDefaultConsole: false, IsDefaultCommunications: false)
            }
        };

        using var cbo = new ComboBox();
        var controller = new MicrophoneSelectionController(cbo, fakeMonitor);

        controller.Populate(null, null);

        Assert.AreEqual(2, cbo.Items.Count);
        var item0 = (MicComboItem?)cbo.Items[0];
        var item1 = (MicComboItem?)cbo.Items[1];
        Assert.IsNotNull(item0);
        Assert.IsNotNull(item1);

        Assert.AreEqual("dev-secondary", item0.Id);
        Assert.AreEqual("Aux Mic", item0.DisplayName);

        Assert.AreEqual("dev-primary", item1.Id);
        Assert.AreEqual("HyperX QuadCast (Default)", item1.DisplayName);
    }

    [TestMethod]
    public void MicrophoneSelectionController_GetDeviceBadge_ReturnsExpectedBadges()
    {
        // Console default
        var consoleDev = new AudioDeviceInfo("c", "Console", IsDefault: true, IsDefaultConsole: true, IsDefaultCommunications: false);
        Assert.AreEqual(" (Default)", MicrophoneSelectionController.GetDeviceBadge(consoleDev));

        // Both roles
        var bothDev = new AudioDeviceInfo("b", "Both", IsDefault: true, IsDefaultConsole: true, IsDefaultCommunications: true);
        Assert.AreEqual(" (Default)", MicrophoneSelectionController.GetDeviceBadge(bothDev));

        // Comms default distinct
        var commsDev = new AudioDeviceInfo("cm", "Comms", IsDefault: false, IsDefaultConsole: false, IsDefaultCommunications: true);
        Assert.AreEqual(" (Default Communications)", MicrophoneSelectionController.GetDeviceBadge(commsDev));

        // Neither
        var plainDev = new AudioDeviceInfo("p", "Plain", IsDefault: false, IsDefaultConsole: false, IsDefaultCommunications: false);
        Assert.AreEqual("", MicrophoneSelectionController.GetDeviceBadge(plainDev));

        // Legacy IsDefault=true
        var legacyDev = new AudioDeviceInfo("l", "Legacy", IsDefault: true, IsDefaultConsole: false, IsDefaultCommunications: false);
        Assert.AreEqual(" (Default)", MicrophoneSelectionController.GetDeviceBadge(legacyDev));
    }

    [TestMethod]
    public void ServerTrayApplicationContext_ContextMenu_ContainsDonateBelowSettingsAndAheadOfExit()
    {
        var tempFolder = TestDirectory.Create("ServerDonateTest");

        try
        {
            var settings = new ServerSettings { Port = 13991, IsPaused = true };
            var fakeAudio = new FakeAudioMonitor();
            using var broadcaster = new UdpBroadcaster(13991);
            using var context = new ServerTrayApplicationContext(settings, fakeAudio, broadcaster);

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
    public void TrayAlertCycler_CyclesActiveAlertsAndFormatsMultilineTooltip()
    {
        var cycler = new TrayAlertCycler();
        cycler.SetState(AlertFlags.ObsDisconnected | AlertFlags.MicMuted, isPaused: false);

        var activeList = AlertDisplayInfo.GetActiveAlertList(AlertFlags.ObsDisconnected | AlertFlags.MicMuted);
        Assert.AreEqual(2, activeList.Count);

        string tooltip = AlertDisplayInfo.FormatTooltip(AlertFlags.ObsDisconnected | AlertFlags.MicMuted, isPaused: false);
        StringAssert.Contains(tooltip, "OBS is not running");
        StringAssert.Contains(tooltip, "Microphone muted");
    }

    [TestMethod]
    public void ServerTrayApplicationContext_ParameterlessConstructor_InstantiatesSuccessfully()
    {
        using var context = new ServerTrayApplicationContext();
        Assert.IsNotNull(context);
    }

    private sealed class FakeAudioMonitor : IAudioMonitor
    {
        public List<AudioDeviceInfo> Devices { get; set; } = new();
        public IReadOnlyList<AudioDeviceInfo> GetActiveCaptureDevices() => Devices;
        public IReadOnlyList<AudioDeviceInfo> GetActiveRenderDevices() => Devices;
        public AudioDeviceInfo? GetDefaultCaptureDevice() => Devices.FirstOrDefault(d => d.IsDefault);
        public void StartMonitoring(string? targetDeviceId, int retryTimeoutSeconds = 5) { }
        public void StopMonitoring() { }
        public bool IsMuted => false;
        public bool IsConnected => true;
        public string? CurrentDeviceId => null;
        public string? CurrentDeviceName => null;
#pragma warning disable CS0067
        public event Action<bool>? MuteChanged;
        public event Action<bool>? ConnectionChanged;
        public event Action? DevicesChanged;
#pragma warning restore CS0067
        public void Dispose() { }
    }

    [TestMethod]
    public void ServerSettingsForm_WhenObsConnects_UpdatesCaptureDevicesDropdown()
    {
        var settings = new ServerSettings { ObsAudioDevice = "Game Audio" };
        var fakeAudio = new FakeAudioMonitor();
        var fakeObs = new FakeObsMonitor();
        fakeObs.IsConnected = false;
        fakeObs.AvailableInputs = new List<string>();

        using var form = new ServerSettingsForm(
            settings,
            fakeAudio,
            _ => {},
            _ => {},
            _ => {},
            obsMonitor: fakeObs);

        _ = form.Handle;

        var cbo = form.ObsAudioComboBox;
        Assert.AreEqual(1, cbo.Items.Count);
        var item0 = (MicComboItem)cbo.Items[0];
        Assert.AreEqual("Game Audio", item0.DisplayName);
        Assert.IsTrue(item0.IsMissing);

        // Now OBS connects and provides inputs
        fakeObs.IsConnected = true;
        fakeObs.AvailableInputs = new List<string> { "Desktop Audio", "Game Audio", "Mic/Aux" };
        fakeObs.RaiseConnected();

        Assert.AreEqual(3, cbo.Items.Count);
        var selectedItem = (MicComboItem)cbo.SelectedItem!;
        Assert.AreEqual("Game Audio", selectedItem.DisplayName);
        Assert.IsFalse(selectedItem.IsMissing);
    }

    [TestMethod]
    public void ServerSettingsForm_SkippedFramesControls_InitializedCorrectlyAndInvokesCallback()
    {
        var settings = new ServerSettings
        {
            SkippedFramesThreshold = 42,
            SkippedFramesPeriodSeconds = 15
        };
        var fakeAudio = new FakeAudioMonitor();
        int callbackPeriod = 0;

        using var form = new ServerSettingsForm(
            settings,
            fakeAudio,
            _ => {},
            _ => {},
            _ => {},
            onSkippedFramesPeriodChanged: p => callbackPeriod = p);

        _ = form.Handle;

        Assert.AreEqual("Skipped frames threshold:", form.SkippedFramesLabel.Text);
        Assert.AreEqual(42, (int)form.SkippedFramesNumeric.Value);
        Assert.AreEqual(1, (int)form.SkippedFramesNumeric.Minimum);
        Assert.AreEqual(10000, (int)form.SkippedFramesNumeric.Maximum);

        Assert.AreEqual("Evaluation period (seconds):", form.SkippedFramesPeriodLabel.Text);
        Assert.AreEqual(15, (int)form.SkippedFramesPeriodNumeric.Value);
        Assert.AreEqual(1, (int)form.SkippedFramesPeriodNumeric.Minimum);
        Assert.AreEqual(300, (int)form.SkippedFramesPeriodNumeric.Maximum);

        // Check layout: Evaluation period controls are placed below Skipped frames threshold controls
        Assert.IsTrue(form.SkippedFramesPeriodLabel.Top >= form.SkippedFramesNumeric.Bottom, "Evaluation period label must be below skipped frames threshold input");
        Assert.IsTrue(form.SkippedFramesPeriodNumeric.Top > form.SkippedFramesNumeric.Bottom, "Evaluation period input must be below skipped frames threshold input");

        // Check right margin (form client width 420, 20px right margin => Right <= 400)
        Assert.IsTrue(form.SkippedFramesPeriodNumeric.Right <= 400, "20px right margin must be preserved");

        // Change values and verify callback & settings update
        form.SkippedFramesPeriodNumeric.Value = 25;
        Assert.AreEqual(25, callbackPeriod);
        Assert.AreEqual(25, settings.SkippedFramesPeriodSeconds);
    }

    private sealed class FakeObsMonitor : IObsMonitor
    {
        public bool IsConnected { get; set; }
        public bool IsStreaming => false;
        public bool IsRecording => false;
        public bool IsReconnecting => false;
        public bool HasNetworkIssue => false;
        public bool HasRenderIssue => false;
        public bool IsCaptureDeviceDisconnected => !AvailableInputs.Contains("Game Audio");
        public bool IsCaptureDeviceMuted => false;
        public List<string> AvailableInputs { get; set; } = new();
        public IReadOnlyList<string> AvailableAudioInputs => AvailableInputs;
        public ObsConnectionState ConnectionState => IsConnected ? ObsConnectionState.Connected : ObsConnectionState.Disconnected;

#pragma warning disable CS0067
        public event Action<bool>? ConnectionChanged;
        public event Action? StateChanged;
        public event Action<ObsAudioMeterEventArgs>? AudioMeterReceived;
        public event Action<IReadOnlyList<string>>? AudioInputsChanged;
        public event Action<ObsConnectionState>? ConnectionStateChanged;
        public event Action? StatusUpdated;
        public event Action<double, double>? AudioMeterUpdated;
#pragma warning restore CS0067

        public bool IsAudioSourceInCurrentScene { get; set; } = true;
        public int SkippedFramesPeriodSeconds { get; set; } = 5;

        public void Start() { }
        public void Stop() { }
        public void UpdateConfig(string host, int port, string? password, int retryTimeoutSeconds, int skippedFramesThreshold, string? audioDeviceName) { }
        public void UpdateConfig(string host, int port, string? password, int retryTimeoutSeconds, int skippedFramesThreshold, string? audioDeviceName, int skippedFramesPeriodSeconds) { }
        public void ConnectAsync(string host, int port, string? password) { }
        public void DisconnectAsync() { }
        public void SetAudioInputName(string? name) { }
        public void SetSkippedFramesThreshold(int threshold) { }
        public void SetSkippedFramesPeriod(int seconds) => SkippedFramesPeriodSeconds = seconds;
        public void Dispose() { }

        public void RaiseConnected()
        {
            ConnectionChanged?.Invoke(true);
            ConnectionStateChanged?.Invoke(ObsConnectionState.Connected);
            AudioInputsChanged?.Invoke(AvailableInputs.ToArray());
            StateChanged?.Invoke();
            StatusUpdated?.Invoke();
        }

        public void TriggerStatusUpdated() => StatusUpdated?.Invoke();
    }

    [TestMethod]
    public void ServerTrayApplicationContext_NotInActiveScene_SuppressesObsSoundCaptureIssue()
    {
        var tempFolder = TestDirectory.Create("ServerSceneAudioSuppressionTest");

        try
        {
            var settings = new ServerSettings { Port = 13992, IsPaused = false, ObsIp = "127.0.0.1", ObsAudioDevice = "Game Audio" };
            var fakeAudio = new FakeAudioMonitor();
            using var broadcaster = new UdpBroadcaster(13992);
            var fakeObs = new FakeObsMonitor();
            fakeObs.IsConnected = true;
            fakeObs.AvailableInputs = new List<string> { "Game Audio" };
            fakeObs.IsAudioSourceInCurrentScene = true;

            var silenceDetector = new ConditionalSilenceDetector(consecutiveReadingsRequired: 1);
            var correlationEngine = new AudioCorrelationEngine(silenceDetector: silenceDetector);

            using var context = new ServerTrayApplicationContext(settings, fakeAudio, broadcaster, fakeObs, correlationEngine);

            // Feed reading triggering sound issue
            correlationEngine.ProcessClientReading(-20.0);
            fakeObs.TriggerStatusUpdated();

            Assert.IsTrue((context.LastAlerts & AlertFlags.ObsSoundCaptureIssue) != 0, "Sound issue alert should be present when source is in active scene");

            // Now scene changes and audio source is NOT in current scene
            fakeObs.IsAudioSourceInCurrentScene = false;
            fakeObs.TriggerStatusUpdated();

            Assert.IsFalse((context.LastAlerts & AlertFlags.ObsSoundCaptureIssue) != 0, "Sound issue alert should be suppressed when source is NOT in active scene");

            // Scene changes back to scene with audio source
            fakeObs.IsAudioSourceInCurrentScene = true;
            fakeObs.TriggerStatusUpdated();

            Assert.IsTrue((context.LastAlerts & AlertFlags.ObsSoundCaptureIssue) != 0, "Sound issue alert should restore when source returns to active scene");
        }
        finally
        {
            if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true);
        }
    }
}

