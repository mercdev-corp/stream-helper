using StreamHelper.Shared.Common;

namespace StreamHelper.Shared.Audio;

public sealed class ConditionalSilenceDetector
{
    private readonly double _clientActiveThresholdDbfs;
    private readonly double _obsSilenceThresholdDbfs;
    private readonly int _consecutiveReadingsRequired;

    private int _consecutiveActiveClientSilentObsCount;
    private bool _isAlertActive;
    private bool _isActivelyConfirmingIssue;

    public bool IsAlertActive => _isAlertActive;
    public bool IsActivelyConfirmingIssue => _isActivelyConfirmingIssue;
    public double ClientActiveThresholdDbfs => _clientActiveThresholdDbfs;
    public double ObsSilenceThresholdDbfs => _obsSilenceThresholdDbfs;
    public int ConsecutiveReadingsRequired => _consecutiveReadingsRequired;

    public ConditionalSilenceDetector(
        double clientActiveThresholdDbfs = -60.0,
        double obsSilenceThresholdDbfs = -60.0,
        int consecutiveReadingsRequired = 10) // 10 * 100ms = 1.0 second
    {
        _clientActiveThresholdDbfs = clientActiveThresholdDbfs;
        _obsSilenceThresholdDbfs = obsSilenceThresholdDbfs;
        _consecutiveReadingsRequired = Math.Max(1, consecutiveReadingsRequired);
    }

    public bool ProcessReading(double clientRmsDbfs, double obsRmsDbfs)
    {
        bool wasAlertActive = _isAlertActive;

        // 1. OBS has sound (>= obsSilenceThresholdDbfs).
        // Check this FIRST before quiet client check so that whenever OBS captures non-silent audio,
        // the detector clears immediately even while client audio is quiet (<= clientActiveThresholdDbfs).
        if (obsRmsDbfs >= _obsSilenceThresholdDbfs)
        {
            _consecutiveActiveClientSilentObsCount = 0;
            _isActivelyConfirmingIssue = false;
            _isAlertActive = false;
            if (wasAlertActive)
            {
                AppLogger.Info($"[SilenceDetector] Sound capture issue resolved: audio detected in OBS ({obsRmsDbfs:F1} dBFS >= {_obsSilenceThresholdDbfs} dBFS).");
            }
            return false;
        }

        // 2. Client audio is quiet or in a micro-pause (<= clientActiveThresholdDbfs) while OBS is silent.
        // Decay counter by 2 so a single active spike after a quiet reading does not re-latch active confirmation,
        // while continuous audio with brief micro-gaps still accumulates. Quiet readings NEVER report active confirmation.
        if (clientRmsDbfs <= _clientActiveThresholdDbfs)
        {
            _isActivelyConfirmingIssue = false;
            _consecutiveActiveClientSilentObsCount = Math.Max(0, _consecutiveActiveClientSilentObsCount - 2);
            if (_consecutiveActiveClientSilentObsCount == 0)
            {
                _isAlertActive = false;
                if (wasAlertActive)
                {
                    AppLogger.Info($"[SilenceDetector] Sound capture issue cleared: client audio quiet ({clientRmsDbfs:F1} dBFS <= {_clientActiveThresholdDbfs} dBFS).");
                }
            }
            return _isAlertActive;
        }

        // 3. Client is active (> clientActiveThresholdDbfs) while OBS is silent (< obsSilenceThresholdDbfs).
        _consecutiveActiveClientSilentObsCount = Math.Min(_consecutiveReadingsRequired, _consecutiveActiveClientSilentObsCount + 1);

        if (_consecutiveActiveClientSilentObsCount >= _consecutiveReadingsRequired)
        {
            _isAlertActive = true;
            if (!wasAlertActive)
            {
                AppLogger.Warn($"[SilenceDetector] Sound capture issue triggered: client active ({clientRmsDbfs:F1} dBFS > {_clientActiveThresholdDbfs} dBFS) while OBS silent ({obsRmsDbfs:F1} dBFS < {_obsSilenceThresholdDbfs} dBFS) for {_consecutiveReadingsRequired * 100}ms.");
            }
        }

        _isActivelyConfirmingIssue = _isAlertActive && _consecutiveActiveClientSilentObsCount >= _consecutiveReadingsRequired;

        return _isAlertActive;
    }

    public void Reset()
    {
        bool wasAlertActive = _isAlertActive;
        _consecutiveActiveClientSilentObsCount = 0;
        _isActivelyConfirmingIssue = false;
        _isAlertActive = false;
        if (wasAlertActive)
        {
            AppLogger.Debug($"[SilenceDetector] Silence detector reset.");
        }
    }
}
