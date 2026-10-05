using System.Drawing;
using System.Windows.Forms;
using StreamHelper.Server.Config;
using StreamHelper.Server.UI;
using StreamHelper.Shared.Audio;
using StreamHelper.Shared.Network;
using StreamHelper.Shared.Obs;
using StreamHelper.Shared.Protocol;
using StreamHelper.Shared.UI;
using StreamHelper.Shared.Common;

namespace StreamHelper.Server;

public sealed class ServerTrayApplicationContext : ApplicationContext
{
    private readonly ServerSettings _settings;
    private readonly IAudioMonitor _audioMonitor;
    private readonly IObsMonitor _obsMonitor;
    private readonly AudioCorrelationEngine _correlationEngine;
    private readonly AudioTelemetryReceiver _telemetryReceiver;
    private readonly UdpBroadcaster _broadcaster;
    private readonly TrayAlertCycler _trayCycler;

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _menuPauseResume;
    private readonly ToolStripMenuItem _menuSettings;
    private readonly ToolStripMenuItem _menuDonate;
    private readonly ToolStripMenuItem _menuExit;
    private readonly ContextMenuStrip _contextMenu;

    private readonly SynchronizationContext _syncContext;
    private ServerSettingsForm? _settingsForm;
    private AlertFlags? _lastAlerts;

    public ServerTrayApplicationContext() : this(null, null, null, null, null, null)
    {
    }

