using StreamHelper.Shared.Common;

namespace StreamHelper.Shared.Audio;

public sealed class PearsonCorrelationEngine
{
    private readonly int _windowSize; // 120 samples (12 seconds at 100ms)
    private readonly int _minLagSteps; // -5 steps (-500ms for network/clock lead)
    private readonly int _maxLagSteps; // 25 steps (up to 2500ms for HDMI / Realtek stereo mix delay)
    private readonly double _varianceThreshold; // 5.0 dB
    private readonly double _minCorrelationThreshold; // 0.5
    private readonly int _consecutiveLowCorrCountRequired; // 50 samples (5.0s)

    private readonly Queue<double> _clientSamples = new();
    private readonly Queue<double> _obsSamples = new();
    private int _consecutiveLowCorrCount;
    private bool _isAlertActive;

    public bool IsAlertActive => _isAlertActive;
    public double LastCalculatedMaxCorrelation { get; private set; }
    public double LastClientVariance { get; private set; }
    public double LastCalculatedDifference { get; private set; }

    private double _toleranceDb = 20.0;
    public double ToleranceDb
    {
        get => _toleranceDb;
        set => _toleranceDb = Math.Clamp(value, 0, 40);
    }

    public PearsonCorrelationEngine(
        int windowSize = 120,
        int maxLagSteps = 25,
        double varianceThreshold = 1.0,
        double minCorrelationThreshold = 0.5,
        int consecutiveLowCorrCountRequired = 30,
        int minLagSteps = -5,
        double toleranceDb = 20.0)
    {
        _windowSize = Math.Max(20, windowSize);
        _minLagSteps = Math.Min(0, minLagSteps);
        _maxLagSteps = Math.Max(0, maxLagSteps);
        _varianceThreshold = varianceThreshold;
        _minCorrelationThreshold = minCorrelationThreshold;
        _consecutiveLowCorrCountRequired = Math.Max(1, consecutiveLowCorrCountRequired);
        ToleranceDb = toleranceDb;
    }

    public bool AddSamples(double clientRmsDbfs, double obsRmsDbfs)
    {
        bool wasAlertActive = _isAlertActive;

        _clientSamples.Enqueue(clientRmsDbfs);
        _obsSamples.Enqueue(obsRmsDbfs);

        while (_clientSamples.Count > _windowSize)
        {
            _clientSamples.Dequeue();
        }
        while (_obsSamples.Count > _windowSize)
        {
            _obsSamples.Dequeue();
        }

        // Allow correlation calculation as soon as we have enough samples to test lags
        int minRequired = Math.Max(35, _maxLagSteps + 5);
        if (_clientSamples.Count < minRequired || _obsSamples.Count < minRequired)
        {
            return false;
        }

        var clientArr = _clientSamples.ToArray();
        var obsArr = _obsSamples.ToArray();

        // Calculate client variance
        double clientMean = CalculateMean(clientArr);
        double clientVariance = CalculateVariance(clientArr, clientMean);
        LastClientVariance = clientVariance;

        // If client audio doesn't have enough dynamic variance, decay counter gradually
        if (clientVariance <= _varianceThreshold)
        {
            _consecutiveLowCorrCount = Math.Max(0, _consecutiveLowCorrCount - 1);
            if (_consecutiveLowCorrCount == 0)
            {
                _isAlertActive = false;
                if (wasAlertActive)
                {
                    AppLogger.Info($"[CorrelationEngine] Correlation mismatch cleared: client variance below threshold ({clientVariance:F1} dB <= {_varianceThreshold} dB).");
                }
            }
            return _isAlertActive;
        }

        // Calculate maximum correlation across minLagSteps..maxLagSteps
        double maxR = -1.0;
        int bestLag = 0;
        for (int lag = _minLagSteps; lag <= _maxLagSteps; lag++)
        {
            double r = CalculateLaggedCorrelation(clientArr, obsArr, lag);
            if (r > maxR)
            {
                maxR = r;
                bestLag = lag;
            }
        }

        LastCalculatedMaxCorrelation = maxR;
        double meanDiff = CalculateLaggedMeanAbsoluteDifference(clientArr, obsArr, bestLag);
        LastCalculatedDifference = meanDiff;

        if (maxR < _minCorrelationThreshold && meanDiff > ToleranceDb)
        {
            _consecutiveLowCorrCount = Math.Min(_consecutiveLowCorrCountRequired, _consecutiveLowCorrCount + 1);
            if (_consecutiveLowCorrCount >= _consecutiveLowCorrCountRequired)
            {
                _isAlertActive = true;
                if (!wasAlertActive)
                {
                    AppLogger.Warn($"[CorrelationEngine] Correlation mismatch alert triggered: max correlation r={maxR:F2} < {_minCorrelationThreshold} (diff={meanDiff:F1} dB > {ToleranceDb:F1} dB) across lags [{_minLagSteps}..{_maxLagSteps}] for {_consecutiveLowCorrCountRequired * 100}ms (variance={clientVariance:F1} dB).");
                }
            }
        }
        else
        {
            // Matching audio or difference within tolerance -> decay counter
            _consecutiveLowCorrCount = Math.Max(0, _consecutiveLowCorrCount - 2);
            if (_consecutiveLowCorrCount == 0)
            {
                _isAlertActive = false;
                if (wasAlertActive)
                {
                    if (maxR >= _minCorrelationThreshold)
                    {
                        AppLogger.Info($"[CorrelationEngine] Correlation mismatch resolved: max correlation r={maxR:F2} >= {_minCorrelationThreshold}.");
                    }
                    else
                    {
                        AppLogger.Info($"[CorrelationEngine] Correlation mismatch resolved: difference within tolerance ({meanDiff:F1} dB <= {ToleranceDb:F1} dB).");
                    }
                }
            }
        }

        return _isAlertActive;
    }

