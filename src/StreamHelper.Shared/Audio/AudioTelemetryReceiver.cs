using System.Net;
using System.Net.Sockets;
using StreamHelper.Shared.Common;

namespace StreamHelper.Shared.Audio;

public sealed class AudioTelemetryReceiver : IDisposable
{
    private readonly object _lock = new();
    private readonly AudioCorrelationEngine _engine;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private int _port;
    private bool _disposed;

    public int Port => _port;
    public event Action<AudioTelemetryPacket, IPEndPoint>? PacketReceived;

    public AudioTelemetryReceiver(AudioCorrelationEngine engine, int port)
    {
        _engine = engine;
        _port = port;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed) return;
            Stop();

            try
            {
                _cts = new CancellationTokenSource();
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _port));

                AppLogger.Info($"[TelemetryReceiver] Listening for audio telemetry on port {_port}.");

                var token = _cts.Token;
                Task.Run(() => ReceiveLoopAsync(_udpClient, token), token);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[TelemetryReceiver] Failed to start AudioTelemetryReceiver on port {_port}", ex);
            }
        }
    }

    public void Rebind(int newPort)
    {
        lock (_lock)
        {
            if (_port == newPort) return;
            AppLogger.Info($"[TelemetryReceiver] Rebinding telemetry receiver from port {_port} to {newPort}.");
            _port = newPort;
            Start();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            _udpClient?.Dispose();
            _udpClient = null;
            AppLogger.Info($"[TelemetryReceiver] Stopped audio telemetry receiver on port {_port}.");
        }
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(token).ConfigureAwait(false);
                if (AudioTelemetryPacket.TryParse(result.Buffer, out var packet))
                {
                    AppLogger.Debug($"[TelemetryReceiver] Received telemetry packet #{packet.SequenceNumber} ({packet.Readings.Length} samples) from {result.RemoteEndPoint}.");
                    _engine.ProcessTelemetryPacket(packet);
                    PacketReceived?.Invoke(packet, result.RemoteEndPoint);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested) break;
                AppLogger.Warn($"[TelemetryReceiver] Telemetry receive error on port {_port}: {ex.Message}", ex);
                await Task.Delay(50, token).ConfigureAwait(false);
            }
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
    }
}
