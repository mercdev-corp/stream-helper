using System.Drawing;
using System.Windows.Forms;
using StreamHelper.Shared.Audio;

namespace StreamHelper.Shared.UI;

public sealed class MicrophoneSelectionController
{
    private readonly ComboBox _comboBox;
    private readonly IAudioMonitor _audioMonitor;
    private readonly Action<MicComboItem>? _onMicrophoneChanged;
    private bool _isPopulating;

    public MicrophoneSelectionController(
        ComboBox comboBox,
        IAudioMonitor audioMonitor,
        Action<MicComboItem>? onMicrophoneChanged = null)
    {
        _comboBox = comboBox ?? throw new ArgumentNullException(nameof(comboBox));
        _audioMonitor = audioMonitor ?? throw new ArgumentNullException(nameof(audioMonitor));
        _onMicrophoneChanged = onMicrophoneChanged;

        _comboBox.DrawMode = DrawMode.OwnerDrawFixed;
        _comboBox.ItemHeight = 22;
        _comboBox.DrawItem -= HandleDrawItem;
        _comboBox.DrawItem += HandleDrawItem;
        _comboBox.SelectedIndexChanged -= HandleSelectedIndexChanged;
        _comboBox.SelectedIndexChanged += HandleSelectedIndexChanged;
    }

    public ComboBox ComboBox => _comboBox;
    public MicComboItem? SelectedItem => _comboBox.SelectedItem as MicComboItem;
    public string? SelectedId => SelectedItem?.Id;
    public string? SelectedDisplayName => SelectedItem?.DisplayName;

    public void Populate(string? savedId, string? savedName)
    {
        _isPopulating = true;
        try
        {
            _comboBox.Items.Clear();

            var activeDevices = _audioMonitor.GetActiveCaptureDevices();

            bool savedFound = false;
            if (!string.IsNullOrEmpty(savedId))
            {
                savedFound = activeDevices.Any(d => string.Equals(d.Id, savedId, StringComparison.OrdinalIgnoreCase));
            }

            // If saved mic is not found and was configured, add as first item with missing flag
            if (!savedFound && !string.IsNullOrEmpty(savedId))
            {
                var missingName = !string.IsNullOrEmpty(savedName) ? savedName : "(Missing Microphone)";
                var missingItem = new MicComboItem(savedId, missingName, IsMissing: true);
                _comboBox.Items.Add(missingItem);
                _comboBox.SelectedItem = missingItem;
            }

            // Add currently connected devices in alphabetical order
            var activeItems = activeDevices
                .Select(dev => new MicComboItem(dev.Id, dev.Name + GetDeviceBadge(dev), IsMissing: false))
                .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            foreach (var item in activeItems)
            {
                _comboBox.Items.Add(item);

                if (savedFound && string.Equals(item.Id, savedId, StringComparison.OrdinalIgnoreCase))
                {
                    _comboBox.SelectedItem = item;
                }
            }

            // If nothing selected yet, select first or default
            if (_comboBox.SelectedItem == null && _comboBox.Items.Count > 0)
            {
                _comboBox.SelectedIndex = 0;
                if (_comboBox.SelectedItem is MicComboItem selectedItem && !selectedItem.IsMissing)
                {
                    _onMicrophoneChanged?.Invoke(selectedItem);
                }
            }
        }
        finally
        {
            _isPopulating = false;
        }
    }

    private void HandleSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isPopulating) return;

        if (_comboBox.SelectedItem is MicComboItem selectedItem && !selectedItem.IsMissing)
        {
            _onMicrophoneChanged?.Invoke(selectedItem);
        }
    }

    private void HandleDrawItem(object? sender, DrawItemEventArgs e)
    {
        DrawItem(_comboBox, e);
    }

    public static void DrawItem(ComboBox comboBox, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= comboBox.Items.Count) return;

        e.DrawBackground();

        if (comboBox.Items[e.Index] is MicComboItem item)
        {
            using var brush = new SolidBrush(item.IsMissing ? Color.Red : e.ForeColor);
            var fontStyle = item.IsMissing ? (e.Font?.Style ?? FontStyle.Regular) | FontStyle.Strikeout : (e.Font?.Style ?? FontStyle.Regular);
            using var font = new Font(e.Font ?? SystemFonts.DefaultFont, fontStyle);

            e.Graphics.DrawString(item.DisplayName, font, brush, e.Bounds.X + 2, e.Bounds.Y + 2);
        }

        e.DrawFocusRectangle();
    }

    public static string GetDeviceBadge(AudioDeviceInfo dev)
    {
        bool isConsoleDefault = dev.IsDefaultConsole || (dev.IsDefault && !dev.IsDefaultCommunications);

        if (isConsoleDefault)
        {
            return " (Default)";
        }

        if (dev.IsDefaultCommunications)
        {
            return " (Default Communications)";
        }

        if (dev.IsDefault)
        {
            return " (Default)";
        }

        return "";
    }
}
