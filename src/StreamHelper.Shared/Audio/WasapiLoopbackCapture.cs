using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using StreamHelper.Shared.Common;

namespace StreamHelper.Shared.Audio;

[SupportedOSPlatform("windows")]
public sealed class WasapiLoopbackCapture : IDisposable
{
    private readonly object _lock = new();
    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _captureClient;
    private CancellationTokenSource? _cts;
    private Thread? _captureThread;

    private string? _targetDeviceId;
    private bool _isDeviceMissing;
    private bool _isRunning;
    private bool _disposed;

    public bool IsDeviceMissing => _isDeviceMissing;
    public bool IsRunning => _isRunning;

    public event Action<AudioWindowReading>? ReadingAvailable;
    public event Action<bool>? DeviceStateChanged;

    public WasapiLoopbackCapture()
    {
    }

    public void Start(string? targetDeviceId)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _targetDeviceId = targetDeviceId;
            _isRunning = true;
        }

        AppLogger.Info($"[WasapiLoopback] Starting loopback capture (target device: '{targetDeviceId ?? "Default"}').");
        StartCapture();
    }

    public void Stop()
    {
        lock (_lock)
        {
            _isRunning = false;
        }

        AppLogger.Info($"[WasapiLoopback] Stopping loopback capture.");
        StopCapture();
    }

    private void StartCapture()
    {
        StopCapture();

        lock (_lock)
        {
            if (!_isRunning || _disposed) return;
            _cts = new CancellationTokenSource();
            _captureThread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "WASAPI Loopback Capture Worker"
            };
            _captureThread.Start(_cts.Token);
        }
    }

    private void StopCapture()
    {
        CancellationTokenSource? cts;
        Thread? thread;
        lock (_lock)
        {
            cts = _cts;
            _cts = null;
            thread = _captureThread;
            _captureThread = null;
        }

        cts?.Cancel();
        cts?.Dispose();
        thread?.Join(1000);

        CleanupCom();
    }

    private void CleanupCom()
    {
        lock (_lock)
        {
            try
            {
                _audioClient?.Stop();
            }
            catch
            {
            }

            if (_captureClient != null)
            {
                Marshal.ReleaseComObject(_captureClient);
                _captureClient = null;
            }

            if (_audioClient != null)
            {
                Marshal.ReleaseComObject(_audioClient);
                _audioClient = null;
            }

            if (_device != null)
            {
                Marshal.ReleaseComObject(_device);
                _device = null;
            }

            if (_enumerator != null)
            {
                Marshal.ReleaseComObject(_enumerator);
                _enumerator = null;
            }
        }
    }

    private void CaptureLoop(object? state)
    {
        var token = (CancellationToken)(state ?? CancellationToken.None);

        try
        {
            IntPtr pFormat = IntPtr.Zero;
            uint sampleRate = 48000;
            ushort channels = 2;
            ushort bitsPerSample = 32;
            bool isFloat = true;

            lock (_lock)
            {
                var comType = Type.GetTypeFromCLSID(CoreAudioConstants.CLSID_MMDeviceEnumerator);
                if (comType == null)
                {
                    SetDeviceMissing(true);
                    return;
                }

                _enumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(comType);
                if (_enumerator == null)
                {
                    SetDeviceMissing(true);
                    return;
                }

                IMMDevice? device = null;
                if (!string.IsNullOrEmpty(_targetDeviceId))
                {
                    int hr = _enumerator.GetDevice(_targetDeviceId, out device);
                    if (hr != 0 || device == null)
                    {
                        SetDeviceMissing(true);
                        return;
                    }
                }
                else
                {
                    int hr = _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out device);
                    if (hr != 0 || device == null)
                    {
                        hr = _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
                    }
                    if (hr != 0 || device == null)
                    {
                        SetDeviceMissing(true);
                        return;
                    }
                }

                _device = device;
                SetDeviceMissing(false);

                var audioClientIid = WasapiConstants.IID_IAudioClient;
                int actHr = _device.Activate(ref audioClientIid, CoreAudioConstants.CLSCTX_ALL, IntPtr.Zero, out var clientObj);
                if (actHr != 0 || clientObj is not IAudioClient audioClient)
                {
                    SetDeviceMissing(true);
                    return;
                }
                _audioClient = audioClient;

                int fmtHr = _audioClient.GetMixFormat(out pFormat);
                if (fmtHr != 0 || pFormat == IntPtr.Zero)
                {
                    SetDeviceMissing(true);
                    return;
                }

                var waveFormat = Marshal.PtrToStructure<WAVEFORMATEX>(pFormat);
                sampleRate = waveFormat.nSamplesPerSec;
                channels = waveFormat.nChannels;
                bitsPerSample = waveFormat.wBitsPerSample;

                if (waveFormat.wFormatTag == 0xFFFE && waveFormat.cbSize >= 22)
                {
                    var ext = Marshal.PtrToStructure<WAVEFORMATEXTENSIBLE>(pFormat);
                    isFloat = ext.SubFormat == WasapiConstants.KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
                }
                else
                {
                    isFloat = waveFormat.wFormatTag == 3; // WAVE_FORMAT_IEEE_FLOAT
                }

                // 100ms buffer period (10,000,000 hns = 1 second)
                long bufferDuration = 10000000;
                var sessionGuid = Guid.Empty;
                int initHr = _audioClient.Initialize(
                    WasapiConstants.AUDCLNT_SHAREMODE_SHARED,
                    WasapiConstants.AUDCLNT_STREAMFLAGS_LOOPBACK,
                    bufferDuration,
                    0,
                    pFormat,
                    ref sessionGuid);

                if (initHr != 0)
                {
                    SetDeviceMissing(true);
                    return;
                }

                var captureClientIid = WasapiConstants.IID_IAudioCaptureClient;
                int srvHr = _audioClient.GetService(ref captureClientIid, out var captureObj);
                if (srvHr != 0 || captureObj is not IAudioCaptureClient captureClient)
                {
                    SetDeviceMissing(true);
                    return;
                }
                _captureClient = captureClient;

                _audioClient.Start();
                AppLogger.Info($"[WasapiLoopback] Loopback capture started: {sampleRate}Hz, {channels}ch, {bitsPerSample}-bit (isFloat={isFloat}) on device '{_targetDeviceId ?? "Default"}'.");
            }

            if (pFormat != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pFormat);
            }

            // Sub-sampling: 100ms window = sampleRate * 0.1 frames
            long targetFramesPerWindow = (long)(sampleRate * 0.1);
            long accumulatedFrames = 0;
            double sumSquares = 0;
            double peakSample = 0;

            while (!token.IsCancellationRequested)
            {
                IAudioCaptureClient? client;
                lock (_lock)
                {
                    client = _captureClient;
                }
                if (client == null) break;

                while (client.GetNextPacketSize(out uint packetSize) == 0 && packetSize > 0)
                {
                    int getHr = client.GetBuffer(out IntPtr pData, out uint numFrames, out uint flags, out _, out _);
                    if (getHr != 0 || pData == IntPtr.Zero) break;

                    bool isSilent = (flags & WasapiConstants.AUDCLNT_BUFFERFLAGS_SILENT) != 0;

                    if (!isSilent)
                    {
                        int totalSamples = (int)(numFrames * channels);
                        if (isFloat && bitsPerSample == 32)
                        {
                            var samples = new float[totalSamples];
                            Marshal.Copy(pData, samples, 0, totalSamples);
                            for (int i = 0; i < totalSamples; i++)
                            {
                                float s = samples[i];
                                double abs = Math.Abs(s);
                                if (abs > peakSample) peakSample = abs;
                                sumSquares += s * s;
                            }
                        }
                        else if (!isFloat && bitsPerSample == 32)
                        {
                            var samples = new int[totalSamples];
                            Marshal.Copy(pData, samples, 0, totalSamples);
                            for (int i = 0; i < totalSamples; i++)
                            {
                                float s = samples[i] / 2147483648.0f;
                                double abs = Math.Abs(s);
                                if (abs > peakSample) peakSample = abs;
                                sumSquares += s * s;
                            }
                        }
                        else if (bitsPerSample == 24)
                        {
                            int byteCount = totalSamples * 3;
                            var rawBytes = new byte[byteCount];
                            Marshal.Copy(pData, rawBytes, 0, byteCount);
                            for (int i = 0; i < totalSamples; i++)
                            {
                                int offset = i * 3;
                                int val = rawBytes[offset] | (rawBytes[offset + 1] << 8) | (rawBytes[offset + 2] << 16);
                                if ((val & 0x800000) != 0) val |= unchecked((int)0xFF000000);
                                float s = val / 8388608.0f;
                                double abs = Math.Abs(s);
                                if (abs > peakSample) peakSample = abs;
                                sumSquares += s * s;
                            }
                        }
                        else if (bitsPerSample == 16)
                        {
                            var samples = new short[totalSamples];
                            Marshal.Copy(pData, samples, 0, totalSamples);
                            for (int i = 0; i < totalSamples; i++)
                            {
                                float s = samples[i] / 32768.0f;
                                double abs = Math.Abs(s);
                                if (abs > peakSample) peakSample = abs;
                                sumSquares += s * s;
                            }
                        }
                    }

                    accumulatedFrames += numFrames;
                    client.ReleaseBuffer(numFrames);

                    if (accumulatedFrames >= targetFramesPerWindow)
                    {
                        long totalSamplesCount = accumulatedFrames * channels;
                        double rms = totalSamplesCount > 0 ? Math.Sqrt(sumSquares / totalSamplesCount) : 0;
                        double rmsDbfs = CalculateDbfs(rms);
                        double peakDbfs = CalculateDbfs(peakSample);

                        ReadingAvailable?.Invoke(new AudioWindowReading(
                            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            rmsDbfs,
                            peakDbfs));

                        accumulatedFrames = 0;
                        sumSquares = 0;
                        peakSample = 0;
                    }
                }

                Thread.Sleep(10);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[WasapiLoopback] WASAPI Loopback exception on device '{_targetDeviceId ?? "Default"}'", ex);
            SetDeviceMissing(true);
        }
        finally
        {
            CleanupCom();
        }
    }

    public static double CalculateDbfs(double linear)
    {
        if (linear <= 0.00001) return -100.0;
        var db = 20.0 * Math.Log10(linear);
        return Math.Max(-100.0, db);
    }

    private void SetDeviceMissing(bool missing)
    {
        bool changed = false;
        lock (_lock)
        {
            if (_isDeviceMissing != missing)
            {
                _isDeviceMissing = missing;
                changed = true;
            }
        }

        if (changed)
        {
            AppLogger.Warn($"[WasapiLoopback] Device missing state changed: isMissing={missing} (Device: '{_targetDeviceId ?? "Default"}').");
            DeviceStateChanged?.Invoke(missing);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _isRunning = false;
        }

        StopCapture();
    }
}
