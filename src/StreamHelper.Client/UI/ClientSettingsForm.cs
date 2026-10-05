using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using StreamHelper.Client.Config;
using StreamHelper.Client.Overlay;
using StreamHelper.Shared.Audio;
using StreamHelper.Shared.Common;
using StreamHelper.Shared.Network;
using StreamHelper.Shared.Obs;
using StreamHelper.Shared.UI;

namespace StreamHelper.Client.UI;

public sealed class ClientSettingsForm : Form
{
    private readonly ClientSettings _settings;
    private readonly UdpListener _udpListener;
    private readonly IAudioMonitor _audioMonitor;
    private readonly IObsMonitor? _obsMonitor;
    private readonly OverlayForm _overlayForm;
    private readonly Action<ClientMode>? _onModeChanged;
    private readonly Action<string?, string>? _onMicrophoneChanged;
    private readonly Action<int>? _onPortChanged;
    private readonly Action<int>? _onTimeoutChanged;
    private readonly Action<string, int, string>? _onObsConfigChanged;
    private readonly Action<string?>? _onObsAudioDeviceChanged;
    private readonly Action<int>? _onSkippedFramesThresholdChanged;
    private readonly Action<bool, string?, string?>? _onGameAudioChanged;

    private ComboBox _cboMode = null!;
    private CheckBox _chkStartup = null!;
    private CheckBox _chkDebugLogging = null!;

    // Dual PC Panel
    private Panel _pnlDualPc = null!;
    private Label _lblDualPcNote = null!;
    private Label _lblServer = null!;
    private ComboBox _cboServerIp = null!;
    private Label _lblPort = null!;
    private TextBox _txtPort = null!;

    // Single PC Panel
    private Panel _pnlSinglePc = null!;
    private Label _lblMicrophone = null!;
    private ComboBox _cboMicrophone = null!;
    private MicrophoneSelectionController _micController = null!;
    private TextBox _txtObsIp = null!;
    private TextBox _txtObsPort = null!;
    private TextBox _txtObsPassword = null!;
    private CheckBox _chkShowPassword = null!;
    private Label _lblSkipped = null!;
    private NumericUpDown _numSkippedFrames = null!;
    private Label _lblObsAudio = null!;
    private ComboBox _cboObsAudio = null!;
    private CheckBox _chkSimpleAudioDetection = null!;
    private TrackBar _trkAudioMatchTolerance = null!;
    private Label _lblAudioMatchToleranceVal = null!;
    private AudioDetectionSettingsController _audioDetectionController = null!;

    // Game Audio Controls
    private CheckBox _chkGameAudio = null!;
    private ComboBox _cboGameAudioOutput = null!;

    // Shared Controls
    private Label _lblTimeout = null!;
    private NumericUpDown _numTimeout = null!;
    private Label _lblOpacity = null!;
    private TrackBar _trkOpacity = null!;
    private Label _lblOpacityVal = null!;
    private Label _lblFrequency = null!;
    private TrackBar _trkFrequency = null!;
    private Label _lblFrequencyVal = null!;
    private Label _lblAnimationCycles = null!;
    private TrackBar _trkAnimationCycles = null!;
    private Label _lblAnimationCyclesVal = null!;
    private Label _lblSize = null!;
    private TrackBar _trkSize = null!;
    private Label _lblSizeVal = null!;
    private Label _lblHint = null!;
    private Button _btnOpenLogs = null!;
    private Button _btnClose = null!;
    private Label _lblVersion = null!;
    private ToolTip _toolTip = null!;
    private bool _isUpdatingControls;
    private bool _isApplyingLayout;
    private readonly Action<double, bool>? _onAudioDetectionConfigChanged;

    internal Label VersionLabel => _lblVersion;
    internal ComboBox ModeComboBox => _cboMode;
    internal Label DualPcNoteLabel => _lblDualPcNote;
    internal Label ServerLabel => _lblServer;
    internal Panel DualPcPanel => _pnlDualPc;
    internal Panel SinglePcPanel => _pnlSinglePc;
    internal ComboBox ServerComboBox => _cboServerIp;
    internal TextBox PortTextBox => _txtPort;
    internal Label MicrophoneLabel => _lblMicrophone;
    internal ComboBox MicrophoneComboBox => _cboMicrophone;
    internal Label TimeoutLabel => _lblTimeout;
    internal NumericUpDown TimeoutNumeric => _numTimeout;
    internal MicrophoneSelectionController MicController => _micController;
    internal ComboBox GameAudioOutputComboBox => _cboGameAudioOutput;
    internal CheckBox GameAudioCheckBox => _chkGameAudio;
    internal Label SkippedFramesLabel => _lblSkipped;
    internal NumericUpDown SkippedFramesNumeric => _numSkippedFrames;
    internal Label ObsAudioLabel => _lblObsAudio;
    internal ComboBox ObsAudioComboBox => _cboObsAudio;
    internal CheckBox SimpleAudioIssueDetectionCheckBox => _chkSimpleAudioDetection;
    internal TrackBar AudioMatchToleranceTrackBar => _trkAudioMatchTolerance;
    internal Label AudioMatchToleranceValueLabel => _lblAudioMatchToleranceVal;
    internal TrackBar AnimationCyclesTrackBar => _trkAnimationCycles;
    internal Label AnimationCyclesValueLabel => _lblAnimationCyclesVal;
    internal Label OpacityLabel => _lblOpacity;
    internal TrackBar OpacityTrackBar => _trkOpacity;
    internal Button CloseButton => _btnClose;
    internal Button OpenLogsButton => _btnOpenLogs;

