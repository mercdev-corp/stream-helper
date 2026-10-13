using System.Drawing;
using System.Windows.Forms;

namespace StreamHelper.Shared.UI;

public sealed class AudioDetectionSettingsController
{
    private readonly CheckBox _checkBox;
    private readonly TrackBar _trackBar;
    private readonly Label _valueLabel;
    private readonly TrackBar? _lagTrackBar;
    private readonly Label? _lagValueLabel;

    private readonly Action<double, bool>? _onChanged;
    private readonly Action<double, bool, double>? _onChangedWithLag;
    private readonly Action<bool>? _saveSimpleMode;
    private readonly Action<double>? _saveTolerance;
    private readonly Action<double>? _saveLagWindow;

    private double _savedTolerance;
    private double _savedLagWindow;
    private bool _isUpdating;

    public CheckBox CheckBox => _checkBox;
    public TrackBar TrackBar => _trackBar;
    public Label ValueLabel => _valueLabel;
    public TrackBar? LagTrackBar => _lagTrackBar;
    public Label? LagValueLabel => _lagValueLabel;
    public double SavedTolerance => _savedTolerance;
    public double SavedLagWindow => _savedLagWindow;

    public AudioDetectionSettingsController(
        CheckBox checkBox,
        TrackBar trackBar,
        Label valueLabel,
        bool initialSimpleMode,
        double initialToleranceDb,
        Action<bool>? saveSimpleMode = null,
        Action<double>? saveTolerance = null,
        Action<double, bool>? onChanged = null)
        : this(
            checkBox,
            trackBar,
            valueLabel,
            lagTrackBar: null,
            lagValueLabel: null,
            initialSimpleMode: initialSimpleMode,
            initialToleranceDb: initialToleranceDb,
            initialLagWindowSeconds: 1.0,
            saveSimpleMode: saveSimpleMode,
            saveTolerance: saveTolerance,
            saveLagWindow: null,
            onChanged: onChanged,
            onChangedWithLag: null)
    {
    }

    public AudioDetectionSettingsController(
        CheckBox checkBox,
        TrackBar trackBar,
        Label valueLabel,
        TrackBar? lagTrackBar,
        Label? lagValueLabel,
        bool initialSimpleMode,
        double initialToleranceDb,
        double initialLagWindowSeconds = 1.0,
        Action<bool>? saveSimpleMode = null,
        Action<double>? saveTolerance = null,
        Action<double>? saveLagWindow = null,
        Action<double, bool>? onChanged = null,
        Action<double, bool, double>? onChangedWithLag = null)
    {
        _checkBox = checkBox ?? throw new ArgumentNullException(nameof(checkBox));
        _trackBar = trackBar ?? throw new ArgumentNullException(nameof(trackBar));
        _valueLabel = valueLabel ?? throw new ArgumentNullException(nameof(valueLabel));
        _lagTrackBar = lagTrackBar;
        _lagValueLabel = lagValueLabel;

        _saveSimpleMode = saveSimpleMode;
        _saveTolerance = saveTolerance;
        _saveLagWindow = saveLagWindow;
        _onChanged = onChanged;
        _onChangedWithLag = onChangedWithLag;

        _savedTolerance = Math.Clamp(initialToleranceDb, 0, 40);
        _savedLagWindow = Math.Clamp(initialLagWindowSeconds, 0.0, 3.0);

        _checkBox.CheckedChanged += HandleCheckBoxCheckedChanged;
        _trackBar.ValueChanged += HandleTrackBarValueChanged;
        if (_lagTrackBar != null)
        {
            _lagTrackBar.ValueChanged += HandleLagTrackBarValueChanged;
        }

        ApplyState(initialSimpleMode, _savedTolerance, _savedLagWindow);
    }

    public void ApplyState(bool simpleMode, double toleranceDb)
    {
        ApplyState(simpleMode, toleranceDb, _savedLagWindow);
    }

    public void ApplyState(bool simpleMode, double toleranceDb, double lagWindowSeconds)
    {
        _isUpdating = true;
        try
        {
            _savedTolerance = Math.Clamp(toleranceDb, 0, 40);
            _savedLagWindow = Math.Clamp(lagWindowSeconds, 0.0, 3.0);
            _checkBox.Checked = simpleMode;

            if (simpleMode)
            {
                _trackBar.Enabled = false;
                _trackBar.Value = _trackBar.Maximum;
                _valueLabel.Text = $"{_trackBar.Maximum} dB";
            }
            else
            {
                _trackBar.Enabled = true;
                _trackBar.Value = Math.Clamp((int)Math.Round(_savedTolerance), _trackBar.Minimum, _trackBar.Maximum);
                _valueLabel.Text = $"{_trackBar.Value} dB";
            }

            if (_lagTrackBar != null)
            {
                _lagTrackBar.Enabled = true;
                _lagTrackBar.Value = Math.Clamp((int)Math.Round(_savedLagWindow * 10.0), _lagTrackBar.Minimum, _lagTrackBar.Maximum);
                if (_lagValueLabel != null)
                {
                    _lagValueLabel.Text = $"{_savedLagWindow:F1} s";
                }
            }
        }
        finally
        {
            _isUpdating = false;
        }
    }

    private void HandleCheckBoxCheckedChanged(object? sender, EventArgs e)
    {
        if (_isUpdating) return;

        bool isSimple = _checkBox.Checked;
        _isUpdating = true;
        try
        {
            if (isSimple)
            {
                _trackBar.Enabled = false;
                _trackBar.Value = _trackBar.Maximum;
                _valueLabel.Text = $"{_trackBar.Maximum} dB";
            }
            else
            {
                _trackBar.Enabled = true;
                _trackBar.Value = Math.Clamp((int)Math.Round(_savedTolerance), _trackBar.Minimum, _trackBar.Maximum);
                _valueLabel.Text = $"{_trackBar.Value} dB";
            }

            if (_lagTrackBar != null)
            {
                _lagTrackBar.Enabled = true;
                _lagTrackBar.Value = Math.Clamp((int)Math.Round(_savedLagWindow * 10.0), _lagTrackBar.Minimum, _lagTrackBar.Maximum);
                if (_lagValueLabel != null)
                {
                    _lagValueLabel.Text = $"{_savedLagWindow:F1} s";
                }
            }
        }
        finally
        {
            _isUpdating = false;
        }

        _saveSimpleMode?.Invoke(isSimple);
        _onChanged?.Invoke(_savedTolerance, isSimple);
        _onChangedWithLag?.Invoke(_savedTolerance, isSimple, _savedLagWindow);
    }

    private void HandleTrackBarValueChanged(object? sender, EventArgs e)
    {
        if (_isUpdating) return;

        if (!_checkBox.Checked)
        {
            _savedTolerance = _trackBar.Value;
            _valueLabel.Text = $"{_trackBar.Value} dB";

            _saveTolerance?.Invoke(_savedTolerance);
            _onChanged?.Invoke(_savedTolerance, false);
            _onChangedWithLag?.Invoke(_savedTolerance, false, _savedLagWindow);
        }
    }

    private void HandleLagTrackBarValueChanged(object? sender, EventArgs e)
    {
        if (_isUpdating || _lagTrackBar == null) return;

        _savedLagWindow = Math.Clamp(_lagTrackBar.Value / 10.0, 0.0, 3.0);
        if (_lagValueLabel != null)
        {
            _lagValueLabel.Text = $"{_savedLagWindow:F1} s";
        }

        _saveLagWindow?.Invoke(_savedLagWindow);
        _onChangedWithLag?.Invoke(_savedTolerance, _checkBox.Checked, _savedLagWindow);
    }
}
