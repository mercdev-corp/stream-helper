using StreamHelper.Shared.Common;

namespace StreamHelper.Shared.Audio;

public sealed class AudioCorrelationEngine
{
    private readonly object _lock = new();
    private readonly ConditionalSilenceDetector _silenceDetector;
    private readonly PearsonCorrelationEngine _correlationEngine;

    private readonly List<(long TimestampMs, double RmsDbfs)> _obsMeterHistory = new();
    private const int MaxHistoryRetentionMs = 20000;

    private double _latestObsRmsDbfs = -100.0;
    private DateTime _lastObsMeterTime = DateTime.MinValue;

    private bool _isObsDeviceDisconnected;
    private bool _isObsDeviceMuted;

    private bool _simpleDetection;
    private double _toleranceDb = 20.0;

    private bool _hasSoundIssue;

    public bool HasSoundIssue
    {
        get { lock (_lock) return _hasSoundIssue; }
        private set
        {
            bool changed = false;
            lock (_lock)
            {
                if (_hasSoundIssue != value)
                {
                    _hasSoundIssue = value;
                    changed = true;
                }
            }
            if (changed)
            {
                AppLogger.Warn($"[AudioCorrelation] Sound issue state changed: hasSoundIssue={value} (SilenceIssue={_silenceDetector.IsAlertActive}, CorrelationIssue={_correlationEngine.IsAlertActive}).");
                SoundIssueChanged?.Invoke(value);
            }
        }
    }

    public bool SimpleDetection
    {
        get { lock (_lock) return _simpleDetection; }
    }

    public double ToleranceDb
    {
        get { lock (_lock) return _toleranceDb; }
    }

    public event Action<bool>? SoundIssueChanged;

    public AudioCorrelationEngine(
        ConditionalSilenceDetector? silenceDetector = null,
        PearsonCorrelationEngine? correlationEngine = null,
        double toleranceDb = 20.0,
        bool simpleMode = false)
    {
        _silenceDetector = silenceDetector ?? new ConditionalSilenceDetector();
        _correlationEngine = correlationEngine ?? new PearsonCorrelationEngine(toleranceDb: toleranceDb);
        _toleranceDb = Math.Clamp(correlationEngine != null && toleranceDb == 20.0 ? correlationEngine.ToleranceDb : toleranceDb, 0, 40);
        _correlationEngine.ToleranceDb = _toleranceDb;
        _simpleDetection = simpleMode;
    }

    public void Configure(double toleranceDb, bool simpleMode)
    {
        bool modeChanged = false;
        bool toleranceChanged = false;
        double oldTolerance;
        bool oldMode;

        lock (_lock)
        {
            oldTolerance = _toleranceDb;
            oldMode = _simpleDetection;

            double clampedTolerance = Math.Clamp(toleranceDb, 0, 40);
            if (Math.Abs(_toleranceDb - clampedTolerance) > 1e-4)
            {
                _toleranceDb = clampedTolerance;
                _correlationEngine.ToleranceDb = clampedTolerance;
                toleranceChanged = true;
            }

            if (_simpleDetection != simpleMode)
            {
                _simpleDetection = simpleMode;
                modeChanged = true;
                _correlationEngine.Reset();
            }
        }

        if (toleranceChanged)
        {
            AppLogger.Info($"[AudioCorrelation] Tolerance changed from {oldTolerance:F1} dB to {_toleranceDb:F1} dB.");
        }

        if (modeChanged)
        {
            AppLogger.Info($"[AudioCorrelation] SimpleDetection changed from {oldMode} to {simpleMode}.");
        }

        EvaluateSoundIssue();
    }

    public void UpdateSuppressionStates(
        bool obsDeviceDisconnected,
        bool obsDeviceMuted)
    {
        bool changed = false;
        lock (_lock)
        {
            if (_isObsDeviceDisconnected != obsDeviceDisconnected || _isObsDeviceMuted != obsDeviceMuted)
            {
                _isObsDeviceDisconnected = obsDeviceDisconnected;
                _isObsDeviceMuted = obsDeviceMuted;
                changed = true;
            }
        }

        if (changed)
        {
            AppLogger.Debug($"[AudioCorrelation] Suppression states updated: obsDisconnected={obsDeviceDisconnected}, obsMuted={obsDeviceMuted}.");
        }

        EvaluateSoundIssue();
    }

    public void UpdateSuppressionStates(
        bool micDisconnected,
        bool micMuted,
        bool obsDeviceDisconnected,
        bool obsDeviceMuted)
    {
        // Mic states belong to the microphone monitor, not the game audio correlation engine.
        // Alert Suppression Hierarchy: OBS capture device disconnected -> OBS capture device muted -> sound issue.
        UpdateSuppressionStates(obsDeviceDisconnected, obsDeviceMuted);
    }

