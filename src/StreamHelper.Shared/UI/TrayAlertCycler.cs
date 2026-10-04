using System.Drawing;
using StreamHelper.Shared.Common;
using StreamHelper.Shared.Protocol;

namespace StreamHelper.Shared.UI;

public sealed class TrayAlertCycler : IDisposable
{
    private readonly object _lock = new();
    private readonly SynchronizationContext? _syncContext;
    private System.Threading.Timer? _timer;
    private AlertFlags _currentAlerts = AlertFlags.None;
    private bool _isPaused;
    private int _alertIndex;
    private bool _disposed;

    public AlertFlags CurrentAlerts
    {
        get { lock (_lock) return _currentAlerts; }
    }

    public bool IsPaused
    {
        get { lock (_lock) return _isPaused; }
    }

    public event Action<Icon, string>? VisualChanged;

    public TrayAlertCycler(SynchronizationContext? syncContext = null)
    {
        _syncContext = syncContext ?? SynchronizationContext.Current;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _timer?.Dispose();
            _timer = new System.Threading.Timer(OnTimerTick, null, 1000, 1000);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public void SetState(AlertFlags alerts, bool isPaused)
    {
        bool stateChanged = false;
        lock (_lock)
        {
            if (_currentAlerts != alerts || _isPaused != isPaused)
            {
                stateChanged = true;
            }
            _currentAlerts = alerts;
            _isPaused = isPaused;
            _alertIndex = 0;
        }

        if (stateChanged)
        {
            AppLogger.Debug($"[TrayAlertCycler] State updated: alerts={alerts}, isPaused={isPaused}.");
        }

        UpdateVisual();
    }

    private void OnTimerTick(object? state)
    {
        lock (_lock)
        {
            if (_disposed || _isPaused) return;

            var active = AlertDisplayInfo.GetActiveAlertList(_currentAlerts);
            if (active.Count > 1)
            {
                _alertIndex = (_alertIndex + 1) % active.Count;
                AppLogger.Debug($"[TrayAlertCycler] Cycling tray icon to alert: {active[_alertIndex]} ({_alertIndex + 1}/{active.Count}).");
            }
            else
            {
                _alertIndex = 0;
            }
        }

        UpdateVisual();
    }

    public void UpdateVisual()
    {
        Icon icon;
        string tooltip;

        lock (_lock)
        {
            if (_disposed) return;

            if (_isPaused)
            {
                icon = StatusIconGenerator.GetPauseIcon();
                tooltip = AlertDisplayInfo.FormatTooltip(AlertFlags.None, isPaused: true);
            }
            else
            {
                var active = AlertDisplayInfo.GetActiveAlertList(_currentAlerts);
                if (active.Count == 0)
                {
                    icon = StatusIconGenerator.GetActiveIcon();
                    tooltip = AlertDisplayInfo.FormatTooltip(AlertFlags.None, isPaused: false);
                }
                else
                {
                    if (_alertIndex >= active.Count) _alertIndex = 0;
                    var currentAlert = active[_alertIndex];
                    icon = StatusIconGenerator.GetIconForAlert(currentAlert);
                    tooltip = AlertDisplayInfo.FormatTooltip(_currentAlerts, isPaused: false);
                }
            }
        }

        if (_syncContext != null && SynchronizationContext.Current != _syncContext)
        {
            _syncContext.Post(_ => VisualChanged?.Invoke(icon, tooltip), null);
        }
        else
        {
            VisualChanged?.Invoke(icon, tooltip);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