    public void Reset()
    {
        bool wasAlertActive = _isAlertActive;
        _clientSamples.Clear();
        _obsSamples.Clear();
        _consecutiveLowCorrCount = 0;
        _isAlertActive = false;
        LastCalculatedMaxCorrelation = 0;
        LastCalculatedDifference = 0;
        LastClientVariance = 0;
        if (wasAlertActive)
        {
            AppLogger.Debug($"[CorrelationEngine] Pearson correlation engine reset.");
        }
    }

    public static double CalculateMean(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty) return 0;
        double sum = 0;
        for (int i = 0; i < values.Length; i++) sum += values[i];
        return sum / values.Length;
    }

    public static double CalculateVariance(ReadOnlySpan<double> values, double mean)
    {
        if (values.Length <= 1) return 0;
        double sumSq = 0;
        for (int i = 0; i < values.Length; i++)
        {
            var diff = values[i] - mean;
            sumSq += diff * diff;
        }
        return sumSq / values.Length;
    }

    public static double CalculateLaggedCorrelation(double[] x, double[] y, int lag)
    {
        int n;
        int startX = 0;
        int startY = 0;

        if (lag >= 0)
        {
            n = Math.Min(x.Length - lag, y.Length - lag);
            startY = lag;
        }
        else
        {
            n = Math.Min(x.Length + lag, y.Length + lag);
            startX = -lag;
        }

        if (n <= 1) return 0;

        double sumX = 0, sumY = 0;
        for (int i = 0; i < n; i++)
        {
            sumX += x[startX + i];
            sumY += y[startY + i];
        }

        double meanX = sumX / n;
        double meanY = sumY / n;

        double num = 0;
        double denomX = 0;
        double denomY = 0;

        for (int i = 0; i < n; i++)
        {
            double dx = x[startX + i] - meanX;
            double dy = y[startY + i] - meanY;
            num += dx * dy;
            denomX += dx * dx;
            denomY += dy * dy;
        }

        double denom = Math.Sqrt(denomX * denomY);
        if (denom <= 1e-9) return 0;

        return num / denom;
    }

    public static double CalculateLaggedMeanAbsoluteDifference(double[] x, double[] y, int lag)
    {
        int n;
        int startX = 0;
        int startY = 0;

        if (lag >= 0)
        {
            n = Math.Min(x.Length - lag, y.Length - lag);
            startY = lag;
        }
        else
        {
            n = Math.Min(x.Length + lag, y.Length + lag);
            startX = -lag;
        }

        if (n <= 0) return 0;

        double sumDiff = 0;
        for (int i = 0; i < n; i++)
        {
            sumDiff += Math.Abs(x[startX + i] - y[startY + i]);
        }

        return sumDiff / n;
    }
}
