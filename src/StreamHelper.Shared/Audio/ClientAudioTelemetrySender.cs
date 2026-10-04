using System.Net;
using System.Net.Sockets;
using StreamHelper.Shared.Common;

namespace StreamHelper.Shared.Audio;

public sealed class ClientAudioTelemetrySender : IDisposable
{
    private readonly object _lock = new();
    private readonly WasapiLoopbackCapture _capture;
    private readonly List<float> _readingsBatch = new(10);
    private UdpClient? _udpClient;
    private IPEndPoint? _serverEndPoint;
    private ulong _sequenceNumber;
    private bool _isDualPc = true;
    private AudioCorrelationEngine? _inMemoryEngine;
    private bool _disposed;

    public bool IsDeviceMissing => _capture.IsDeviceMissing;
    public bool IsRunning => _capture.IsRunning;

    public event Action<AudioTelemetryPacket>? PacketSent;
    public event Action<bool>? DeviceStateChanged;

    public ClientAudioTelemetrySender(WasapiLoopbackCapture? capture = null)
    {
        _capture = capture ?? new WasapiLoopbackCapture();
        _capture.ReadingAvailable += OnReadingAvailable;
        _capture.DeviceStateChanged += missing => DeviceStateChanged?.Invoke(missing);
    }

    public void ConfigureDualPc(string serverIp, int port)
    {
        lock (_lock)
        {
            _isDualPc = true;
            _inMemoryEngine = null;

            _udpClient?.Dispose();
            _udpClient = new UdpClient();
            if (IPAddress.TryParse(serverIp, out var ip))
            {
                _serverEndPoint = new IPEndPoint(ip, port);
            }
            else
            {
                _serverEndPoint = new IPEndPoint(IPAddress.Loopback, port);
            }
            _readingsBatch.Clear();
            AppLogger.Info($"[TelemetrySender] Configured for Dual-PC mode, server endpoint: {_serverEndPoint}.");
        }
    }

    public void ConfigureSinglePc(AudioCorrelationEngine inMemoryEngine)
    {
        lock (_lock)
        {
            _isDualPc = false;
            _inMemoryEngine = inMemoryEngine;
            _udpClient?.Dispose();
            _udpClient = null;
            _serverEndPoint = null;
            _readingsBatch.Clear();
            AppLogger.Info($"[TelemetrySender] Configured for Single-PC mode (direct in-memory correlation routing).");
        }
    }

    public void Start(string? targetAudioDeviceId)
    {
        AppLogger.Info($"[TelemetrySender] Starting audio telemetry capture on device '{targetAudioDeviceId ?? "Default"}'.");
        _capture.Start(targetAudioDeviceId);
    }

    public void Stop()
    {
        AppLogger.Info($"[TelemetrySender] Stopping audio telemetry capture.");
        _capture.Stop();
        lock (_lock)
        {
            _readingsBatch.Clear();
        }
    }

    private void OnReadingAvailable(AudioWindowReading reading)
    {
        lock (_lock)
        {
            if (_disposed || !_capture.IsRunning) return;

            if (!_isDualPc)
            {
                _inMemoryEngine?.ProcessClientReading(reading.RmsDbfs);
                return;
            }

            _readingsBatch.Add((float)reading.RmsDbfs);
            if (_readingsBatch.Count >= 10)
            {
                _sequenceNumber++;
                var packet = new AudioTelemetryPacket
                {
                    SequenceNumber = _sequenceNumber,
                    TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Readings = _readingsBatch.ToArray()
                };
                _readingsBatch.Clear();

                SendTelemetryPacket(packet);
            }
        }
    }

    public void SendTelemetryPacket(AudioTelemetryPacket packet)
    {
        IPEndPoint? endPoint;
        UdpClient? client;
        lock (_lock)
        {
            endPoint = _serverEndPoint;
            client = _udpClient;
        }

        if (client == null || endPoint == null) return;

        try
        {
            var bytes = packet.ToBytes();
            client.Send(bytes, bytes.Length, endPoint);
            AppLogger.Debug($"[TelemetrySender] Sent telemetry batch #{packet.SequenceNumber} ({packet.Readings.Length} samples) to {endPoint}.");
            PacketSent?.Invoke(packet);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[TelemetrySender] Failed to send audio telemetry packet to {endPoint}: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        Stop();
        _capture.Dispose();

        lock (_lock)
        {
            _udpClient?.Dispose();
            _udpClient = null;
        }
    }
}
