using System.Drawing;
using System.Windows.Forms;
using StreamHelper.Client.Config;
using StreamHelper.Client.Overlay;
using StreamHelper.Client.UI;
using StreamHelper.Shared.Audio;
using StreamHelper.Shared.Network;
using StreamHelper.Shared.Obs;
using StreamHelper.Shared.Protocol;
using StreamHelper.Shared.UI;
using StreamHelper.Shared.Common;

namespace StreamHelper.Client;

public sealed class ClientTrayApplicationContext : ApplicationContext
{
    private readonly ClientSettings _settings;
    private readonly OverlayAssetManager _assetManager;
    private readonly OverlayForm _overlayForm;
    private readonly UdpListener _udpListener;
    private readonly IAudioMonitor _audioMonitor;
    private readonly IObsMonitor _obsMonitor;
    private readonly AudioCorrelationEngine _correlationEngine;
    private readonly ClientAudioTelemetrySender _telemetrySender;
    private readonly TrayAlertCycler _trayCycler;

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _menuPauseResume;
    private readonly ToolStripMenuItem _menuSettings;
    private readonly ToolStripMenuItem _menuDonate;
    private readonly ToolStripMenuItem _menuExit;
    private readonly ContextMenuStrip _contextMenu;

    private readonly SynchronizationContext _syncContext;
    private ClientSettingsForm? _settingsForm;
    private ClientMode _activeMode;
    private AlertFlags? _lastAlerts;

    public ClientTrayApplicationContext() : this(null, null, null, null, null, null, null, null)
    {
    }

    internal ClientTrayApplicationContext(
        ClientSettings? settings,
        UdpListener? udpListener,
        IAudioMonitor? audioMonitor,
        OverlayAssetManager? assetManager,
        OverlayForm? overlayForm,
        IObsMonitor? obsMonitor = null,
        AudioCorrelationEngine? correlationEngine = null,
        ClientAudioTelemetrySender? telemetrySender = null)
    {
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = settings ?? ClientSettings.Load();
        _assetManager = assetManager ?? new OverlayAssetManager();
        _overlayForm = overlayForm ?? new OverlayForm(_settings, _assetManager);
        _udpListener = udpListener ?? new UdpListener(_settings.Port);
        _audioMonitor = audioMonitor ?? new WindowsAudioMonitor();
        _obsMonitor = obsMonitor ?? new ObsMonitor();
        _correlationEngine = correlationEngine ?? new AudioCorrelationEngine();
        _telemetrySender = telemetrySender ?? new ClientAudioTelemetrySender();
        _trayCycler = new TrayAlertCycler(_syncContext);
        _activeMode = _settings.Mode;

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
            Text = "Stream Helper Client",
            Icon = StatusIconGenerator.GetActiveIcon(),
            ContextMenuStrip = _contextMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) =>
        {
            AppLogger.Debug("[Client] Tray icon double-clicked.");
            ShowSettings();
        };

        try
        {
            _trayIcon.ShowBalloonTip(
                3000,
                "Stream Helper Client",
                "Stream Helper is running in your system tray.\nDouble-click the tray icon to open Settings.",
                ToolTipIcon.Info);
        }
        catch
        {
        }

