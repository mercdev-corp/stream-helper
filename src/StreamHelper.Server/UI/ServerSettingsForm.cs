using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using StreamHelper.Server.Config;
using StreamHelper.Shared.Audio;
using StreamHelper.Shared.Common;
using StreamHelper.Shared.Network;
using StreamHelper.Shared.Obs;
using StreamHelper.Shared.UI;

namespace StreamHelper.Server.UI;

public sealed class ServerSettingsForm : Form
{
    private readonly ServerSettings _settings;
    private readonly IAudioMonitor _audioMonitor;
    private readonly IObsMonitor? _obsMonitor;
    private readonly Action<int> _onPortChanged;
    private readonly Action<string?> _onMicrophoneChanged;
    private readonly Action<int> _onTimeoutChanged;
    private readonly Action<string, int, string>? _onObsConfigChanged;
    private readonly Action<string?>? _onObsAudioDeviceChanged;
    private readonly Action<int>? _onSkippedFramesThresholdChanged;
    private readonly Action<double, bool>? _onAudioDetectionConfigChanged;

    private CheckBox _chkStartup = null!;
    private CheckBox _chkDebugLogging = null!;
    private ComboBox _cboMicrophone = null!;
    private TextBox _txtObsIp = null!;
    private TextBox _txtObsPort = null!;
    private TextBox _txtObsPassword = null!;
    private CheckBox _chkShowPassword = null!;
    private NumericUpDown _numSkippedFrames = null!;
    private ComboBox _cboObsAudio = null!;
    private CheckBox _chkSimpleAudioDetection = null!;
    private TrackBar _trkAudioMatchTolerance = null!;
    private Label _lblAudioMatchToleranceValue = null!;
    private AudioDetectionSettingsController _audioDetectionController = null!;
    private NumericUpDown _numTimeout = null!;
    private TextBox _txtPort = null!;
    private ToolTip _toolTip = null!;
    private Button _btnOpenLogs = null!;
    private Button _btnClose = null!;
    private Label _lblVersion = null!;
    private MicrophoneSelectionController _micController = null!;
    private bool _isUpdatingControls;

    internal Label VersionLabel => _lblVersion;
    internal ComboBox MicrophoneComboBox => _cboMicrophone;
    internal ComboBox ObsAudioComboBox => _cboObsAudio;
    internal CheckBox SimpleAudioIssueDetectionCheckBox => _chkSimpleAudioDetection;
    internal TrackBar AudioMatchToleranceTrackBar => _trkAudioMatchTolerance;
    internal Label AudioMatchToleranceValueLabel => _lblAudioMatchToleranceValue;
    internal TextBox ObsIpTextBox => _txtObsIp;
    internal TextBox ObsPortTextBox => _txtObsPort;
    internal TextBox ObsPasswordTextBox => _txtObsPassword;
    internal NumericUpDown SkippedFramesNumeric => _numSkippedFrames;
    internal TextBox PortTextBox => _txtPort;
    internal NumericUpDown TimeoutNumeric => _numTimeout;

    public ServerSettingsForm(
        ServerSettings settings,
        IAudioMonitor audioMonitor,
        Action<int> onPortChanged,
        Action<string?> onMicrophoneChanged,
        Action<int> onTimeoutChanged,
        IObsMonitor? obsMonitor = null,
        Action<string, int, string>? onObsConfigChanged = null,
        Action<string?>? onObsAudioDeviceChanged = null,
        Action<int>? onSkippedFramesThresholdChanged = null,
        Action<double, bool>? onAudioDetectionConfigChanged = null)
    {
        _settings = settings;
        _audioMonitor = audioMonitor ?? new WindowsAudioMonitor();
        _obsMonitor = obsMonitor;
        _onPortChanged = onPortChanged;
        _onMicrophoneChanged = onMicrophoneChanged;
        _onTimeoutChanged = onTimeoutChanged;
        _onObsConfigChanged = onObsConfigChanged;
        _onObsAudioDeviceChanged = onObsAudioDeviceChanged;
        _onSkippedFramesThresholdChanged = onSkippedFramesThresholdChanged;
        _onAudioDetectionConfigChanged = onAudioDetectionConfigChanged;

        InitializeComponent();
        LoadSettingsIntoControls();

        if (_obsMonitor != null)
        {
            _obsMonitor.AudioInputsChanged += OnObsAudioInputsChanged;
            _obsMonitor.ConnectionStateChanged += OnObsConnectionStateChanged;
        }

        if (_audioMonitor != null)
        {
            _audioMonitor.DevicesChanged += OnAudioDevicesChanged;
        }
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWA_TEXT_COLOR = 36;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBarTheme();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyTitleBarTheme();
    }

