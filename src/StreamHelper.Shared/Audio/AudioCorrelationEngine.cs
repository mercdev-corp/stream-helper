using StreamHelper.Shared.Common;
using StreamHelper.Shared.Protocol;

namespace StreamHelper.Shared.Audio;

public sealed class AudioCorrelationEngine : IDisposable
{
    private readonly object _lock = new();
    private readonly ConditionalSilenceDetector _silenceDetector;
    private readonly PearsonCorrelationEngine _correlationEngine;

    private readonly List<(long TimestampMs, double RmsDbfs)> _obsMeterHistory = new();
    private const int MaxHistoryRetentionMs = 20000;
    private double? _clockOffsetMs;
    private DateTime _lastPacketTime = DateTime.MinValue;

    private double _latestObsRmsDbfs = -100.0;
    private DateTime _lastObsMeterTime = DateTime.MinValue;

    private bool _isObsDeviceDisconnected;
    private bool _isObsDeviceMuted;
    private bool _isNotInActiveScene;

    private bool _simpleDetection;
    private double _toleranceDb = 20.0;
    private double _lagWindowSeconds = 1.0;
    private int _retryTimeoutSeconds = ProtocolConstants.DefaultRetryTimeoutSeconds;
    private DateTime? _lastAudioIssueTime;
    private readonly System.Threading.Timer _expirationTimer;

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

    public double LagWindowSeconds
    {
        get { lock (_lock) return _lagWindowSeconds; }
    }

    public int RetryTimeoutSeconds
    {
        get { lock (_lock) return _retryTimeoutSeconds; }
    }

    public DateTime? LastAudioIssueTime
    {
        get { lock (_lock) return _lastAudioIssueTime; }
    }

    public event Action<bool>? SoundIssueChanged;