    internal sealed record ServerComboItem(string? Ip, string DisplayText, bool IsOffline);

    public ClientSettingsForm(
        ClientSettings settings,
        UdpListener udpListener,
        OverlayForm overlayForm,
        Action<int> onPortChanged)
        : this(settings, udpListener, new WindowsAudioMonitor(), overlayForm, null, null, onPortChanged, null)
    {
    }

    public ClientSettingsForm(
        ClientSettings settings,
        UdpListener udpListener,
        IAudioMonitor audioMonitor,
        OverlayForm overlayForm,
        Action<ClientMode>? onModeChanged = null,
        Action<string?, string>? onMicrophoneChanged = null,
        Action<int>? onPortChanged = null,
        Action<int>? onTimeoutChanged = null,
        IObsMonitor? obsMonitor = null,
        Action<string, int, string>? onObsConfigChanged = null,
        Action<string?>? onObsAudioDeviceChanged = null,
        Action<int>? onSkippedFramesThresholdChanged = null,
        Action<bool, string?, string?>? onGameAudioChanged = null,
        Action<double, bool>? onAudioDetectionConfigChanged = null)
    {
        _settings = settings;
        _udpListener = udpListener;
        _audioMonitor = audioMonitor;
        _obsMonitor = obsMonitor;
        _overlayForm = overlayForm;
        _onModeChanged = onModeChanged;
        _onMicrophoneChanged = onMicrophoneChanged;
        _onPortChanged = onPortChanged;
        _onTimeoutChanged = onTimeoutChanged;
        _onObsConfigChanged = onObsConfigChanged;
        _onObsAudioDeviceChanged = onObsAudioDeviceChanged;
        _onSkippedFramesThresholdChanged = onSkippedFramesThresholdChanged;
        _onGameAudioChanged = onGameAudioChanged;
        _onAudioDetectionConfigChanged = onAudioDetectionConfigChanged;

        InitializeComponent();
        LoadSettingsIntoControls();

        // Synchronize size slider when overlay is resized via mouse drag or scroll wheel
        _overlayForm.OverlayResized += OnOverlayResized;

        // Enable WYSIWYG mode while settings dialog is open
        _overlayForm.SetWysiwygMode(true);
    }

