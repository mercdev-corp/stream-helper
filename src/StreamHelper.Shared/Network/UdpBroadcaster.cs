using System.Net;
using System.Net.Sockets;
using StreamHelper.Shared.Common;
using StreamHelper.Shared.Protocol;

namespace StreamHelper.Shared.Network;

public sealed class UdpBroadcaster : IDisposable
{
    private readonly object _lock = new();
    private UdpClient? _udpClient;
    private IPEndPoint _multicastEndpoint;
    private System.Threading.Timer? _heartbeatTimer;

    private int _port;
    private ulong _sequenceNumber;
    private MicState? _currentState;
    private AlertFlags _currentAlerts = AlertFlags.None;
    private bool _isPaused;
    private bool _disposed;

    public string ServerId { get; }
    public string HostName { get; }
    public string MicrophoneName { get; set; } = string.Empty;
    public int Port => _port;
    public MicState CurrentState => _currentState ?? MicState.Unmuted;
    public AlertFlags CurrentAlerts => _currentAlerts;
    public bool IsPaused => _isPaused;

    public event Action<StatusPacket>? PacketSent;

    public UdpBroadcaster(int port = ProtocolConstants.DefaultPort, string? serverId = null)
    {
        _port = port;
        ServerId = serverId ?? Guid.NewGuid().ToString("N");
        HostName = NetworkUtils.GetLocalHostName();
        _multicastEndpoint = new IPEndPoint(IPAddress.Parse(ProtocolConstants.DefaultMulticastAddress), _port);

        InitializeSocket();
    }

    private void InitializeSocket()
    {
        lock (_lock)
        {
            _udpClient?.Dispose();

            try
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0)); // ephemeral source port
                _udpClient.JoinMulticastGroup(IPAddress.Parse(ProtocolConstants.DefaultMulticastAddress));
                _udpClient.MulticastLoopback = true;
                AppLogger.Info($"[UdpBroadcaster] Initialized multicast broadcast socket on port {_port} (Group: {ProtocolConstants.DefaultMulticastAddress}).");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[UdpBroadcaster] Failed to initialize broadcast socket on port {_port}", ex);
            }
        }
    }

    public void Rebind(int newPort)
    {
        lock (_lock)
        {
            if (_port == newPort) return;
            AppLogger.Info($"[UdpBroadcaster] Rebinding broadcaster from port {_port} to {newPort}.");
            _port = newPort;
            _multicastEndpoint = new IPEndPoint(IPAddress.Parse(ProtocolConstants.DefaultMulticastAddress), _port);
            InitializeSocket();
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = new System.Threading.Timer(
                _ => SendHeartbeat(),
                null,
                TimeSpan.Zero,
                ProtocolConstants.HeartbeatInterval);
            AppLogger.Info($"[UdpBroadcaster] Started heartbeat broadcaster on port {_port}.");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
            AppLogger.Info($"[UdpBroadcaster] Stopped heartbeat broadcaster.");
        }
    }

    public void SetPaused(bool paused)
    {
        bool transition = false;
        lock (_lock)
        {
            if (_isPaused != paused)
            {
                _isPaused = paused;
                if (paused)
                {
                    _currentState = MicState.Paused;
                }
                transition = true;
            }
        }

        if (transition)
        {
            AppLogger.Info($"[UdpBroadcaster] Broadcaster paused state set to: {paused}.");
            if (paused)
            {
                BroadcastImmediateBurst(MicState.Paused, _currentAlerts, PacketType.StateChange);
                Stop();
            }
            else
            {
                Start();
                BroadcastImmediateBurst(CurrentState, _currentAlerts, PacketType.StateChange);
            }
        }
    }

    public void UpdateState(MicState newState, string? micName = null, bool force = false)
    {
        bool changed = false;
        AlertFlags alerts;
        lock (_lock)
        {
            if (micName != null) MicrophoneName = micName;

            if (!_isPaused && (force || _currentState != newState))
            {
                _currentState = newState;
                _currentAlerts &= ~(AlertFlags.MicMuted | AlertFlags.MicDisconnected);
                if (newState == MicState.Muted) _currentAlerts |= AlertFlags.MicMuted;
                else if (newState == MicState.Disconnected) _currentAlerts |= AlertFlags.MicDisconnected;
                changed = true;
            }
            alerts = _currentAlerts;
        }

        if (changed)
        {
            AppLogger.Debug($"[UdpBroadcaster] State changed to {newState}, alerts: {alerts}, mic: '{MicrophoneName}'.");
            BroadcastImmediateBurst(newState, alerts, PacketType.StateChange);
        }
    }

    public void UpdateAlerts(AlertFlags newAlerts, string? micName = null, bool force = false)
    {
        bool changed = false;
        MicState state;
        lock (_lock)
        {
            if (micName != null) MicrophoneName = micName;

            if (!_isPaused && (force || _currentAlerts != newAlerts))
            {
                _currentAlerts = newAlerts;
                if (newAlerts.HasFlag(AlertFlags.MicDisconnected)) _currentState = MicState.Disconnected;
                else if (newAlerts.HasFlag(AlertFlags.MicMuted)) _currentState = MicState.Muted;
                else _currentState = MicState.Unmuted;
                changed = true;
            }
            state = CurrentState;
        }

        if (changed)
        {
            AppLogger.Debug($"[UdpBroadcaster] Alerts updated to {newAlerts}, state: {state}, mic: '{MicrophoneName}'.");
            BroadcastImmediateBurst(state, newAlerts, PacketType.StateChange);
        }
    }

    private void BroadcastImmediateBurst(MicState state, AlertFlags alerts, PacketType type)
    {
        AppLogger.Debug($"[UdpBroadcaster] Broadcasting burst of {ProtocolConstants.BurstCount} packets (State={state}, Alerts={alerts}, Type={type}).");
        Task.Run(async () =>
        {
            for (int i = 0; i < ProtocolConstants.BurstCount; i++)
            {
                if (_disposed) break;
                SendPacket(state, alerts, type);
                if (i < ProtocolConstants.BurstCount - 1)
                {
                    await Task.Delay(ProtocolConstants.BurstDelay).ConfigureAwait(false);
                }
            }
        });
    }

    private void SendHeartbeat()
    {
        MicState state;
        AlertFlags alerts;
        bool paused;
        lock (_lock)
        {
            state = CurrentState;
            alerts = _currentAlerts;
            paused = _isPaused;
        }

        if (paused || _disposed) return;
        SendPacket(state, alerts, PacketType.Heartbeat);
    }

    private void SendPacket(MicState state, AlertFlags alerts, PacketType type)
    {
        byte[] bytes;
        StatusPacket packet;

        lock (_lock)
        {
            if (_disposed || _udpClient == null) return;

            _sequenceNumber++;
            packet = new StatusPacket
            {
                Type = type,
                State = state,
                Alerts = alerts,
                SequenceNumber = _sequenceNumber,
                TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ServerId = ServerId,
                HostName = HostName,
                MicrophoneName = MicrophoneName
            };

            bytes = packet.ToBytes();
        }

        try
        {
            _udpClient.Send(bytes, bytes.Length, _multicastEndpoint);
            PacketSent?.Invoke(packet);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[UdpBroadcaster] Failed to send multicast UDP packet to {_multicastEndpoint}: {ex.Message}", ex);
        }

        try
        {
            _udpClient.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, _port));
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[UdpBroadcaster] Failed to send loopback UDP packet on port {_port}: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;

            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;

            _udpClient?.Dispose();
            _udpClient = null;
        }
    }
}