    public AudioCorrelationEngine(
        ConditionalSilenceDetector? silenceDetector = null,
        PearsonCorrelationEngine? correlationEngine = null,
        double toleranceDb = 20.0,
        bool simpleMode = false,
        double lagWindowSeconds = 1.0,
        int retryTimeoutSeconds = ProtocolConstants.DefaultRetryTimeoutSeconds)
    {
        _silenceDetector = silenceDetector ?? new ConditionalSilenceDetector();
        _correlationEngine = correlationEngine ?? new PearsonCorrelationEngine(toleranceDb: toleranceDb);
        _toleranceDb = Math.Clamp(correlationEngine != null && toleranceDb == 20.0 ? correlationEngine.ToleranceDb : toleranceDb, 0, 40);
        _correlationEngine.ToleranceDb = _toleranceDb;
        _simpleDetection = simpleMode;
        _lagWindowSeconds = Math.Clamp(lagWindowSeconds, 0.0, 3.0);
        _correlationEngine.SetLagWindow(_lagWindowSeconds);
        _retryTimeoutSeconds = Math.Clamp(retryTimeoutSeconds, 1, 300);
        _expirationTimer = new System.Threading.Timer(OnExpirationTimerFired, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void SetRetryTimeout(int retryTimeoutSeconds)
    {
        int clamped = Math.Clamp(retryTimeoutSeconds, 1, 300);
        bool changed = false;
        int oldTimeout;
        lock (_lock)
        {
            oldTimeout = _retryTimeoutSeconds;
            if (_retryTimeoutSeconds != clamped)
            {
                _retryTimeoutSeconds = clamped;
                changed = true;
            }
        }

        if (changed)
        {
            AppLogger.Info($"[AudioCorrelation] RetryTimeout changed from {oldTimeout}s to {clamped}s.");
            EvaluateSoundIssue();
        }
    }

    public void Configure(double toleranceDb, bool simpleMode)
    {
        Configure(toleranceDb, simpleMode, _lagWindowSeconds, _retryTimeoutSeconds);
    }

    public void Configure(double toleranceDb, bool simpleMode, double lagWindowSeconds)
    {
        Configure(toleranceDb, simpleMode, lagWindowSeconds, _retryTimeoutSeconds);
    }

    public void Configure(double toleranceDb, bool simpleMode, double lagWindowSeconds, int retryTimeoutSeconds)
    {
        bool modeChanged = false;
        bool toleranceChanged = false;
        bool lagChanged = false;
        bool timeoutChanged = false;
        double oldTolerance;
        bool oldMode;
        double oldLag;
        int oldTimeout;

        lock (_lock)
        {
            oldTolerance = _toleranceDb;
            oldMode = _simpleDetection;
            oldLag = _lagWindowSeconds;
            oldTimeout = _retryTimeoutSeconds;

            double clampedTolerance = Math.Clamp(toleranceDb, 0, 40);
            if (Math.Abs(_toleranceDb - clampedTolerance) > 1e-4)
            {
                _toleranceDb = clampedTolerance;
                _correlationEngine.ToleranceDb = clampedTolerance;
                toleranceChanged = true;
            }

            double clampedLag = Math.Clamp(lagWindowSeconds, 0.0, 3.0);
            if (Math.Abs(_lagWindowSeconds - clampedLag) > 1e-4)
            {
                _lagWindowSeconds = clampedLag;
                _correlationEngine.SetLagWindow(_lagWindowSeconds);
                lagChanged = true;
            }

            int clampedTimeout = Math.Clamp(retryTimeoutSeconds, 1, 300);
            if (_retryTimeoutSeconds != clampedTimeout)
            {
                _retryTimeoutSeconds = clampedTimeout;
                timeoutChanged = true;
            }

            if (_simpleDetection != simpleMode)
            {
                _simpleDetection = simpleMode;
                modeChanged = true;
                _silenceDetector.Reset();
                _correlationEngine.Reset();
                _lastAudioIssueTime = null;
                DisarmExpirationTimerLocked();
            }
        }

        if (toleranceChanged)
        {
            AppLogger.Info($"[AudioCorrelation] Tolerance changed from {oldTolerance:F1} dB to {_toleranceDb:F1} dB.");
        }

        if (lagChanged)
        {
            AppLogger.Info($"[AudioCorrelation] LagWindowSeconds changed from {oldLag:F1}s to {_lagWindowSeconds:F1}s.");
        }

        if (timeoutChanged)
        {
            AppLogger.Info($"[AudioCorrelation] RetryTimeout changed from {oldTimeout}s to {_retryTimeoutSeconds}s.");
        }

        if (modeChanged)
        {
            AppLogger.Info($"[AudioCorrelation] SimpleDetection changed from {oldMode} to {simpleMode}.");
        }

        EvaluateSoundIssue();
    }

    public void UpdateSuppressionStates(
        bool obsDeviceDisconnected,
        bool obsDeviceMuted,
        bool isNotInActiveScene = false)
    {
        bool changed = false;
        lock (_lock)
        {
            if (_isObsDeviceDisconnected != obsDeviceDisconnected ||
                _isObsDeviceMuted != obsDeviceMuted ||
                _isNotInActiveScene != isNotInActiveScene)
            {
                _isObsDeviceDisconnected = obsDeviceDisconnected;
                _isObsDeviceMuted = obsDeviceMuted;
                _isNotInActiveScene = isNotInActiveScene;
                changed = true;
            }
        }

        if (changed)
        {
            AppLogger.Debug($"[AudioCorrelation] Suppression states updated: obsDisconnected={obsDeviceDisconnected}, obsMuted={obsDeviceMuted}, isNotInActiveScene={isNotInActiveScene}.");
        }

        EvaluateSoundIssue();
    }

    public void UpdateSuppressionStates(
        bool micDisconnected,
        bool micMuted,
        bool obsDeviceDisconnected,
        bool obsDeviceMuted,
        bool isNotInActiveScene = false)
    {
        // Mic states belong to the microphone monitor, not the game audio correlation engine.
        // Alert Suppression Hierarchy: OBS capture device disconnected -> OBS capture device muted -> sound issue.
        UpdateSuppressionStates(obsDeviceDisconnected, obsDeviceMuted, isNotInActiveScene);
    }

    public void IngestObsAudioMeter(double rmsDbfs, double peakDbfs, DateTime timestamp)
    {
        bool shouldEvaluate = false;
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

            if (rmsDbfs >= _silenceDetector.ObsSilenceThresholdDbfs)
            {
                _silenceDetector.Reset();
                if (_hasSoundIssue)
                {
                    _lastAudioIssueTime = null;
                    DisarmExpirationTimerLocked();
                    shouldEvaluate = true;
                }
            }
            else if (_hasSoundIssue)
            {
                shouldEvaluate = true;
            }
        }

        if (shouldEvaluate)
        {
            EvaluateSoundIssue();
        }
    }

    private double GetCurrentObsRms()
    {
        lock (_lock)
        {
            return GetCurrentObsRmsLocked();
        }
    }

    private double GetCurrentObsRmsLocked()
    {
        if (_lastObsMeterTime == DateTime.MinValue || (DateTime.UtcNow - _lastObsMeterTime) > TimeSpan.FromMilliseconds(1500))
        {
            return -100.0;
        }
        return _latestObsRmsDbfs;
    }

    // Direct in-memory feed for Single-PC mode (called per 100ms reading)
    public void ProcessClientReading(double clientRmsDbfs, long? sampleTimeMs = null)
    {
        long timeMs = sampleTimeMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool anyReadingConfirmed = false;

        lock (_lock)
        {
            ProcessSingleSampleLocked(clientRmsDbfs, timeMs, ref anyReadingConfirmed);
        }

        if (anyReadingConfirmed)
        {
            lock (_lock)
            {
                _lastAudioIssueTime = DateTime.UtcNow;
            }
        }

        EvaluateSoundIssue();
    }

    // Ingestion for Dual-PC mode (called per 1-second UDP packet containing 10 readings)
    public void ProcessTelemetryPacket(AudioTelemetryPacket packet)
    {
        if (packet.Readings == null || packet.Readings.Length == 0) return;

        int count = packet.Readings.Length;
        DateTime nowUtc = DateTime.UtcNow;
        long serverNowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool anyPacketConfirmed = false;

        lock (_lock)
        {
            long referenceTimeMs = serverNowMs;
            if (_obsMeterHistory.Count > 0 && Math.Abs(_obsMeterHistory[^1].TimestampMs - serverNowMs) > 60000)
            {
                referenceTimeMs = _obsMeterHistory[^1].TimestampMs;
            }

            long rawOffsetMs = referenceTimeMs - packet.TimestampUnixMs;
            if (!_clockOffsetMs.HasValue || (nowUtc - _lastPacketTime) > TimeSpan.FromSeconds(10) || Math.Abs(rawOffsetMs - _clockOffsetMs.Value) > 5000)
            {
                _clockOffsetMs = rawOffsetMs;
            }
            else
            {
                _clockOffsetMs = (_clockOffsetMs.Value * 0.8) + (rawOffsetMs * 0.2);
            }
            _lastPacketTime = nowUtc;

            long effectiveOffsetMs = (long)Math.Round(_clockOffsetMs.Value);
            long baseTimeMs = packet.TimestampUnixMs + effectiveOffsetMs;
            if (Math.Abs(referenceTimeMs - serverNowMs) <= 60000 && baseTimeMs > serverNowMs)
            {
                baseTimeMs = serverNowMs;
            }

            for (int i = 0; i < count; i++)
            {
                double reading = packet.Readings[i];
                long sampleTimeMs = baseTimeMs - (long)((count - 1 - i) * 100);
                ProcessSingleSampleLocked(reading, sampleTimeMs, ref anyPacketConfirmed);
            }
        }

        if (anyPacketConfirmed)
        {
            lock (_lock)
            {
                _lastAudioIssueTime = DateTime.UtcNow;
            }
        }

        EvaluateSoundIssue();
    }

    private void ProcessSingleSampleLocked(double clientRmsDbfs, long sampleTimeMs, ref bool anyReadingConfirmed)
    {
        double obsSilenceRms = ResolveObsRmsForSilenceDetectionLocked(sampleTimeMs);
        double obsCorrRms = ResolveObsRmsAtTimeLocked(sampleTimeMs);

        _silenceDetector.ProcessReading(clientRmsDbfs, obsSilenceRms);

        if (!_simpleDetection)
        {
            _correlationEngine.AddSamples(clientRmsDbfs, obsCorrRms);
        }

        bool confirmed = _simpleDetection
            ? _silenceDetector.IsActivelyConfirmingIssue
            : (_silenceDetector.IsActivelyConfirmingIssue || _correlationEngine.IsActivelyConfirmingIssue);

        if (confirmed)
        {
            anyReadingConfirmed = true;
        }
    }

    private double ResolveObsRmsForSilenceDetectionLocked(long sampleTimeMs)
    {
        double currentObsRms = ResolveObsRmsAtTimeLocked(sampleTimeMs);
        if (currentObsRms >= _silenceDetector.ObsSilenceThresholdDbfs)
        {
            return currentObsRms;
        }

        long lagWindowMs = (long)Math.Round(_lagWindowSeconds * 1000);
        if (lagWindowMs <= 0)
        {
            return currentObsRms;
        }

        if (_lastObsMeterTime == DateTime.MinValue || (DateTime.UtcNow - _lastObsMeterTime) > TimeSpan.FromMilliseconds(1500))
        {
            return -100.0;
        }

        if (_obsMeterHistory.Count == 0)
        {
            return currentObsRms;
        }

        long windowEndMs = sampleTimeMs + lagWindowMs;

        double maxRmsInWindow = currentObsRms;
        for (int i = _obsMeterHistory.Count - 1; i >= 0; i--)
        {
            var (ts, rms) = _obsMeterHistory[i];
            if (ts > sampleTimeMs && ts <= windowEndMs)
            {
                if (rms > maxRmsInWindow)
                {
                    maxRmsInWindow = rms;
                }
                if (rms >= _silenceDetector.ObsSilenceThresholdDbfs)
                {
                    return rms;
                }
            }
            if (ts <= sampleTimeMs)
            {
                break;
            }
        }

        return maxRmsInWindow;
    }

    private double ResolveObsRmsAtTimeLocked(long sampleTimeMs)
    {
        if (_lastObsMeterTime == DateTime.MinValue || (DateTime.UtcNow - _lastObsMeterTime) > TimeSpan.FromMilliseconds(1500))
        {
            return -100.0;
        }

        if (_obsMeterHistory.Count == 0)
        {
            return GetCurrentObsRmsLocked();
        }

        int bestIdx = _obsMeterHistory.Count - 1;
        long bestDiff = Math.Abs(_obsMeterHistory[bestIdx].TimestampMs - sampleTimeMs);

        for (int j = _obsMeterHistory.Count - 2; j >= 0; j--)
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

        return _latestObsRmsDbfs;
    }

    private void ArmExpirationTimerLocked(TimeSpan duration)
    {
        try
        {
            _expirationTimer.Change(duration, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void DisarmExpirationTimerLocked()
    {
        try
        {
            _expirationTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnExpirationTimerFired(object? state)
    {
        bool shouldClear = false;
        lock (_lock)
        {
            if (_hasSoundIssue)
            {
                if (_lastAudioIssueTime.HasValue)
                {
                    TimeSpan elapsed = DateTime.UtcNow - _lastAudioIssueTime.Value;
                    if (elapsed >= TimeSpan.FromSeconds(_retryTimeoutSeconds))
                    {
                        shouldClear = true;
                        _lastAudioIssueTime = null;
                        _silenceDetector.Reset();
                        _correlationEngine.Reset();
                        DisarmExpirationTimerLocked();
                    }
                    else
                    {
                        TimeSpan remaining = TimeSpan.FromSeconds(_retryTimeoutSeconds) - elapsed;
                        if (remaining > TimeSpan.Zero)
                        {
                            ArmExpirationTimerLocked(remaining);
                        }
                        else
                        {
                            shouldClear = true;
                            _lastAudioIssueTime = null;
                            _silenceDetector.Reset();
                            _correlationEngine.Reset();
                            DisarmExpirationTimerLocked();
                        }
                    }
                }
                else
                {
                    shouldClear = true;
                    _lastAudioIssueTime = null;
                    _silenceDetector.Reset();
                    _correlationEngine.Reset();
                    DisarmExpirationTimerLocked();
                }
            }
        }

        if (shouldClear)
        {
            HasSoundIssue = false;
        }
    }

    private void EvaluateSoundIssue()
    {
        bool suppressed;
        bool simpleMode;
        lock (_lock)
        {
            // Alert Suppression Hierarchy:
            // OBS Device Disconnected -> OBS Device Muted -> Not In Active Scene -> Correlation Sound Issue
            suppressed = _isObsDeviceDisconnected || _isObsDeviceMuted || _isNotInActiveScene;
            simpleMode = _simpleDetection;
        }

        if (suppressed)
        {
            lock (_lock)
            {
                DisarmExpirationTimerLocked();
            }
            HasSoundIssue = false;
            return;
        }

        bool silenceIssue = _silenceDetector.IsAlertActive;
        bool correlationIssue = !simpleMode && _correlationEngine.IsAlertActive;
        bool obsHasAudio = GetCurrentObsRms() >= _silenceDetector.ObsSilenceThresholdDbfs;

        if (!simpleMode)
        {
            // Full mode: verified correlation suppresses spurious silence alerts
            // ONLY when correlation engine has valid calculation AND OBS actually has non-silent audio!
            if (_correlationEngine.HasValidCalculation && obsHasAudio &&
                (_correlationEngine.LastCalculatedMaxCorrelation >= _correlationEngine.MinCorrelationThreshold ||
                 _correlationEngine.LastCalculatedDifference <= _correlationEngine.ToleranceDb))
            {
                silenceIssue = false;
            }
        }

        bool issueAlertActive = simpleMode ? silenceIssue : (silenceIssue || correlationIssue);
        bool matchingObsAudio = obsHasAudio && (simpleMode || !_correlationEngine.IsAlertActive);

        if (matchingObsAudio && !issueAlertActive)
        {
            lock (_lock)
            {
                _lastAudioIssueTime = null;
                DisarmExpirationTimerLocked();
            }
            HasSoundIssue = false;
            return;
        }

        bool shouldClear = false;
        bool shouldSetIssue = false;

        lock (_lock)
        {
            if (_hasSoundIssue || issueAlertActive)
            {
                if (_lastAudioIssueTime.HasValue)
                {
                    TimeSpan elapsed = DateTime.UtcNow - _lastAudioIssueTime.Value;
                    if (elapsed >= TimeSpan.FromSeconds(_retryTimeoutSeconds))
                    {
                        shouldClear = true;
                        _lastAudioIssueTime = null;
                        _silenceDetector.Reset();
                        _correlationEngine.Reset();
                        DisarmExpirationTimerLocked();
                    }
                    else
                    {
                        shouldSetIssue = true;
                        TimeSpan remaining = TimeSpan.FromSeconds(_retryTimeoutSeconds) - elapsed;
                        ArmExpirationTimerLocked(remaining);
                    }
                }
                else if (issueAlertActive)
                {
                    _lastAudioIssueTime = DateTime.UtcNow;
                    shouldSetIssue = true;
                    ArmExpirationTimerLocked(TimeSpan.FromSeconds(_retryTimeoutSeconds));
                }
                else
                {
                    shouldClear = true;
                    _lastAudioIssueTime = null;
                    _silenceDetector.Reset();
                    _correlationEngine.Reset();
                    DisarmExpirationTimerLocked();
                }
            }
            else
            {
                _lastAudioIssueTime = null;
                DisarmExpirationTimerLocked();
                shouldClear = true;
            }
        }

        if (shouldClear)
        {
            HasSoundIssue = false;
        }
        else if (shouldSetIssue)
        {
            HasSoundIssue = true;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _latestObsRmsDbfs = -100.0;
            _lastObsMeterTime = DateTime.MinValue;
            _obsMeterHistory.Clear();
            _clockOffsetMs = null;
            _lastPacketTime = DateTime.MinValue;
            _hasSoundIssue = false;
            _isNotInActiveScene = false;
            _lastAudioIssueTime = null;
            DisarmExpirationTimerLocked();
        }

        _silenceDetector.Reset();
        _correlationEngine.Reset();
    }

    public void Dispose()
    {
        _expirationTimer.Dispose();
    }
}
