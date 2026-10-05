using System.Drawing;
using System.Windows.Forms;

namespace StreamHelper.Shared.UI;

public sealed class AudioDetectionSettingsController
{
    private readonly CheckBox _checkBox;
    private readonly TrackBar _trackBar;
    private readonly Label _valueLabel;
    private readonly Action<double, bool>? _onChanged;
    private readonly Action<bool>? _saveSimpleMode;
    private readonly Action<double>? _saveTolerance;

    private double _savedTolerance;
    private bool _isUpdating;

    public CheckBox CheckBox => _checkBox;
    public TrackBar TrackBar => _trackBar;
    public Label ValueLabel => _valueLabel;
    public double SavedTolerance => _savedTolerance;

    public AudioDetectionSettingsController(
        CheckBox checkBox,
        TrackBar trackBar,
        Label valueLabel,
        bool initialSimpleMode,
        double initialToleranceDb,
        Action<bool>? saveSimpleMode = null,
        Action<double>? saveTolerance = null,
        Action<double, bool>? onChanged = null)
    {
        _checkBox = checkBox ?? throw new ArgumentNullException(nameof(checkBox));
        _trackBar = trackBar ?? throw new ArgumentNullException(nameof(trackBar));
        _valueLabel = valueLabel ?? throw new ArgumentNullException(nameof(valueLabel));
        _saveSimpleMode = saveSimpleMode;
        _saveTolerance = saveTolerance;
        _onChanged = onChanged;

        _savedTolerance = Math.Clamp(initialToleranceDb, 0, 40);

        _checkBox.CheckedChanged += HandleCheckBoxCheckedChanged;
        _trackBar.ValueChanged += HandleTrackBarValueChanged;

        ApplyState(initialSimpleMode, _savedTolerance);
    }

    public void ApplyState(bool simpleMode, double toleranceDb)
    {
        _isUpdating = true;
        try
        {
            _savedTolerance = Math.Clamp(toleranceDb, 0, 40);
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
        }
        finally
        {
            _isUpdating = false;
        }

        _saveSimpleMode?.Invoke(isSimple);
        _onChanged?.Invoke(_savedTolerance, isSimple);
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
        }
    }
}
