namespace StreamHelper.Shared.Obs;

public sealed class RollingFrameCounter
{
    private TimeSpan _window;
    private readonly TimeSpan _hysteresis;
    private readonly Queue<(DateTime Timestamp, long TotalSkipped)> _samples = new();
    private DateTime? _belowThresholdSince;
    private bool _isAlertActive;

    public bool IsAlertActive => _isAlertActive;
    public long CurrentDelta { get; private set; }

    public TimeSpan Window
    {
        get => _window;
        set => SetWindow(value);
    }

    public RollingFrameCounter(TimeSpan? window = null, TimeSpan? hysteresis = null)
    {
        _window = window ?? TimeSpan.FromSeconds(ObsSettingsConstants.DefaultSkippedFramesPeriodSeconds);
        _hysteresis = hysteresis ?? TimeSpan.FromSeconds(5);
    }

    public void SetWindow(TimeSpan window)
    {
        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Window must be positive.");
        }
        _window = window;
    }

    public bool Update(long currentTotalSkipped, int threshold, DateTime now, bool isOutputActive)
    {
        if (!isOutputActive)
        {
            Reset();
            return false;
        }

        _samples.Enqueue((now, currentTotalSkipped));

        // Purge samples older than window
        var cutoff = now - _window;
        while (_samples.Count > 1 && _samples.Peek().Timestamp < cutoff)
        {
            _samples.Dequeue();
        }

        long oldestTotal = _samples.Peek().TotalSkipped;
        long delta = currentTotalSkipped - oldestTotal;
        if (delta < 0) delta = 0;
        CurrentDelta = delta;

        if (delta > threshold)
        {
            _isAlertActive = true;
            _belowThresholdSince = null;
        }
        else
        {
            if (_isAlertActive)
            {
                _belowThresholdSince ??= now;
                if (now - _belowThresholdSince.Value >= _hysteresis)
                {
                    _isAlertActive = false;
                    _belowThresholdSince = null;
                }
            }
            else
            {
                _belowThresholdSince = null;
            }
        }

        return _isAlertActive;
    }

    public void Reset()
    {
        _samples.Clear();
        _belowThresholdSince = null;
        _isAlertActive = false;
        CurrentDelta = 0;
    }
}