    internal ServerTrayApplicationContext(
        ServerSettings? settings,
        IAudioMonitor? audioMonitor,
        UdpBroadcaster? broadcaster,
        IObsMonitor? obsMonitor = null,
        AudioCorrelationEngine? correlationEngine = null,
        AudioTelemetryReceiver? telemetryReceiver = null)
    {
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = settings ?? ServerSettings.Load();

        _broadcaster = broadcaster ?? new UdpBroadcaster(_settings.Port);
        _audioMonitor = audioMonitor ?? new WindowsAudioMonitor();
        _correlationEngine = correlationEngine ?? new AudioCorrelationEngine();
        _correlationEngine.Configure(_settings.AudioMatchToleranceDb, _settings.SimpleAudioIssueDetection);
        _obsMonitor = obsMonitor ?? new ObsMonitor();
        _telemetryReceiver = telemetryReceiver ?? new AudioTelemetryReceiver(_correlationEngine, _settings.Port);
        _trayCycler = new TrayAlertCycler(_syncContext);

        // Build Tray Menu
        _contextMenu = new ContextMenuStrip();
        _menuPauseResume = new ToolStripMenuItem("Pause", null, OnPauseResumeClicked);
        _menuSettings = new ToolStripMenuItem("Settings...", null, OnSettingsClicked);
        _menuDonate = new ToolStripMenuItem("Donate", null, OnDonateClicked);
        _menuExit = new ToolStripMenuItem("Exit", null, OnExitClicked);

        _contextMenu.Items.Add(_menuPauseResume);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_menuSettings);
        _contextMenu.Items.Add(_menuDonate);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_menuExit);

        _trayIcon = new NotifyIcon
        {
            Text = "Stream Helper Server",
            Icon = StatusIconGenerator.GetActiveIcon(),
            ContextMenuStrip = _contextMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) =>
        {
            AppLogger.Debug("[Server] Tray icon double-clicked.");
            ShowSettings();
        };

        try
        {
            _trayIcon.ShowBalloonTip(
                3000,
                "Stream Helper Server",
                "Stream Helper Server is running in your system tray.\nDouble-click the tray icon to open Settings.",
                ToolTipIcon.Info);
        }
        catch
        {
        }

        AppLogger.Info($"Stream Helper Server initialized on port {_settings.Port}.");

        // Subscribe to tray cycler visual updates
        _trayCycler.VisualChanged += (icon, tooltip) =>
        {
            try
            {
                _trayIcon.Icon = icon;
                _trayIcon.Text = tooltip;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to update tray icon: {ex.Message}");
            }
        };

        // Subscribe to audio monitor events
        _audioMonitor.MuteChanged += _ => PostToUiThread(UpdateAllStates);
        _audioMonitor.ConnectionChanged += _ => PostToUiThread(UpdateAllStates);
        _audioMonitor.DevicesChanged += () => PostToUiThread(UpdateAllStates);

        // Subscribe to OBS events
        _obsMonitor.ConnectionStateChanged += _ => PostToUiThread(UpdateAllStates);
        _obsMonitor.StatusUpdated += () => PostToUiThread(UpdateAllStates);
        _obsMonitor.AudioInputsChanged += _ => PostToUiThread(UpdateAllStates);
        _obsMonitor.AudioMeterUpdated += (rms, peak) =>
        {
            _correlationEngine.IngestObsAudioMeter(rms, peak, DateTime.UtcNow);
        };
        _correlationEngine.SoundIssueChanged += _ => PostToUiThread(UpdateAllStates);

        // Apply startup settings
        if (_settings.IsPaused)
        {
            _menuPauseResume.Text = "Resume";
            _broadcaster.SetPaused(true);
            _trayCycler.SetState(AlertFlags.None, isPaused: true);
        }
        else
        {
            _audioMonitor.StartMonitoring(_settings.MicrophoneId, _settings.RetryTimeout);
            _broadcaster.Start();
            _telemetryReceiver.Start();
            _trayCycler.Start();

            // Connect OBS if configured
            if (!string.IsNullOrEmpty(_settings.ObsIp))
            {
                _obsMonitor.ConnectAsync(_settings.ObsIp, _settings.ObsPort, _settings.ObsPassword);
                if (!string.IsNullOrEmpty(_settings.ObsAudioDevice))
                {
                    _obsMonitor.SetAudioInputName(_settings.ObsAudioDevice);
                }
            }

            UpdateAllStates();
        }
    }

    internal ContextMenuStrip ContextMenu => _contextMenu;
    internal ToolStripMenuItem MenuDonate => _menuDonate;

    private void PostToUiThread(Action action)
    {
        if (SynchronizationContext.Current == _syncContext)
        {
            action();
        }
        else
        {
            _syncContext.Post(_ => action(), null);
        }
    }

    private void UpdateAllStates()
    {
        if (_settings.IsPaused)
        {
            if (_lastAlerts != AlertFlags.None)
            {
                AppLogger.Info($"[Server] Monitoring paused; clearing alerts (was: [{_lastAlerts}]).");
                _lastAlerts = AlertFlags.None;
            }
            _broadcaster.SetPaused(true);
            _trayCycler.SetState(AlertFlags.None, isPaused: true);
            return;
        }

        bool micDisconnected = !_audioMonitor.IsConnected;
        bool micMuted = _audioMonitor.IsMuted;

        bool obsAudioDisconnected = _obsMonitor.IsAudioSourceMissing;
        bool obsAudioMuted = _obsMonitor.IsAudioSourceMuted;

        // Update correlation engine suppression states (OBS Device Disconnected -> Muted -> Correlation Sound Issue)
        _correlationEngine.UpdateSuppressionStates(obsAudioDisconnected, obsAudioMuted);

        AlertFlags alerts = AlertFlags.None;
        if (micDisconnected) alerts |= AlertFlags.MicDisconnected;
        else if (micMuted) alerts |= AlertFlags.MicMuted;

        if (_obsMonitor.ConnectionState == ObsConnectionState.Disconnected) alerts |= AlertFlags.ObsDisconnected;
        else if (_obsMonitor.ConnectionState == ObsConnectionState.Reconnecting) alerts |= AlertFlags.ObsReconnecting;
        else if (_obsMonitor.ConnectionState == ObsConnectionState.Connected)
        {
            if (_obsMonitor.HasNetworkCongestion) alerts |= AlertFlags.ObsNetworkIssue;
            if (_obsMonitor.HasRenderLag || _obsMonitor.HasEncodingLag) alerts |= AlertFlags.ObsRenderIssue;

            if (obsAudioDisconnected) alerts |= AlertFlags.ObsCaptureDeviceDisconnected;
            else if (obsAudioMuted) alerts |= AlertFlags.ObsCaptureDeviceMuted;
            else if (_correlationEngine.HasSoundIssue) alerts |= AlertFlags.ObsSoundCaptureIssue;
        }

        if (_lastAlerts != alerts)
        {
            AppLogger.Info($"[Server] Alert state changed: [{_lastAlerts}] -> [{alerts}] (MicDisconnected={micDisconnected}, MicMuted={micMuted}, ObsState={_obsMonitor.ConnectionState}, ObsNet={_obsMonitor.HasNetworkCongestion}, ObsRender={_obsMonitor.HasRenderLag || _obsMonitor.HasEncodingLag}, ObsAudioMissing={obsAudioDisconnected}, ObsAudioMuted={obsAudioMuted}, SoundIssue={_correlationEngine.HasSoundIssue})");
            _lastAlerts = alerts;
        }
        else
        {
            AppLogger.Debug($"[Server] UpdateAllStates: alerts=[{alerts}], micDisconnected={micDisconnected}, micMuted={micMuted}, obsState={_obsMonitor.ConnectionState}");
        }

        var micName = _audioMonitor.CurrentDeviceName ?? _settings.MicrophoneName;
        _broadcaster.UpdateAlerts(alerts, micName);
        _trayCycler.SetState(alerts, isPaused: false);
    }

    private void OnPauseResumeClicked(object? sender, EventArgs e)
    {
        bool newPaused = !_settings.IsPaused;
        _settings.IsPaused = newPaused;
        _settings.Save();

        AppLogger.Info($"[Server] Pause/Resume clicked: IsPaused is now {newPaused}");

        _menuPauseResume.Text = newPaused ? "Resume" : "Pause";
        _broadcaster.SetPaused(newPaused);

        if (newPaused)
        {
            _audioMonitor.StopMonitoring();
            _telemetryReceiver.Stop();
            _trayCycler.SetState(AlertFlags.None, isPaused: true);
        }
        else
        {
            _audioMonitor.StartMonitoring(_settings.MicrophoneId, _settings.RetryTimeout);
            _telemetryReceiver.Start();
            _trayCycler.Start();
            UpdateAllStates();
        }
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        AppLogger.Debug("[Server] Tray menu 'Settings...' clicked.");
        ShowSettings();
    }

    public void ShowSettings()
    {
        AppLogger.Debug("[Server] Opening ServerSettingsForm.");
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new ServerSettingsForm(
                _settings,
                _audioMonitor,
                onPortChanged: newPort =>
                {
                    AppLogger.Info($"[Server] Port changed callback triggered: newPort={newPort}");
                    _broadcaster.Rebind(newPort);
                    _telemetryReceiver.Rebind(newPort);
                },
                onMicrophoneChanged: newMicId =>
                {
                    AppLogger.Info($"[Server] Microphone changed callback triggered: newMicId='{newMicId}'");
                    if (!_settings.IsPaused)
                    {
                        _audioMonitor.StartMonitoring(newMicId, _settings.RetryTimeout);
                        UpdateAllStates();
                    }
                },
                onTimeoutChanged: newTimeout =>
                {
                    AppLogger.Info($"[Server] Timeout changed callback triggered: newTimeout={newTimeout}s");
                    if (!_settings.IsPaused)
                    {
                        _audioMonitor.StartMonitoring(_settings.MicrophoneId, newTimeout);
                    }
                },
                obsMonitor: _obsMonitor,
                onObsConfigChanged: (ip, port, pw) =>
                {
                    AppLogger.Info($"[Server] OBS config changed callback triggered: {ip}:{port}");
                    _obsMonitor.ConnectAsync(ip, port, pw);
                },
                onObsAudioDeviceChanged: dev =>
                {
                    AppLogger.Info($"[Server] OBS audio device changed callback triggered: '{dev}'");
                    _obsMonitor.SetAudioInputName(dev);
                    UpdateAllStates();
                },
                onSkippedFramesThresholdChanged: threshold =>
                {
                    AppLogger.Info($"[Server] Skipped frames threshold changed callback triggered: {threshold}%");
                    _obsMonitor.SetSkippedFramesThreshold(threshold);
                    UpdateAllStates();
                },
                onAudioDetectionConfigChanged: (tolerance, simpleMode) =>
                {
                    AppLogger.Info($"[Server] Audio detection config changed callback triggered: tolerance={tolerance:F1} dB, simpleMode={simpleMode}");
                    _correlationEngine.Configure(tolerance, simpleMode);
                    UpdateAllStates();
                });

            _settingsForm.FormClosed += (_, _) =>
            {
                AppLogger.Debug("[Server] ServerSettingsForm closed.");
                _settingsForm = null;
            };
            _settingsForm.Show();
            _settingsForm.BringToFront();
            _settingsForm.Activate();
        }
        else
        {
            _settingsForm.BringToFront();
            _settingsForm.Activate();
        }
    }

    private void OnDonateClicked(object? sender, EventArgs e)
    {
        AppLogger.Info("[Server] Tray menu 'Donate' clicked.");
        DonateUrlProvider.OpenDonationPage();
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        AppLogger.Info("[Server] Tray menu 'Exit' clicked.");
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AppLogger.Info("[Server] Disposing ServerTrayApplicationContext resources.");
            _trayCycler.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            _audioMonitor.Dispose();
            _obsMonitor.Dispose();
            _telemetryReceiver.Dispose();
            _broadcaster.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void ExitThreadCore()
    {
        Dispose(true);
        base.ExitThreadCore();
    }
}