    private void ApplyTitleBarTheme()
    {
        try
        {
            int captionColor = 0x00F0F0F0;
            int textColor = 0x00000000;
            DwmSetWindowAttribute(Handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
            DwmSetWindowAttribute(Handle, DWMWA_TEXT_COLOR, ref textColor, sizeof(int));
        }
        catch
        {
        }
    }

    private void InitializeComponent()
    {
        Text = "Stream Helper Server Settings";
        Icon = StatusIconGenerator.GetAppIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 505);
        ShowInTaskbar = true;

        _toolTip = new ToolTip();

        _chkStartup = new CheckBox
        {
            Text = "Run on startup",
            Location = new Point(20, 15),
            AutoSize = true
        };
        _chkStartup.CheckedChanged += ChkStartup_CheckedChanged;

        _chkDebugLogging = new CheckBox
        {
            Text = "Enable debug logging",
            Location = new Point(160, 15),
            AutoSize = true
        };
        _chkDebugLogging.CheckedChanged += ChkDebugLogging_CheckedChanged;

        var lblMic = new Label
        {
            Text = "Microphone:",
            Location = new Point(20, 45),
            AutoSize = true
        };

        _cboMicrophone = new ComboBox
        {
            Location = new Point(20, 68),
            Width = 380,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _micController = new MicrophoneSelectionController(_cboMicrophone, _audioMonitor, item =>
        {
            AppLogger.Info($"[ServerSettingsForm] Microphone selected: '{item.DisplayName}' ({item.Id})");
            _settings.MicrophoneId = item.Id;
            _settings.MicrophoneName = item.DisplayName;
            _settings.Save();
            _onMicrophoneChanged(item.Id);
        });

        // OBS Section
        var lblObsGroup = new Label
        {
            Text = "OBS WebSocket Connection:",
            Location = new Point(20, 102),
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true
        };

        var lblObsIp = new Label
        {
            Text = "IP Address:",
            Location = new Point(20, 128),
            AutoSize = true
        };
        _txtObsIp = new TextBox
        {
            Location = new Point(20, 148),
            Width = 110,
            Text = _settings.ObsIp
        };
        _txtObsIp.TextChanged += TxtObsParams_TextChanged;

        var lblObsPort = new Label
        {
            Text = "Port:",
            Location = new Point(140, 128),
            AutoSize = true
        };
        _txtObsPort = new TextBox
        {
            Location = new Point(140, 148),
            Width = 60,
            Text = _settings.ObsPort.ToString()
        };
        _txtObsPort.TextChanged += TxtObsParams_TextChanged;

        var lblObsPassword = new Label
        {
            Text = "Password:",
            Location = new Point(210, 128),
            AutoSize = true
        };
        _txtObsPassword = new TextBox
        {
            Location = new Point(210, 148),
            Width = 120,
            UseSystemPasswordChar = true,
            Text = _settings.ObsPassword
        };
        _txtObsPassword.TextChanged += TxtObsParams_TextChanged;

        _chkShowPassword = new CheckBox
        {
            Text = "Show",
            Location = new Point(335, 150),
            AutoSize = true
        };
        _chkShowPassword.CheckedChanged += (_, _) =>
        {
            _txtObsPassword.UseSystemPasswordChar = !_chkShowPassword.Checked;
        };

        var lblSkippedFrames = new Label
        {
            Text = "Skipped frames threshold (per min):",
            Location = new Point(20, 180),
            AutoSize = true
        };
        _numSkippedFrames = new NumericUpDown
        {
            Location = new Point(20, 202),
            Width = 100,
            Minimum = 1,
            Maximum = 10000,
            Value = Math.Clamp(_settings.SkippedFramesThreshold, 1, 10000)
        };
        _numSkippedFrames.ValueChanged += NumSkippedFrames_ValueChanged;

        var lblObsAudio = new Label
        {
            Text = "OBS Game Audio Capture Source:",
            Location = new Point(20, 235),
            AutoSize = true
        };
        _cboObsAudio = new ComboBox
        {
            Location = new Point(20, 258),
            Width = 380,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 22
        };
        _cboObsAudio.DrawItem += CboObsAudio_DrawItem;
        _cboObsAudio.SelectedIndexChanged += CboObsAudio_SelectedIndexChanged;

        _chkSimpleAudioDetection = new CheckBox
        {
            Text = "Simple audio issue detection",
            Location = new Point(20, 288),
            AutoSize = true
        };

        var lblAudioTolerance = new Label
        {
            Text = "Audio match tolerance:",
            Location = new Point(20, 314),
            AutoSize = true
        };

        _lblAudioMatchToleranceValue = new Label
        {
            Location = new Point(220, 314),
            Width = 60,
            Text = $"{_settings.AudioMatchToleranceDb} dB"
        };

        _trkAudioMatchTolerance = new TrackBar
        {
            Location = new Point(20, 340),
            Width = 380,
            Minimum = 0,
            Maximum = 40,
            TickFrequency = 5,
            Value = Math.Clamp((int)Math.Round(_settings.AudioMatchToleranceDb), 0, 40)
        };

        // Network Section
        var lblPort = new Label
        {
            Text = "Broadcast Port:",
            Location = new Point(20, 390),
            AutoSize = true
        };
        _txtPort = new TextBox
        {
            Location = new Point(20, 413),
            Width = 100,
            Text = _settings.Port.ToString()
        };
        _txtPort.TextChanged += TxtPort_TextChanged;

        var lblTimeout = new Label
        {
            Text = "Retry timeout (seconds):",
            Location = new Point(140, 390),
            AutoSize = true
        };
        _numTimeout = new NumericUpDown
        {
            Location = new Point(140, 413),
            Width = 100,
            Minimum = 1,
            Maximum = 300,
            Value = _settings.RetryTimeout
        };
        _numTimeout.ValueChanged += NumTimeout_ValueChanged;

        _btnOpenLogs = new Button
        {
            Text = "View Logs...",
            Location = new Point(20, 455),
            Width = 100,
            Height = 30
        };
        _btnOpenLogs.Click += BtnOpenLogs_Click;

        _btnClose = new Button
        {
            Text = "Close",
            Location = new Point(320, 455),
            Width = 80,
            Height = 30
        };
        _btnClose.Click += (_, _) => Close();

        _lblVersion = new Label
        {
            Text = AppVersion.DisplayVersion,
            Location = new Point(120, 455),
            Size = new Size(200, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
            AutoEllipsis = true
        };

        Controls.Add(_chkStartup);
        Controls.Add(_chkDebugLogging);
        Controls.Add(lblMic);
        Controls.Add(_cboMicrophone);
        Controls.Add(lblObsGroup);
        Controls.Add(lblObsIp);
        Controls.Add(_txtObsIp);
        Controls.Add(lblObsPort);
        Controls.Add(_txtObsPort);
        Controls.Add(lblObsPassword);
        Controls.Add(_txtObsPassword);
        Controls.Add(_chkShowPassword);
        Controls.Add(lblSkippedFrames);
        Controls.Add(_numSkippedFrames);
        Controls.Add(lblObsAudio);
        Controls.Add(_cboObsAudio);
        Controls.Add(_chkSimpleAudioDetection);
        Controls.Add(lblAudioTolerance);
        Controls.Add(_lblAudioMatchToleranceValue);
        Controls.Add(_trkAudioMatchTolerance);
        Controls.Add(lblPort);
        Controls.Add(_txtPort);
        Controls.Add(lblTimeout);
        Controls.Add(_numTimeout);
        Controls.Add(_btnOpenLogs);
        Controls.Add(_lblVersion);
        Controls.Add(_btnClose);
    }

    private void LoadSettingsIntoControls()
    {
        _isUpdatingControls = true;
        try
        {
            _chkStartup.Checked = StartupRegistryManager.IsStartupEnabled("StreamHelperServer");
            _chkDebugLogging.Checked = _settings.DebugLogging;
            _numTimeout.Value = Math.Clamp(_settings.RetryTimeout, 1, 300);
            _txtPort.Text = _settings.Port.ToString();
            _txtObsIp.Text = _settings.ObsIp;
            _txtObsPort.Text = _settings.ObsPort.ToString();
            _txtObsPassword.Text = _settings.ObsPassword;
            _numSkippedFrames.Value = Math.Clamp(_settings.SkippedFramesThreshold, 1, 10000);

            if (_audioDetectionController == null)
            {
                _audioDetectionController = new AudioDetectionSettingsController(
                    _chkSimpleAudioDetection,
                    _trkAudioMatchTolerance,
                    _lblAudioMatchToleranceValue,
                    initialSimpleMode: _settings.SimpleAudioIssueDetection,
                    initialToleranceDb: _settings.AudioMatchToleranceDb,
                    saveSimpleMode: simple =>
                    {
                        _settings.SimpleAudioIssueDetection = simple;
                        _settings.Save();
                    },
                    saveTolerance: tol =>
                    {
                        _settings.AudioMatchToleranceDb = tol;
                        _settings.Save();
                    },
                    onChanged: (tol, simple) =>
                    {
                        _onAudioDetectionConfigChanged?.Invoke(tol, simple);
                    });
            }
            else
            {
                _audioDetectionController.ApplyState(_settings.SimpleAudioIssueDetection, _settings.AudioMatchToleranceDb);
            }

            _micController.Populate(_settings.MicrophoneId, _settings.MicrophoneName);
            PopulateObsAudioSources();
        }
        finally
        {
            _isUpdatingControls = false;
        }
    }

    private void PopulateObsAudioSources()
    {
        if (_cboObsAudio.DroppedDown) return;

        var availableSources = _obsMonitor?.GetAudioInputNames() ?? Array.Empty<string>();
        string? saved = _settings.ObsAudioDevice;

        bool savedFound = false;
        if (!string.IsNullOrEmpty(saved))
        {
            savedFound = availableSources.Any(s => string.Equals(s, saved, StringComparison.OrdinalIgnoreCase));
        }

        var newItems = new List<MicComboItem>();
        if (!savedFound && !string.IsNullOrEmpty(saved))
        {
            newItems.Add(new MicComboItem(saved, saved, IsMissing: true));
        }

        var sortedSources = availableSources
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        foreach (var src in sortedSources)
        {
            newItems.Add(new MicComboItem(src, src, IsMissing: false));
        }

        // Compare newItems with existing items in _cboObsAudio
        bool itemsIdentical = _cboObsAudio.Items.Count == newItems.Count;
        if (itemsIdentical)
        {
            for (int i = 0; i < newItems.Count; i++)
            {
                if (_cboObsAudio.Items[i] is MicComboItem existing)
                {
                    if (!string.Equals(existing.Id, newItems[i].Id, StringComparison.OrdinalIgnoreCase) ||
                        existing.IsMissing != newItems[i].IsMissing)
                    {
                        itemsIdentical = false;
                        break;
                    }
                }
                else
                {
                    itemsIdentical = false;
                    break;
                }
            }
        }

        if (itemsIdentical)
        {
            if (_cboObsAudio.SelectedItem is MicComboItem currentSel)
            {
                if (savedFound && string.Equals(currentSel.Id, saved, StringComparison.OrdinalIgnoreCase))
                {
                    return; // Items and selection already match, do not reload
                }
                if (!savedFound && !string.IsNullOrEmpty(saved) && currentSel.IsMissing && string.Equals(currentSel.Id, saved, StringComparison.OrdinalIgnoreCase))
                {
                    return; // Missing item already selected, do not reload
                }
            }
            else if (_cboObsAudio.Items.Count == 0)
            {
                return;
            }
        }

        _isUpdatingControls = true;
        _cboObsAudio.BeginUpdate();
        try
        {
            _cboObsAudio.Items.Clear();
            foreach (var item in newItems)
            {
                _cboObsAudio.Items.Add(item);
                if (savedFound && string.Equals(item.Id, saved, StringComparison.OrdinalIgnoreCase))
                {
                    _cboObsAudio.SelectedItem = item;
                }
                else if (!savedFound && item.IsMissing)
                {
                    _cboObsAudio.SelectedItem = item;
                }
            }

            if (_cboObsAudio.SelectedItem == null && _cboObsAudio.Items.Count > 0)
            {
                _cboObsAudio.SelectedIndex = 0;
            }

            if (string.IsNullOrEmpty(_settings.ObsAudioDevice) && _cboObsAudio.SelectedItem is MicComboItem defaultObsItem && !defaultObsItem.IsMissing)
            {
                _settings.ObsAudioDevice = defaultObsItem.Id;
                _settings.Save();
                _onObsAudioDeviceChanged?.Invoke(defaultObsItem.Id);
            }
        }
        finally
        {
            _cboObsAudio.EndUpdate();
            _isUpdatingControls = false;
        }
    }

    private void OnObsAudioInputsChanged(IReadOnlyList<string> inputs)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed && !Disposing && !_cboObsAudio.DroppedDown)
                    {
                        PopulateObsAudioSources();
                    }
                }));
            }
            catch { }
            return;
        }
        if (!_cboObsAudio.DroppedDown)
        {
            PopulateObsAudioSources();
        }
    }

    private void OnObsConnectionStateChanged(ObsConnectionState state)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed && !Disposing && !_cboObsAudio.DroppedDown)
                    {
                        PopulateObsAudioSources();
                    }
                }));
            }
            catch { }
            return;
        }
        if (!_cboObsAudio.DroppedDown)
        {
            PopulateObsAudioSources();
        }
    }

    private void OnAudioDevicesChanged()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => _micController.Populate(_settings.MicrophoneId, _settings.MicrophoneName))); } catch { }
            return;
        }
        if (!_cboMicrophone.DroppedDown)
        {
            _micController.Populate(_settings.MicrophoneId, _settings.MicrophoneName);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        AppLogger.Debug("[ServerSettingsForm] Dialog shown.");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        AppLogger.Debug("[ServerSettingsForm] Dialog closed.");
        if (_obsMonitor != null)
        {
            _obsMonitor.AudioInputsChanged -= OnObsAudioInputsChanged;
            _obsMonitor.ConnectionStateChanged -= OnObsConnectionStateChanged;
        }
        if (_audioMonitor != null)
        {
            _audioMonitor.DevicesChanged -= OnAudioDevicesChanged;
        }
        base.OnFormClosed(e);
    }

    private void CboObsAudio_DrawItem(object? sender, DrawItemEventArgs e)
    {
        MicrophoneSelectionController.DrawItem(_cboObsAudio, e);
    }

    private void CboObsAudio_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingControls) return;

        if (_cboObsAudio.SelectedItem is MicComboItem item && !item.IsMissing)
        {
            AppLogger.Info($"[ServerSettingsForm] OBS audio capture source selected: '{item.Id}'");
            _settings.ObsAudioDevice = item.Id;
            _settings.Save();
            _onObsAudioDeviceChanged?.Invoke(item.Id);
        }
    }

    private void TxtObsParams_TextChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingControls) return;

        string ip = _txtObsIp.Text.Trim();
        if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";

        if (int.TryParse(_txtObsPort.Text.Trim(), out int port) && port is >= 1 and <= 65535)
        {
            _txtObsPort.ForeColor = SystemColors.WindowText;
            string pw = _txtObsPassword.Text;

            AppLogger.Info($"[ServerSettingsForm] OBS connection parameters changed: {ip}:{port}");
            _settings.ObsIp = ip;
            _settings.ObsPort = port;
            _settings.ObsPassword = pw;
            _settings.Save();

            _onObsConfigChanged?.Invoke(ip, port, pw);
        }
        else
        {
            _txtObsPort.ForeColor = Color.Red;
        }
    }

    private void NumSkippedFrames_ValueChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingControls) return;

        int val = (int)_numSkippedFrames.Value;
        AppLogger.Info($"[ServerSettingsForm] Skipped frames threshold changed: {val}%");
        _settings.SkippedFramesThreshold = val;
        _settings.Save();
        _onSkippedFramesThresholdChanged?.Invoke(val);
    }

    private void ChkStartup_CheckedChanged(object? sender, EventArgs e)
    {
        try
        {
            AppLogger.Info($"[ServerSettingsForm] Run on startup changed: {_chkStartup.Checked}");
            StartupRegistryManager.SetStartupEnabled("StreamHelperServer", _chkStartup.Checked);
            _settings.RunOnStartup = _chkStartup.Checked;
            _settings.Save();
        }
        catch (Exception ex)
        {
            AppLogger.Error("[ServerSettingsForm] Failed to update startup setting", ex);
            MessageBox.Show($"Failed to update startup setting: {ex.Message}", "Startup Configuration Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void NumTimeout_ValueChanged(object? sender, EventArgs e)
    {
        int val = (int)_numTimeout.Value;
        AppLogger.Info($"[ServerSettingsForm] Timeout changed: {val}s");
        _settings.RetryTimeout = val;
        _settings.Save();
        _onTimeoutChanged(val);
    }

    private void TxtPort_TextChanged(object? sender, EventArgs e)
    {
        if (int.TryParse(_txtPort.Text.Trim(), out int port) && port is >= 1 and <= 65535)
        {
            bool inUse = (port != _settings.Port) && NetworkUtils.IsUdpPortInUse(port);
            if (inUse)
            {
                _txtPort.ForeColor = Color.Red;
                _toolTip.SetToolTip(_txtPort, "Port is already in use");
            }
            else
            {
                _txtPort.ForeColor = SystemColors.WindowText;
                _toolTip.SetToolTip(_txtPort, string.Empty);

                if (port != _settings.Port)
                {
                    AppLogger.Info($"[ServerSettingsForm] Port changed: {_settings.Port} -> {port}");
                    _settings.Port = port;
                    _settings.Save();
                    _onPortChanged(port);
                }
            }
        }
        else
        {
            _txtPort.ForeColor = Color.Red;
            _toolTip.SetToolTip(_txtPort, "Invalid port number (1-65535)");
        }
    }

    private void ChkDebugLogging_CheckedChanged(object? sender, EventArgs e)
    {
        _settings.DebugLogging = _chkDebugLogging.Checked;
        _settings.Save();
        if (_chkDebugLogging.Checked)
        {
            AppLogger.IsDebugEnabled = true;
            AppLogger.Info("Debug logging enabled in settings.");
        }
        else
        {
            AppLogger.Info("Debug logging disabled in settings.");
            AppLogger.IsDebugEnabled = false;
        }
    }

    private void BtnOpenLogs_Click(object? sender, EventArgs e)
    {
        try
        {
            string path = AppLogger.LogFilePath;
            if (string.IsNullOrEmpty(path))
            {
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stream-helper-server.log");
            }

            if (!File.Exists(path))
            {
                if (!_settings.DebugLogging)
                {
                    MessageBox.Show(
                        "Debug logging is currently disabled and no log file has been created yet.\n\nEnable 'Enable debug logging' to start recording logs.",
                        "Stream Helper Server Logs",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                File.WriteAllText(path, $"=== Stream Helper Server Log initialized on {DateTime.Now} ===" + Environment.NewLine);
            }

            AppLogger.Info($"[ServerSettingsForm] Opening log file '{path}'.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Error("[ServerSettingsForm] Failed to open log file", ex);
            MessageBox.Show($"Failed to open log file: {ex.Message}", "Open Log Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _obsMonitor != null)
        {
            _obsMonitor.AudioInputsChanged -= OnObsAudioInputsChanged;
        }
        base.Dispose(disposing);
    }
}