    private void InitializeComponent()
    {
        Text = "Stream Helper Client Settings";
        Icon = StatusIconGenerator.GetAppIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(420, 886);
        ShowInTaskbar = true;

        _toolTip = new ToolTip();

        var lblMode = new Label
        {
            Text = "Mode:",
            Location = new Point(20, 10),
            AutoSize = true
        };

        _cboMode = new ComboBox
        {
            Location = new Point(20, 30),
            Width = 380,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cboMode.Items.Add("Dual PC");
        _cboMode.Items.Add("Single PC");
        _cboMode.SelectedIndexChanged += CboMode_SelectedIndexChanged;

        _chkStartup = new CheckBox
        {
            Text = "Run on startup",
            Location = new Point(20, 60),
            AutoSize = true
        };
        _chkStartup.CheckedChanged += ChkStartup_CheckedChanged;

        _chkDebugLogging = new CheckBox
        {
            Text = "Enable debug logging",
            Location = new Point(180, 60),
            AutoSize = true
        };
        _chkDebugLogging.CheckedChanged += ChkDebugLogging_CheckedChanged;

        // Dual PC Panel
        _pnlDualPc = new Panel
        {
            Location = new Point(0, 85),
            Size = new Size(420, 140)
        };

        _lblDualPcNote = new Label
        {
            Text = "Run server app on remote PC where your Microphone is plugged in",
            Location = new Point(20, 0),
            MaximumSize = new Size(380, 0),
            ForeColor = SystemColors.GrayText,
            AutoSize = true
        };

        _lblServer = new Label
        {
            Text = "Server:",
            Location = new Point(20, 35),
            AutoSize = true
        };

        _cboServerIp = new ComboBox
        {
            Location = new Point(20, 55),
            Width = 380,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 22
        };
        _cboServerIp.DrawItem += CboServerIp_DrawItem;
        _cboServerIp.SelectedIndexChanged += CboServerIp_SelectedIndexChanged;

        _lblPort = new Label
        {
            Text = "Port number:",
            Location = new Point(20, 85),
            AutoSize = true
        };

        _txtPort = new TextBox
        {
            Location = new Point(20, 105),
            Width = 100,
            Text = _settings.Port.ToString()
        };
        _txtPort.TextChanged += TxtPort_TextChanged;

        _pnlDualPc.Controls.Add(_lblDualPcNote);
        _pnlDualPc.Controls.Add(_lblServer);
        _pnlDualPc.Controls.Add(_cboServerIp);
        _pnlDualPc.Controls.Add(_lblPort);
        _pnlDualPc.Controls.Add(_txtPort);

        // Single PC Panel
        _pnlSinglePc = new Panel
        {
            Location = new Point(0, 85),
            Size = new Size(420, 336)
        };

        _lblMicrophone = new Label
        {
            Text = "Microphone:",
            Location = new Point(20, 0),
            AutoSize = true
        };

        _cboMicrophone = new ComboBox
        {
            Location = new Point(20, 20),
            Width = 380,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _micController = new MicrophoneSelectionController(_cboMicrophone, _audioMonitor, item =>
        {
            AppLogger.Info($"[ClientSettingsForm] Microphone selected: '{item.DisplayName}' ({item.Id})");
            _settings.MicrophoneId = item.Id;
            _settings.MicrophoneName = item.DisplayName;
            _settings.Save();
            _onMicrophoneChanged?.Invoke(item.Id, item.DisplayName);
        });

        var lblObsGroup = new Label
        {
            Text = "OBS WebSocket Connection:",
            Location = new Point(20, 50),
            Font = new Font(Font, FontStyle.Bold),
            AutoSize = true
        };

        var lblObsIp = new Label { Text = "IP:", Location = new Point(20, 72), AutoSize = true };
        _txtObsIp = new TextBox { Location = new Point(20, 92), Width = 110, Text = _settings.ObsIp };
        _txtObsIp.TextChanged += TxtObsParams_TextChanged;

        var lblObsPort = new Label { Text = "Port:", Location = new Point(140, 72), AutoSize = true };
        _txtObsPort = new TextBox { Location = new Point(140, 92), Width = 60, Text = _settings.ObsPort.ToString() };
        _txtObsPort.TextChanged += TxtObsParams_TextChanged;

        var lblObsPw = new Label { Text = "Password:", Location = new Point(210, 72), AutoSize = true };
        _txtObsPassword = new TextBox { Location = new Point(210, 92), Width = 110, UseSystemPasswordChar = true, Text = _settings.ObsPassword };
        _txtObsPassword.TextChanged += TxtObsParams_TextChanged;

        _chkShowPassword = new CheckBox { Text = "Show", Location = new Point(330, 94), AutoSize = true };
        _chkShowPassword.CheckedChanged += (_, _) => _txtObsPassword.UseSystemPasswordChar = !_chkShowPassword.Checked;

        _lblSkipped = new Label { Text = "Skipped frames threshold:", Location = new Point(20, 120), AutoSize = true };
        _numSkippedFrames = new NumericUpDown { Location = new Point(20, 140), Width = 90, Minimum = 1, Maximum = 10000, Value = Math.Clamp(_settings.SkippedFramesThreshold, 1, 10000) };
        _numSkippedFrames.ValueChanged += NumSkippedFrames_ValueChanged;

        _lblObsAudio = new Label { Text = "OBS Game Audio Source:", Location = new Point(20, 172), AutoSize = true };
        _cboObsAudio = new ComboBox { Location = new Point(20, 194), Width = 380, DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22 };
        _cboObsAudio.DrawItem += (s, e) => MicrophoneSelectionController.DrawItem(_cboObsAudio, e);
        _cboObsAudio.SelectedIndexChanged += CboObsAudio_SelectedIndexChanged;

        _chkSimpleAudioDetection = new CheckBox
        {
            Text = "Simple audio issue detection",
            Location = new Point(20, 228),
            AutoSize = true
        };

        var lblAudioTolerance = new Label
        {
            Text = "Audio match tolerance:",
            Location = new Point(20, 254),
            AutoSize = true
        };

        _lblAudioMatchToleranceVal = new Label
        {
            Location = new Point(220, 254),
            Width = 60,
            Text = $"{_settings.AudioMatchToleranceDb} dB"
        };

        _trkAudioMatchTolerance = new TrackBar
        {
            Location = new Point(20, 280),
            Width = 380,
            Minimum = 0,
            Maximum = 40,
            TickFrequency = 5,
            Value = Math.Clamp((int)Math.Round(_settings.AudioMatchToleranceDb), 0, 40)
        };

        _pnlSinglePc.Controls.Add(_lblMicrophone);
        _pnlSinglePc.Controls.Add(_cboMicrophone);
        _pnlSinglePc.Controls.Add(lblObsGroup);
        _pnlSinglePc.Controls.Add(lblObsIp);
        _pnlSinglePc.Controls.Add(_txtObsIp);
        _pnlSinglePc.Controls.Add(lblObsPort);
        _pnlSinglePc.Controls.Add(_txtObsPort);
        _pnlSinglePc.Controls.Add(lblObsPw);
        _pnlSinglePc.Controls.Add(_txtObsPassword);
        _pnlSinglePc.Controls.Add(_chkShowPassword);
        _pnlSinglePc.Controls.Add(_lblSkipped);
        _pnlSinglePc.Controls.Add(_numSkippedFrames);
        _pnlSinglePc.Controls.Add(_lblObsAudio);
        _pnlSinglePc.Controls.Add(_cboObsAudio);
        _pnlSinglePc.Controls.Add(_chkSimpleAudioDetection);
        _pnlSinglePc.Controls.Add(lblAudioTolerance);
        _pnlSinglePc.Controls.Add(_lblAudioMatchToleranceVal);
        _pnlSinglePc.Controls.Add(_trkAudioMatchTolerance);

        // Game Audio section (visible in both modes)
        _chkGameAudio = new CheckBox
        {
            Text = "Enable Game Audio Monitoring",
            Location = new Point(20, 290),
            AutoSize = true
        };
        _chkGameAudio.CheckedChanged += ChkGameAudio_CheckedChanged;

        _cboGameAudioOutput = new ComboBox
        {
            Location = new Point(20, 315),
            Width = 380,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 22
        };
        _cboGameAudioOutput.DrawItem += (s, e) => MicrophoneSelectionController.DrawItem(_cboGameAudioOutput, e);
        _cboGameAudioOutput.SelectedIndexChanged += CboGameAudioOutput_SelectedIndexChanged;

        _lblTimeout = new Label
        {
            Text = "Retry timeout (seconds):",
            Location = new Point(20, 350),
            AutoSize = true
        };

        _numTimeout = new NumericUpDown
        {
            Location = new Point(20, 372),
            Width = 100,
            Minimum = 1,
            Maximum = 300,
            Value = _settings.RetryTimeout
        };
        _numTimeout.ValueChanged += NumTimeout_ValueChanged;

        _lblOpacity = new Label
        {
            Text = "Overlay maximum opacity:",
            Location = new Point(20, 490),
            AutoSize = true
        };
        _lblOpacityVal = new Label
        {
            Location = new Point(220, 490),
            Width = 50,
            Text = $"{_settings.Opacity}%"
        };
        _trkOpacity = new TrackBar
        {
            Location = new Point(20, 510),
            Width = 380,
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Value = _settings.Opacity
        };
        _trkOpacity.ValueChanged += TrkOpacity_ValueChanged;

        _lblFrequency = new Label
        {
            Text = "Pulse frequency (seconds):",
            Location = new Point(20, 555),
            AutoSize = true
        };
        _lblFrequencyVal = new Label
        {
            Location = new Point(220, 555),
            Width = 50,
            Text = $"{_settings.PulseFrequency:F1}s"
        };
        _trkFrequency = new TrackBar
        {
            Location = new Point(20, 575),
            Width = 380,
            Minimum = 1,
            Maximum = 50,
            TickFrequency = 5,
            Value = Math.Clamp((int)(_settings.PulseFrequency * 10), 1, 50)
        };
        _trkFrequency.ValueChanged += TrkFrequency_ValueChanged;

        _lblAnimationCycles = new Label
        {
            Text = "Animation cycles:",
            Location = new Point(20, 620),
            AutoSize = true
        };
        _lblAnimationCyclesVal = new Label
        {
            Location = new Point(220, 620),
            Width = 50,
            Text = $"{_settings.AnimationCycles}"
        };
        _trkAnimationCycles = new TrackBar
        {
            Location = new Point(20, 640),
            Width = 380,
            Minimum = 1,
            Maximum = 10,
            TickFrequency = 1,
            Value = Math.Clamp(_settings.AnimationCycles, 1, 10)
        };
        _trkAnimationCycles.ValueChanged += TrkAnimationCycles_ValueChanged;

        _lblSize = new Label
        {
            Text = "Overlay size (pixels):",
            Location = new Point(20, 685),
            AutoSize = true
        };
        _lblSizeVal = new Label
        {
            Location = new Point(220, 685),
            Width = 60,
            Text = $"{_settings.OverlayWidth}px"
        };
        _trkSize = new TrackBar
        {
            Location = new Point(20, 705),
            Width = 380,
            Minimum = 32,
            Maximum = 1024,
            TickFrequency = 64,
            Value = Math.Clamp(_settings.OverlayWidth, 32, 1024)
        };
        _trkSize.ValueChanged += TrkSize_ValueChanged;

        _lblHint = new Label
        {
            Text = "Drag overlay to move • Scroll mouse wheel or drag corners to resize.",
            Location = new Point(20, 750),
            MaximumSize = new Size(380, 0),
            ForeColor = Color.Gray,
            AutoSize = true
        };

        _btnOpenLogs = new Button
        {
            Text = "View Logs...",
            Location = new Point(20, 785),
            Width = 100,
            Height = 30
        };
        _btnOpenLogs.Click += BtnOpenLogs_Click;

        _btnClose = new Button
        {
            Text = "Close",
            Location = new Point(300, 785),
            Width = 100,
            Height = 30
        };
        _btnClose.Click += (_, _) => Close();

        _lblVersion = new Label
        {
            Text = AppVersion.DisplayVersion,
            Location = new Point(120, 785),
            Size = new Size(180, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
            AutoEllipsis = true
        };

        Controls.Add(lblMode);
        Controls.Add(_cboMode);
        Controls.Add(_chkStartup);
        Controls.Add(_chkDebugLogging);
        Controls.Add(_pnlDualPc);
        Controls.Add(_pnlSinglePc);
        Controls.Add(_chkGameAudio);
        Controls.Add(_cboGameAudioOutput);
        Controls.Add(_lblTimeout);
        Controls.Add(_numTimeout);
        Controls.Add(_lblOpacity);
        Controls.Add(_lblOpacityVal);
        Controls.Add(_trkOpacity);
        Controls.Add(_lblFrequency);
        Controls.Add(_lblFrequencyVal);
        Controls.Add(_trkFrequency);
        Controls.Add(_lblAnimationCycles);
        Controls.Add(_lblAnimationCyclesVal);
        Controls.Add(_trkAnimationCycles);
        Controls.Add(_lblSize);
        Controls.Add(_lblSizeVal);
        Controls.Add(_trkSize);
        Controls.Add(_lblHint);
        Controls.Add(_btnOpenLogs);
        Controls.Add(_lblVersion);
        Controls.Add(_btnClose);

        _udpListener.DiscoveredServersUpdated += OnDiscoveredServersUpdated;
        _udpListener.TargetServerConnectionChanged += OnTargetServerConnectionChanged;
        _audioMonitor.DevicesChanged += OnAudioDevicesChanged;
        _cboServerIp.DropDownClosed += (_, _) => PopulateServerIpList();
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

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (_settings != null && _pnlDualPc != null && _pnlSinglePc != null && _lblOpacity != null)
        {
            ApplyLayoutForMode(_settings.Mode);
        }
    }

    private void LayoutDualPcControls()
    {
        if (_lblDualPcNote == null || _lblServer == null || _cboServerIp == null || _lblPort == null || _txtPort == null || _pnlDualPc == null) return;
        int noteBottom = Math.Max(20, _lblDualPcNote.Bottom + 5);
        _lblServer.Location = new Point(20, noteBottom);
        _cboServerIp.Location = new Point(20, _lblServer.Bottom + 5);
        _lblPort.Location = new Point(20, _cboServerIp.Bottom + 8);
        _txtPort.Location = new Point(20, _lblPort.Bottom + 5);
        _pnlDualPc.Height = _txtPort.Bottom + 10;
    }

    private void ApplyLayoutForMode(ClientMode mode)
    {
        if (_isApplyingLayout || _lblOpacity == null || _numTimeout == null) return;
        _isApplyingLayout = true;
        try
        {
            bool isSinglePc = mode == ClientMode.SinglePc;

            _pnlDualPc.Visible = !isSinglePc;
            _pnlSinglePc.Visible = isSinglePc;

            if (isSinglePc)
            {
                _pnlSinglePc.Height = _trkAudioMatchTolerance.Bottom + 11;
            }
            else
            {
                LayoutDualPcControls();
            }

            // Position shared controls below active panel
            int contentBottom = isSinglePc ? _pnlSinglePc.Bottom + 5 : _pnlDualPc.Bottom + 5;
            _chkGameAudio.Location = new Point(20, contentBottom);
            _cboGameAudioOutput.Location = new Point(20, contentBottom + 25);
            _lblTimeout.Location = new Point(20, contentBottom + 55);
            _numTimeout.Location = new Point(20, contentBottom + 77);

            if (isSinglePc)
            {
                _lblTimeout.Text = "Microphone reconnect check (seconds):";
            }
            else
            {
                _lblTimeout.Text = "Retry timeout (seconds):";
            }

            int overlayTop = _numTimeout.Location.Y + 43;
            _lblOpacity.Location = new Point(20, overlayTop);
            _lblOpacityVal.Location = new Point(220, overlayTop);
            _trkOpacity.Location = new Point(20, overlayTop + 20);
            _lblFrequency.Location = new Point(20, overlayTop + 65);
            _lblFrequencyVal.Location = new Point(220, overlayTop + 65);
            _trkFrequency.Location = new Point(20, overlayTop + 85);
            _lblAnimationCycles.Location = new Point(20, overlayTop + 130);
            _lblAnimationCyclesVal.Location = new Point(220, overlayTop + 130);
            _trkAnimationCycles.Location = new Point(20, overlayTop + 150);
            _lblSize.Location = new Point(20, overlayTop + 195);
            _lblSizeVal.Location = new Point(220, overlayTop + 195);
            _trkSize.Location = new Point(20, overlayTop + 215);
            _lblHint.Location = new Point(20, overlayTop + 260);
            _btnOpenLogs.Location = new Point(20, overlayTop + 295);
            _lblVersion.Location = new Point(120, overlayTop + 295);
            _btnClose.Location = new Point(300, overlayTop + 295);

            int targetHeight = overlayTop + 340;
            var targetSize = new Size(420, targetHeight);
            if (ClientSize != targetSize)
            {
                ClientSize = targetSize;
            }
        }
        finally
        {
            _isApplyingLayout = false;
        }
    }

    private void LoadSettingsIntoControls()
    {
        _isUpdatingControls = true;
        try
        {
            _chkStartup.Checked = StartupRegistryManager.IsStartupEnabled("StreamHelperClient");
            _chkDebugLogging.Checked = _settings.DebugLogging;
            _cboMode.SelectedIndex = _settings.Mode == ClientMode.SinglePc ? 1 : 0;
            _numTimeout.Value = Math.Clamp(_settings.RetryTimeout, 1, 300);
            _trkAnimationCycles.Value = Math.Clamp(_settings.AnimationCycles, 1, 10);
            _lblAnimationCyclesVal.Text = $"{_trkAnimationCycles.Value}";
            _txtPort.Text = _settings.Port.ToString();
            _chkGameAudio.Checked = _settings.GameAudioMonitoringEnabled;

            _txtObsIp.Text = _settings.ObsIp;
            _txtObsPort.Text = _settings.ObsPort.ToString();
            _txtObsPassword.Text = _settings.ObsPassword;
            _numSkippedFrames.Value = Math.Clamp(_settings.SkippedFramesThreshold, 1, 10000);

            if (_audioDetectionController == null)
            {
                _audioDetectionController = new AudioDetectionSettingsController(
                    _chkSimpleAudioDetection,
                    _trkAudioMatchTolerance,
                    _lblAudioMatchToleranceVal,
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

            ApplyLayoutForMode(_settings.Mode);

            if (_settings.Mode == ClientMode.SinglePc)
            {
                _micController.Populate(_settings.MicrophoneId, _settings.MicrophoneName);
                PopulateObsAudioSources();
            }
            else
            {
                PopulateServerIpList();
            }

            PopulateGameAudioOutputs();
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
                    return;
                }
                if (!savedFound && !string.IsNullOrEmpty(saved) && currentSel.IsMissing && string.Equals(currentSel.Id, saved, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            else if (_cboObsAudio.Items.Count == 0)
            {
                return;
            }
        }

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
        }
    }

    private void PopulateGameAudioOutputs()
    {
        _cboGameAudioOutput.BeginUpdate();
        try
        {
            _cboGameAudioOutput.Items.Clear();
            var renderDevices = _audioMonitor.GetActiveRenderDevices();
            string? savedId = _settings.GameAudioOutputDeviceId;
            string? savedName = _settings.GameAudioOutputDeviceName;

            bool savedFound = false;
            if (!string.IsNullOrEmpty(savedId))
            {
                savedFound = renderDevices.Any(d => string.Equals(d.Id, savedId, StringComparison.OrdinalIgnoreCase));
            }

            if (!savedFound && !string.IsNullOrEmpty(savedId))
            {
                var missingItem = new MicComboItem(savedId, savedName ?? "(Missing Device)", IsMissing: true);
                _cboGameAudioOutput.Items.Add(missingItem);
                _cboGameAudioOutput.SelectedItem = missingItem;
            }

            var sortedDevices = renderDevices
                .Select(dev => new MicComboItem(dev.Id, dev.Name + MicrophoneSelectionController.GetDeviceBadge(dev), IsMissing: false))
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var item in sortedDevices)
            {
                _cboGameAudioOutput.Items.Add(item);
                if (savedFound && string.Equals(item.Id, savedId, StringComparison.OrdinalIgnoreCase))
                {
                    _cboGameAudioOutput.SelectedItem = item;
                }
            }

            if (_cboGameAudioOutput.SelectedItem == null && _cboGameAudioOutput.Items.Count > 0)
            {
                _cboGameAudioOutput.SelectedIndex = 0;
            }

            if (string.IsNullOrEmpty(_settings.GameAudioOutputDeviceId) && _cboGameAudioOutput.SelectedItem is MicComboItem defaultGameAudioItem && !defaultGameAudioItem.IsMissing)
            {
                _settings.GameAudioOutputDeviceId = defaultGameAudioItem.Id;
                _settings.GameAudioOutputDeviceName = defaultGameAudioItem.DisplayName;
                _settings.Save();
                _onGameAudioChanged?.Invoke(_settings.GameAudioMonitoringEnabled, defaultGameAudioItem.Id, defaultGameAudioItem.DisplayName);
            }
        }
        finally
        {
            _cboGameAudioOutput.EndUpdate();
        }
    }

    private void CboMode_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingControls) return;

        var selectedMode = _cboMode.SelectedIndex == 1 ? ClientMode.SinglePc : ClientMode.DualPc;
        if (_settings.Mode != selectedMode)
        {
            AppLogger.Info($"[ClientSettingsForm] Operational mode changed: {_settings.Mode} -> {selectedMode}");
            _settings.Mode = selectedMode;
            _settings.Save();

            _onModeChanged?.Invoke(selectedMode);
            ApplyLayoutForMode(selectedMode);

            if (selectedMode == ClientMode.SinglePc)
            {
                _micController.Populate(_settings.MicrophoneId, _settings.MicrophoneName);
                PopulateObsAudioSources();
            }
            else
            {
                PopulateServerIpList();
            }
        }
    }

    private void ChkGameAudio_CheckedChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingControls) return;

        if (string.IsNullOrEmpty(_settings.GameAudioOutputDeviceId) && _cboGameAudioOutput.SelectedItem is MicComboItem item && !item.IsMissing)
        {
            _settings.GameAudioOutputDeviceId = item.Id;
            _settings.GameAudioOutputDeviceName = item.DisplayName;
        }

        AppLogger.Info($"[ClientSettingsForm] Game audio monitoring changed: {_chkGameAudio.Checked} (device: '{_settings.GameAudioOutputDeviceName}')");
        _settings.GameAudioMonitoringEnabled = _chkGameAudio.Checked;
        _settings.Save();
        _onGameAudioChanged?.Invoke(_settings.GameAudioMonitoringEnabled, _settings.GameAudioOutputDeviceId, _settings.GameAudioOutputDeviceName);
    }