    public void IngestObsAudioMeter(double rmsDbfs, double peakDbfs, DateTime timestamp)
    {
        lock (_lock)
        {
            _latestObsRmsDbfs = rmsDbfs;
            _lastObsMeterTime = timestamp;

            long ms = new DateTimeOffset(timestamp.Kind == DateTimeKind.Utc ? timestamp : timestamp.ToUniversalTime()).ToUnixTimeMilliseconds();
            _obsMeterHistory.Add((ms, rmsDbfs));

            long pruneBefore = ms - MaxHistoryRetentionMs;
            int removeCount = 0;
            while (removeCount < _obsMeterHistory.Count && _obsMeterHistory[removeCount].TimestampMs < pruneBefore)
            {
                removeCount++;
            }
            if (removeCount > 0)
            {
                _obsMeterHistory.RemoveRange(0, removeCount);
            }
        }
    }

    private double GetCurrentObsRms()
    {
        lock (_lock)
        {
            if (_lastObsMeterTime == DateTime.MinValue || (DateTime.UtcNow - _lastObsMeterTime) > TimeSpan.FromMilliseconds(1500))
            {
                return -100.0;
            }
            return _latestObsRmsDbfs;
        }
    }

    // Direct in-memory feed for Single-PC mode (called per 100ms reading)
    public void ProcessClientReading(double clientRmsDbfs)
    {
        double obsRms = GetCurrentObsRms();

        _silenceDetector.ProcessReading(clientRmsDbfs, obsRms);

        lock (_lock)
        {
            if (!_simpleDetection)
            {
                _correlationEngine.AddSamples(clientRmsDbfs, obsRms);
            }
        }

        EvaluateSoundIssue();
    }

    // Ingestion for Dual-PC mode (called per 1-second UDP packet containing 10 readings)
    public void ProcessTelemetryPacket(AudioTelemetryPacket packet)
    {
        if (packet.Readings == null || packet.Readings.Length == 0) return;

        int count = packet.Readings.Length;
        long serverNowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // If packet timestamp is reasonable and closely synchronized with server time (within 3s),
        // use it to align samples; otherwise map readings relative to server arrival time
        // to prevent cross-machine clock drift from desynchronizing meters.
        long baseTimeMs = (packet.TimestampUnixMs > 0 && Math.Abs(serverNowMs - packet.TimestampUnixMs) <= 3000)
            ? packet.TimestampUnixMs
            : serverNowMs;

        lock (_lock)
        {
            for (int i = 0; i < count; i++)
            {
                double reading = packet.Readings[i];
                long sampleTimeMs = baseTimeMs - (long)((count - 1 - i) * 100);

                double obsRms = ResolveObsRmsAtTime(sampleTimeMs);

                _silenceDetector.ProcessReading(reading, obsRms);
                if (!_simpleDetection)
                {
                    _correlationEngine.AddSamples(reading, obsRms);
                }
            }
        }

        EvaluateSoundIssue();
    }

    private double ResolveObsRmsAtTime(long sampleTimeMs)
    {
        if (_obsMeterHistory.Count == 0)
        {
            return GetCurrentObsRms();
        }

        int bestIdx = -1;
        long bestDiff = long.MaxValue;

        for (int j = _obsMeterHistory.Count - 1; j >= 0; j--)
        {
            long diff = Math.Abs(_obsMeterHistory[j].TimestampMs - sampleTimeMs);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestIdx = j;
            }
            if (_obsMeterHistory[j].TimestampMs < sampleTimeMs && diff > bestDiff)
            {
                break;
            }
        }

        // Allow up to 3000ms match to accommodate HDMI duplication latency / buffering
        if (bestIdx >= 0 && bestDiff <= 3000)
        {
            return _obsMeterHistory[bestIdx].RmsDbfs;
        }

        // If OBS is currently active but nearest history sample is slightly beyond window, return latest
        if (_lastObsMeterTime != DateTime.MinValue && (DateTime.UtcNow - _lastObsMeterTime) <= TimeSpan.FromMilliseconds(1500))
        {
            return _latestObsRmsDbfs;
        }

        return -100.0;
    }

    private void EvaluateSoundIssue()
    {
        bool suppressed;
        bool simpleMode;
        lock (_lock)
        {
            // Alert Suppression Hierarchy:
            // OBS Device Disconnected -> OBS Device Muted -> Correlation Sound Issue
            suppressed = _isObsDeviceDisconnected || _isObsDeviceMuted;
            simpleMode = _simpleDetection;
        }

        if (suppressed)
        {
            HasSoundIssue = false;
            return;
        }

        bool silenceIssue = _silenceDetector.IsAlertActive;
        bool correlationIssue = !simpleMode && _correlationEngine.IsAlertActive;

        HasSoundIssue = silenceIssue || correlationIssue;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _latestObsRmsDbfs = -100.0;
            _lastObsMeterTime = DateTime.MinValue;
            _obsMeterHistory.Clear();
            _hasSoundIssue = false;
        }

        _silenceDetector.Reset();
        _correlationEngine.Reset();
    }
}