        AppLogger.Info($"Stream Helper Client initialized in {_activeMode} mode.");

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
                System.Diagnostics.Debug.WriteLine($"Failed to update client tray icon: {ex.Message}");
            }
        };

        // Subscribe to UDP listener events
        _udpListener.TargetServerAlertsChanged += _ => PostToUiThread(UpdateAllStates);
        _udpListener.TargetServerStateChanged += _ => PostToUiThread(UpdateAllStates);
        _udpListener.TargetServerConnectionChanged += connected =>
        {
            if (connected && _activeMode == ClientMode.DualPc && _settings.GameAudioMonitoringEnabled)
            {
                var effectiveIp = _udpListener.TargetServerIp ?? _settings.ServerIp ?? "127.0.0.1";
                _telemetrySender.ConfigureDualPc(effectiveIp, _settings.Port);
            }
            PostToUiThread(UpdateAllStates);
        };

        // Subscribe to audio monitor events
        _audioMonitor.MuteChanged += _ => PostToUiThread(UpdateAllStates);
        _audioMonitor.ConnectionChanged += _ => PostToUiThread(UpdateAllStates);
        _audioMonitor.DevicesChanged += () => PostToUiThread(UpdateAllStates);

        // Subscribe to OBS events (Single-PC mode)
        _obsMonitor.ConnectionStateChanged += _ => PostToUiThread(UpdateAllStates);
        _obsMonitor.StatusUpdated += () => PostToUiThread(UpdateAllStates);
        _obsMonitor.AudioMeterUpdated += (rms, peak) =>
        {
            if (_activeMode == ClientMode.SinglePc)
            {
                _correlationEngine.IngestObsAudioMeter(rms, peak, DateTime.UtcNow);
            }
        };
        _correlationEngine.SoundIssueChanged += _ => PostToUiThread(UpdateAllStates);

        // Subscribe to game audio device state changes
        _telemetrySender.DeviceStateChanged += _ => PostToUiThread(UpdateAllStates);

        // Apply startup configuration
        ApplyActiveModeStartup();
    }

    internal ClientSettings Settings => _settings;
    internal ClientMode ActiveMode => _activeMode;
    internal UdpListener UdpListener => _udpListener;
    internal IAudioMonitor AudioMonitor => _audioMonitor;
    internal IObsMonitor ObsMonitor => _obsMonitor;
    internal AudioCorrelationEngine CorrelationEngine => _correlationEngine;
    internal ClientAudioTelemetrySender TelemetrySender => _telemetrySender;
    internal TrayAlertCycler TrayCycler => _trayCycler;
    internal OverlayForm OverlayForm => _overlayForm;
    internal NotifyIcon TrayIcon => _trayIcon;
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

    private void ApplyActiveModeStartup()
    {
        AppLogger.Info($"[Client] Applying {_activeMode} mode startup (IsPaused={_settings.IsPaused}).");
        if (_settings.IsPaused)
        {
            _menuPauseResume.Text = "Resume";
            _udpListener.SetPaused(true);
            _udpListener.Stop();
            _audioMonitor.StopMonitoring();
            _obsMonitor.DisconnectAsync();
            _telemetrySender.Stop();
            _trayCycler.SetState(AlertFlags.None, isPaused: true);
            _overlayForm.SetLiveAlerts(AlertFlags.None, isPaused: true);
            return;
        }

        _trayCycler.Start();

        if (_activeMode == ClientMode.SinglePc)
        {
            _udpListener.Stop();
            _audioMonitor.StartMonitoring(_settings.MicrophoneId, _settings.RetryTimeout);

            if (!string.IsNullOrEmpty(_settings.ObsIp))
            {
                _obsMonitor.ConnectAsync(_settings.ObsIp, _settings.ObsPort, _settings.ObsPassword);
                if (!string.IsNullOrEmpty(_settings.ObsAudioDevice))
                {
                    _obsMonitor.SetAudioInputName(_settings.ObsAudioDevice);
                }
            }

            if (_settings.GameAudioMonitoringEnabled)
            {
                _telemetrySender.ConfigureSinglePc(_correlationEngine);
                _telemetrySender.Start(_settings.GameAudioOutputDeviceId);
            }
            else
            {
                _telemetrySender.Stop();
            }
        }
        else // DualPc
        {
            _audioMonitor.StopMonitoring();
            _obsMonitor.DisconnectAsync();

            _udpListener.Start(_settings.ServerIp, _settings.RetryTimeout);

            if (_settings.GameAudioMonitoringEnabled)
            {
                var effectiveIp = _udpListener.TargetServerIp ?? _settings.ServerIp ?? "127.0.0.1";
                _telemetrySender.ConfigureDualPc(effectiveIp, _settings.Port);
                _telemetrySender.Start(_settings.GameAudioOutputDeviceId);
            }
            else
            {
                _telemetrySender.Stop();
            }
        }

        UpdateAllStates();
    }

    public void SwitchMode(ClientMode newMode)
    {
        PostToUiThread(() =>
        {
            if (_activeMode == newMode && _settings.Mode == newMode) return;

            AppLogger.Info($"[Client] Switching operational mode: {_activeMode} -> {newMode}");
            _activeMode = newMode;
            _settings.Mode = newMode;
            _settings.Save();

            ApplyActiveModeStartup();
        });
    }

    public void SetMicrophone(string? micId, string? micName)
    {
        PostToUiThread(() =>
        {
            AppLogger.Info($"[Client] Setting microphone: '{micName}' ({micId})");
            _settings.MicrophoneId = micId;
            _settings.MicrophoneName = micName;
            _settings.Save();

            if (_activeMode == ClientMode.SinglePc && !_settings.IsPaused)
            {
                _audioMonitor.StartMonitoring(micId, _settings.RetryTimeout);
                UpdateAllStates();
            }
        });
    }

    public void SetRetryTimeout(int timeout)
    {
        PostToUiThread(() =>
        {
            AppLogger.Info($"[Client] Setting retry timeout: {timeout}s");
            _settings.RetryTimeout = timeout;
            _settings.Save();

            if (!_settings.IsPaused)
            {
                if (_activeMode == ClientMode.SinglePc)
                {
                    _audioMonitor.StartMonitoring(_settings.MicrophoneId, timeout);
                }
                else
                {
                    _udpListener.Start(_settings.ServerIp, timeout);
                }
            }
        });
    }

    private void UpdateAllStates()
    {
        if (_settings.IsPaused)
        {
            if (_lastAlerts != AlertFlags.None)
            {
                AppLogger.Info($"[Client] Monitoring paused; clearing alerts (was: [{_lastAlerts}]).");
                _lastAlerts = AlertFlags.None;
            }
            _trayCycler.SetState(AlertFlags.None, isPaused: true);
            _overlayForm.SetLiveAlerts(AlertFlags.None, isPaused: true);
            return;
        }

        AlertFlags alerts = AlertFlags.None;

        if (_activeMode == ClientMode.SinglePc)
        {
            bool micDisconnected = !_audioMonitor.IsConnected;
            bool micMuted = _audioMonitor.IsMuted;
            bool obsAudioDisconnected = _obsMonitor.IsAudioSourceMissing;
            bool obsAudioMuted = _obsMonitor.IsAudioSourceMuted;

            _correlationEngine.UpdateSuppressionStates(obsAudioDisconnected, obsAudioMuted);

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

            if (_settings.GameAudioMonitoringEnabled)
            {
                if (string.IsNullOrWhiteSpace(_settings.GameAudioOutputDeviceId))
                {
                    var renderDevices = _audioMonitor.GetActiveRenderDevices();
                    var defaultDev = renderDevices.FirstOrDefault(d => d.IsDefault) ?? renderDevices.FirstOrDefault();
                    if (defaultDev != null)
                    {
                        _settings.GameAudioOutputDeviceId = defaultDev.Id;
                        _settings.GameAudioOutputDeviceName = defaultDev.Name;
                        _settings.Save();
                        _telemetrySender.Start(defaultDev.Id);
                    }
                }

                if (string.IsNullOrWhiteSpace(_settings.GameAudioOutputDeviceId))
                    alerts |= AlertFlags.GameAudioNotSelected;
                else if (_telemetrySender.IsDeviceMissing)
                    alerts |= AlertFlags.GameAudioNotFound;
            }

            if (_lastAlerts != alerts)
            {
                AppLogger.Info($"[Client-SinglePC] Alert state changed: [{_lastAlerts}] -> [{alerts}] (MicDisconnected={micDisconnected}, MicMuted={micMuted}, ObsState={_obsMonitor.ConnectionState}, ObsNet={_obsMonitor.HasNetworkCongestion}, ObsRender={_obsMonitor.HasRenderLag || _obsMonitor.HasEncodingLag}, ObsAudioMissing={obsAudioDisconnected}, ObsAudioMuted={obsAudioMuted}, SoundIssue={_correlationEngine.HasSoundIssue}, GameAudioMissing={_telemetrySender.IsDeviceMissing})");
                _lastAlerts = alerts;
            }
            else
            {
                AppLogger.Debug($"[Client-SinglePC] UpdateAllStates: alerts=[{alerts}]");
            }
        }
        else // DualPc
        {
            if (!_udpListener.IsConnected)
            {
                alerts |= AlertFlags.ServerDisconnected;
            }
            else
            {
                alerts |= _udpListener.LastReportedAlerts;
            }

            if (_settings.GameAudioMonitoringEnabled)
            {
                if (string.IsNullOrWhiteSpace(_settings.GameAudioOutputDeviceId))
                {
                    var renderDevices = _audioMonitor.GetActiveRenderDevices();
                    var defaultDev = renderDevices.FirstOrDefault(d => d.IsDefault) ?? renderDevices.FirstOrDefault();
                    if (defaultDev != null)
                    {
                        _settings.GameAudioOutputDeviceId = defaultDev.Id;
                        _settings.GameAudioOutputDeviceName = defaultDev.Name;
                        _settings.Save();
                        _telemetrySender.Start(defaultDev.Id);
                    }
                }

                if (string.IsNullOrWhiteSpace(_settings.GameAudioOutputDeviceId))
                    alerts |= AlertFlags.GameAudioNotSelected;
                else if (_telemetrySender.IsDeviceMissing)
                    alerts |= AlertFlags.GameAudioNotFound;
            }

            if (_lastAlerts != alerts)
            {
                AppLogger.Info($"[Client-DualPC] Alert state changed: [{_lastAlerts}] -> [{alerts}] (UdpConnected={_udpListener.IsConnected}, ServerAlerts=[{_udpListener.LastReportedAlerts}], GameAudioMissing={_telemetrySender.IsDeviceMissing})");
                _lastAlerts = alerts;
            }
            else
            {
                AppLogger.Debug($"[Client-DualPC] UpdateAllStates: alerts=[{alerts}], UdpConnected={_udpListener.IsConnected}");
            }
        }

        _trayCycler.SetState(alerts, isPaused: false);
        _overlayForm.SetLiveAlerts(alerts, isPaused: false);
    }

    private void OnPauseResumeClicked(object? sender, EventArgs e)
    {
        bool newPaused = !_settings.IsPaused;
        _settings.IsPaused = newPaused;
        _settings.Save();

        AppLogger.Info($"[Client] Pause/Resume clicked: IsPaused is now {newPaused}");

        _menuPauseResume.Text = newPaused ? "Resume" : "Pause";
        _udpListener.SetPaused(newPaused);

        ApplyActiveModeStartup();
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        AppLogger.Debug("[Client] Tray menu 'Settings...' clicked.");
        ShowSettings();
    }

    public void ShowSettings()
    {
        AppLogger.Debug("[Client] Opening ClientSettingsForm.");
        if (_settingsForm == null || _settingsForm.IsDisposed)
        {
            _settingsForm = new ClientSettingsForm(
                _settings,
                _udpListener,
                _audioMonitor,
                _overlayForm,
                onModeChanged: newMode => SwitchMode(newMode),
                onMicrophoneChanged: (micId, micName) => SetMicrophone(micId, micName),
                onPortChanged: newPort =>
                {
                    AppLogger.Info($"[Client] Port changed callback triggered: newPort={newPort}");
                    if (_activeMode == ClientMode.DualPc)
                    {
                        _udpListener.Rebind(newPort, _settings.ServerIp);
                        if (_settings.GameAudioMonitoringEnabled)
                        {
                            var effectiveIp = _udpListener.TargetServerIp ?? _settings.ServerIp ?? "127.0.0.1";
                            _telemetrySender.ConfigureDualPc(effectiveIp, newPort);
                        }
                    }
                },
                onTimeoutChanged: newTimeout => SetRetryTimeout(newTimeout),
                obsMonitor: _obsMonitor,
                onObsConfigChanged: (ip, port, pw) =>
                {
                    AppLogger.Info($"[Client] OBS config changed callback triggered: {ip}:{port}");
                    if (_activeMode == ClientMode.SinglePc)
                    {
                        _obsMonitor.ConnectAsync(ip, port, pw);
                    }
                },
                onObsAudioDeviceChanged: dev =>
                {
                    AppLogger.Info($"[Client] OBS audio device changed callback triggered: '{dev}'");
                    if (_activeMode == ClientMode.SinglePc)
                    {
                        _obsMonitor.SetAudioInputName(dev);
                        UpdateAllStates();
                    }
                },
                onSkippedFramesThresholdChanged: threshold =>
                {
                    AppLogger.Info($"[Client] Skipped frames threshold changed callback triggered: {threshold}%");
                    if (_activeMode == ClientMode.SinglePc)
                    {
                        _obsMonitor.SetSkippedFramesThreshold(threshold);
                        UpdateAllStates();
                    }
                },
                onGameAudioChanged: (enabled, devId, devName) =>
                {
                    AppLogger.Info($"[Client] Game audio changed callback triggered: enabled={enabled}, device='{devName}' ({devId})");
                    _settings.GameAudioMonitoringEnabled = enabled;
                    _settings.GameAudioOutputDeviceId = devId;
                    _settings.GameAudioOutputDeviceName = devName;
                    _settings.Save();

                    if (!_settings.IsPaused)
                    {
                        if (enabled)
                        {
                            if (_activeMode == ClientMode.SinglePc)
                            {
                                _telemetrySender.ConfigureSinglePc(_correlationEngine);
                            }
                            else
                            {
                                var effectiveIp = _udpListener.TargetServerIp ?? _settings.ServerIp ?? "127.0.0.1";
                                _telemetrySender.ConfigureDualPc(effectiveIp, _settings.Port);
                            }
                            _telemetrySender.Start(devId);
                        }
                        else
                        {
                            _telemetrySender.Stop();
                        }
                        UpdateAllStates();
                    }
                });

            _settingsForm.FormClosed += (_, _) =>
            {
                AppLogger.Debug("[Client] ClientSettingsForm closed.");
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
        AppLogger.Info("[Client] Tray menu 'Donate' clicked.");
        DonateUrlProvider.OpenDonationPage();
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        AppLogger.Info("[Client] Tray menu 'Exit' clicked.");
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AppLogger.Info("[Client] Disposing ClientTrayApplicationContext resources.");
            _trayCycler.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            _overlayForm.Close();
            _overlayForm.Dispose();
            _assetManager.Dispose();
            _udpListener.Dispose();
            _audioMonitor.Dispose();
            _obsMonitor.Dispose();
            _telemetrySender.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void ExitThreadCore()
    {
        Dispose(true);
        base.ExitThreadCore();
    }
}