    private void CboGameAudioOutput_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingControls) return;

        if (_cboGameAudioOutput.SelectedItem is MicComboItem item && !item.IsMissing)
        {
            AppLogger.Info($"[ClientSettingsForm] Game audio output device changed: '{item.DisplayName}' ({item.Id})");
            _settings.GameAudioOutputDeviceId = item.Id;
            _settings.GameAudioOutputDeviceName = item.DisplayName;
            _settings.Save();
            _onGameAudioChanged?.Invoke(_settings.GameAudioMonitoringEnabled, item.Id, item.DisplayName);
        }
    }

    private void CboObsAudio_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingControls) return;

        if (_cboObsAudio.SelectedItem is MicComboItem item && !item.IsMissing)
        {
            AppLogger.Info($"[ClientSettingsForm] OBS audio capture source selected: '{item.Id}'");
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

            AppLogger.Info($"[ClientSettingsForm] OBS connection parameters changed: {ip}:{port}");
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
        AppLogger.Info($"[ClientSettingsForm] Skipped frames threshold changed: {val}%");
        _settings.SkippedFramesThreshold = val;
        _settings.Save();
        _onSkippedFramesThresholdChanged?.Invoke(val);
    }

    private void OnDiscoveredServersUpdated()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(OnDiscoveredServersUpdated)); } catch { }
            return;
        }
        if (_cboServerIp.DroppedDown) return;
        if (_settings.Mode == ClientMode.DualPc)
        {
            PopulateServerIpList();
        }
    }

    private void OnTargetServerConnectionChanged(bool isConnected)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => OnTargetServerConnectionChanged(isConnected))); } catch { }
            return;
        }
        if (_cboServerIp.DroppedDown) return;
        if (_settings.Mode == ClientMode.DualPc)
        {
            PopulateServerIpList();
        }
    }

    private void OnAudioDevicesChanged()
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(OnAudioDevicesChanged)); } catch { }
            return;
        }
        if (!_cboMicrophone.DroppedDown && _settings.Mode == ClientMode.SinglePc)
        {
            _micController.Populate(_settings.MicrophoneId, _settings.MicrophoneName);
        }
        if (!_cboGameAudioOutput.DroppedDown)
        {
            PopulateGameAudioOutputs();
        }
    }

    private void PopulateServerIpList()
    {
        _isUpdatingControls = true;
        try
        {
            var prevSelectedIp = (_cboServerIp.SelectedItem as ServerComboItem)?.Ip ?? _settings.ServerIp;

            _cboServerIp.BeginUpdate();
            _cboServerIp.Items.Clear();

            var servers = _udpListener.GetDiscoveredServers();
            string? savedIp = _settings.ServerIp;

            var targetDiscovered = !string.IsNullOrEmpty(savedIp)
                ? servers.FirstOrDefault(s => string.Equals(s.ServerIp, savedIp, StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(s.ServerId, savedIp, StringComparison.OrdinalIgnoreCase))
                : null;

            bool isTargetOnline = _udpListener.IsConnected && targetDiscovered != null;
            ServerComboItem? itemToSelect = null;

            ServerComboItem? offlineTargetItem = null;
            var availableItems = new List<ServerComboItem>();

            if (!string.IsNullOrEmpty(savedIp))
            {
                string displayText = targetDiscovered != null
                    ? $"{targetDiscovered.ServerIp} - {targetDiscovered.HostName} ({targetDiscovered.MicrophoneName})"
                    : savedIp;

                if (!isTargetOnline)
                {
                    displayText += " (Offline)";
                    offlineTargetItem = new ServerComboItem(savedIp, displayText, IsOffline: true);
                }
                else
                {
                    availableItems.Add(new ServerComboItem(savedIp, displayText, IsOffline: false));
                }
            }

            foreach (var s in servers)
            {
                if (!string.IsNullOrEmpty(savedIp) &&
                    (string.Equals(s.ServerIp, savedIp, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(s.ServerId, savedIp, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                bool isOnline = (DateTime.UtcNow - s.LastSeen) <= TimeSpan.FromSeconds(_settings.RetryTimeout);
                var text = $"{s.ServerIp} - {s.HostName} ({s.MicrophoneName})" + (isOnline ? "" : " (Offline)");
                availableItems.Add(new ServerComboItem(s.ServerIp, text, IsOffline: !isOnline));
            }

            var sortedItems = availableItems
                .OrderBy(i => i.DisplayText, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            // If selected option is red strikethrough (offline target), it MUST be the first option!
            if (offlineTargetItem != null)
            {
                _cboServerIp.Items.Add(offlineTargetItem);
                if (string.Equals(prevSelectedIp, savedIp, StringComparison.OrdinalIgnoreCase))
                {
                    itemToSelect = offlineTargetItem;
                }
            }

            foreach (var item in sortedItems)
            {
                _cboServerIp.Items.Add(item);
                if (itemToSelect == null && string.Equals(prevSelectedIp, item.Ip, StringComparison.OrdinalIgnoreCase))
                {
                    itemToSelect = item;
                }
            }

            if (itemToSelect != null)
            {
                _cboServerIp.SelectedItem = itemToSelect;
            }
            else if (_cboServerIp.Items.Count > 0)
            {
                _cboServerIp.SelectedIndex = 0;
            }

            if (string.IsNullOrEmpty(_settings.ServerIp) && _cboServerIp.SelectedItem is ServerComboItem defServer && !string.IsNullOrEmpty(defServer.Ip))
            {
                _settings.ServerIp = defServer.Ip;
                _settings.Save();
                _udpListener.SetTargetServer(defServer.Ip);
            }

            _cboServerIp.Invalidate();
        }
        finally
        {
            _cboServerIp.EndUpdate();
            _isUpdatingControls = false;
        }
    }

    private void CboServerIp_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _cboServerIp.Items.Count) return;

        e.DrawBackground();

        if (_cboServerIp.Items[e.Index] is ServerComboItem item)
        {
            using var brush = new SolidBrush(item.IsOffline ? Color.Red : e.ForeColor);
            var fontStyle = item.IsOffline ? (e.Font?.Style ?? FontStyle.Regular) | FontStyle.Strikeout : (e.Font?.Style ?? FontStyle.Regular);
            using var font = new Font(e.Font ?? SystemFonts.DefaultFont, fontStyle);

            e.Graphics.DrawString(item.DisplayText, font, brush, e.Bounds.X + 2, e.Bounds.Y + 2);
        }

        e.DrawFocusRectangle();
    }

    private void CboServerIp_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingControls) return;

        if (_cboServerIp.SelectedItem is ServerComboItem selected && !string.IsNullOrEmpty(selected.Ip))
        {
            if (!string.Equals(_settings.ServerIp, selected.Ip, StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Info($"[ClientSettingsForm] Target server IP changed: '{_settings.ServerIp}' -> '{selected.Ip}'");
                _settings.ServerIp = selected.Ip;
                _settings.Save();
                _udpListener.SetTargetServer(selected.Ip);
            }
        }
    }

    private void ChkStartup_CheckedChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingControls) return;

        try
        {
            AppLogger.Info($"[ClientSettingsForm] Run on startup changed: {_chkStartup.Checked}");
            StartupRegistryManager.SetStartupEnabled("StreamHelperClient", _chkStartup.Checked);
            _settings.RunOnStartup = _chkStartup.Checked;
            _settings.Save();
        }
        catch (Exception ex)
        {
            AppLogger.Error("[ClientSettingsForm] Failed to update startup setting", ex);
            MessageBox.Show($"Failed to update startup setting: {ex.Message}", "Startup Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void TxtPort_TextChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingControls) return;

        if (int.TryParse(_txtPort.Text.Trim(), out int port) && port is >= 1 and <= 65535)
        {
            _txtPort.ForeColor = SystemColors.WindowText;
            if (port != _settings.Port)
            {
                AppLogger.Info($"[ClientSettingsForm] Port changed: {_settings.Port} -> {port}");
                _settings.Port = port;
                _settings.Save();
                _onPortChanged?.Invoke(port);
            }
        }
        else
        {
            _txtPort.ForeColor = Color.Red;
        }
    }

    private void NumTimeout_ValueChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingControls) return;

        int val = (int)_numTimeout.Value;
        AppLogger.Info($"[ClientSettingsForm] Timeout changed: {val}s");
        _settings.RetryTimeout = val;
        _settings.Save();
        if (_settings.Mode == ClientMode.DualPc)
        {
            _udpListener.SetRetryTimeout(val);
        }
        _onTimeoutChanged?.Invoke(val);
    }

    private void TrkOpacity_ValueChanged(object? sender, EventArgs e)
    {
        int val = _trkOpacity.Value;
        _lblOpacityVal.Text = $"{val}%";
        AppLogger.Debug($"[ClientSettingsForm] Overlay opacity changed: {val}%");
        _settings.Opacity = val;
        _settings.Save();
        _overlayForm.UpdateAnimationSettings(val, _settings.PulseFrequency, _settings.AnimationCycles);
    }

    private void TrkFrequency_ValueChanged(object? sender, EventArgs e)
    {
        double freq = _trkFrequency.Value / 10.0;
        _lblFrequencyVal.Text = $"{freq:F1}s";
        AppLogger.Debug($"[ClientSettingsForm] Overlay pulse frequency changed: {freq:F1}s");
        _settings.PulseFrequency = freq;
        _settings.Save();
        _overlayForm.UpdateAnimationSettings(_settings.Opacity, freq, _settings.AnimationCycles);
    }

    private void TrkAnimationCycles_ValueChanged(object? sender, EventArgs e)
    {
        int val = _trkAnimationCycles.Value;
        _lblAnimationCyclesVal.Text = $"{val}";
        AppLogger.Debug($"[ClientSettingsForm] Animation cycles changed: {val}");
        _settings.AnimationCycles = val;
        _settings.Save();
        _overlayForm.UpdateAnimationSettings(_settings.Opacity, _settings.PulseFrequency, val);
    }

    private void TrkSize_ValueChanged(object? sender, EventArgs e)
    {
        int val = _trkSize.Value;
        _lblSizeVal.Text = $"{val}px";
        AppLogger.Debug($"[ClientSettingsForm] Overlay size changed: {val}px");
        _overlayForm.ApplyNewSize(val);
    }

    private void OnOverlayResized(int newSize)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => OnOverlayResized(newSize)));
            return;
        }

        if (_trkSize.Value != newSize)
        {
            _trkSize.Value = Math.Clamp(newSize, 32, 1024);
        }
        _lblSizeVal.Text = $"{newSize}px";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        AppLogger.Debug("[ClientSettingsForm] Dialog shown.");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        AppLogger.Debug("[ClientSettingsForm] Dialog closed.");
        _udpListener.DiscoveredServersUpdated -= OnDiscoveredServersUpdated;
        _udpListener.TargetServerConnectionChanged -= OnTargetServerConnectionChanged;
        _audioMonitor.DevicesChanged -= OnAudioDevicesChanged;
        _overlayForm.OverlayResized -= OnOverlayResized;
        _overlayForm.SetWysiwygMode(false);
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _udpListener.DiscoveredServersUpdated -= OnDiscoveredServersUpdated;
            _udpListener.TargetServerConnectionChanged -= OnTargetServerConnectionChanged;
            _audioMonitor.DevicesChanged -= OnAudioDevicesChanged;
            _overlayForm.OverlayResized -= OnOverlayResized;
            _overlayForm.SetWysiwygMode(false);
        }
        base.Dispose(disposing);
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
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stream-helper-client.log");
            }

            if (!File.Exists(path))
            {
                if (!_settings.DebugLogging)
                {
                    MessageBox.Show(
                        "Debug logging is currently disabled and no log file has been created yet.\n\nEnable 'Enable debug logging' to start recording logs.",
                        "Stream Helper Client Logs",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                File.WriteAllText(path, $"=== Stream Helper Client Log initialized on {DateTime.Now} ===" + Environment.NewLine);
            }

            AppLogger.Info($"[ClientSettingsForm] Opening log file '{path}'.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Error("[ClientSettingsForm] Failed to open log file", ex);
            MessageBox.Show($"Failed to open log file: {ex.Message}", "Open Log Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
