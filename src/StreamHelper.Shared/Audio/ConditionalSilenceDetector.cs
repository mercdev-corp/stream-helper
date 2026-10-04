using StreamHelper.Shared.Common;

namespace StreamHelper.Shared.Audio;

public sealed class ConditionalSilenceDetector
{
    private readonly double _clientActiveThresholdDbfs;
    private readonly double _obsSilenceThresholdDbfs;
    private readonly int _consecutiveReadingsRequired;

    private int _consecutiveActiveClientSilentObsCount;
    private bool _isAlertActive;

    public bool IsAlertActive => _isAlertActive;

    public ConditionalSilenceDetector(
        double clientActiveThresholdDbfs = -45.0,
        double obsSilenceThresholdDbfs = -60.0,
        int consecutiveReadingsRequired = 30) // 30 * 100ms = 3.0 seconds
    {
        _clientActiveThresholdDbfs = clientActiveThresholdDbfs;
        _obsSilenceThresholdDbfs = obsSilenceThresholdDbfs;
        _consecutiveReadingsRequired = Math.Max(1, consecutiveReadingsRequired);
    }

    public bool ProcessReading(double clientRmsDbfs, double obsRmsDbfs)
    {
        bool wasAlertActive = _isAlertActive;

        // If client audio is quiet, suppress alert and reset counter
        if (clientRmsDbfs <= _clientActiveThresholdDbfs)
        {
            _consecutiveActiveClientSilentObsCount = 0;
            _isAlertActive = false;
            if (wasAlertActive)
            {
                AppLogger.Info($"[SilenceDetector] Sound capture issue cleared: client audio quiet ({clientRmsDbfs:F1} dBFS <= {_clientActiveThresholdDbfs} dBFS).");
            }
            return false;
        }

        // Client is active (> -45 dBFS)
        if (obsRmsDbfs < _obsSilenceThresholdDbfs)
        {
            _consecutiveActiveClientSilentObsCount++;
            if (_consecutiveActiveClientSilentObsCount >= _consecutiveReadingsRequired)
            {
                _isAlertActive = true;
                if (!wasAlertActive)
                {
                    AppLogger.Warn($"[SilenceDetector] Sound capture issue triggered: client active ({clientRmsDbfs:F1} dBFS > {_clientActiveThresholdDbfs} dBFS) while OBS silent ({obsRmsDbfs:F1} dBFS < {_obsSilenceThresholdDbfs} dBFS) for {_consecutiveReadingsRequired * 100}ms.");
                }
            }
        }
        else
        {
            // OBS has sound
            _consecutiveActiveClientSilentObsCount = 0;
            _isAlertActive = false;
            if (wasAlertActive)
            {
                AppLogger.Info($"[SilenceDetector] Sound capture issue resolved: audio detected in OBS ({obsRmsDbfs:F1} dBFS >= {_obsSilenceThresholdDbfs} dBFS).");
            }
        }

        return _isAlertActive;
    }

    public void Reset()
    {
        bool wasAlertActive = _isAlertActive;
        _consecutiveActiveClientSilentObsCount = 0;
        _isAlertActive = false;
        if (wasAlertActive)
        {
            AppLogger.Debug($"[SilenceDetector] Silence detector reset.");
        }
    }
}
