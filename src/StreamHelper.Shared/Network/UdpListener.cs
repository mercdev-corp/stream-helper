using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using StreamHelper.Shared.Common;
using StreamHelper.Shared.Protocol;

namespace StreamHelper.Shared.Network;

public sealed record DiscoveredServer(
    string ServerId,
    string HostName,
    string ServerIp,
    string MicrophoneName,
    MicState LastState,
    AlertFlags LastAlerts,
    DateTime LastSeen)
{
    public DiscoveredServer(
        string serverId,
        string hostName,
        string serverIp,
        string microphoneName,
        MicState lastState,
        DateTime lastSeen)
        : this(serverId, hostName, serverIp, microphoneName, lastState, AlertFlags.None, lastSeen)
    {
    }
}

public sealed class UdpListener : IDisposable
{
    private readonly object _lock = new();
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cts;
    private System.Threading.Timer? _watchdogTimer;

    private int _port;
    private string? _targetServerIp;
    private int _retryTimeoutSeconds = 5;
    private bool _isPaused;
    private bool _isConnected;
    private MicState _lastReportedState = MicState.Disconnected;
    private AlertFlags _lastReportedAlerts = AlertFlags.None;
    private DateTime _lastTargetPacketTime = DateTime.MinValue;
    private bool _disposed;

    private readonly ConcurrentDictionary<string, DiscoveredServer> _discoveredServers = new();

    public int Port => _port;
    public string? TargetServerIp => _targetServerIp;
    public bool IsConnected => _isConnected;
    public MicState LastReportedState => _lastReportedState;
    public AlertFlags LastReportedAlerts => _lastReportedAlerts;
    public bool IsPaused => _isPaused;

    public event Action<StatusPacket, IPEndPoint>? PacketReceived;
    public event Action<MicState>? TargetServerStateChanged;
    public event Action<AlertFlags>? TargetServerAlertsChanged;
    public event Action<bool>? TargetServerConnectionChanged;
    public event Action? DiscoveredServersUpdated;

    public UdpListener(int port = ProtocolConstants.DefaultPort)
    {
        _port = port;
    }

    public IReadOnlyList<DiscoveredServer> GetDiscoveredServers()
    {
        // Prune servers not seen in last 60 seconds
        var threshold = DateTime.UtcNow.AddSeconds(-60);
        foreach (var kvp in _discoveredServers)
        {
            if (kvp.Value.LastSeen < threshold)
            {
                _discoveredServers.TryRemove(kvp.Key, out _);
            }
        }
        return _discoveredServers.Values.OrderByDescending(s => s.LastSeen).ToList();
    }

    public void Start(string? targetServerIp = null, int retryTimeoutSeconds = 5)
    {
        lock (_lock)
        {
            _targetServerIp = targetServerIp;
            _retryTimeoutSeconds = Math.Max(1, retryTimeoutSeconds);
            _isPaused = false;
            _lastTargetPacketTime = DateTime.MinValue;
        }

        AppLogger.Info($"[UdpListener] Starting listener on port {_port}, target server: {_targetServerIp ?? "Any"}, retry timeout: {_retryTimeoutSeconds}s.");

        SetConnected(false);
        SetState(MicState.Disconnected);
        SetAlerts(AlertFlags.None);

        RestartSocket();
        StartWatchdog();
    }

    public void Stop()
    {
        lock (_lock)
        {
            _isPaused = true;
            StopWatchdog();
            CloseSocket();
        }

        AppLogger.Info($"[UdpListener] Stopped listener on port {_port}.");

        SetConnected(false);
        SetState(MicState.Disconnected);
        SetAlerts(AlertFlags.None);
    }

    public void SetPaused(bool paused)
    {
        lock (_lock)
        {
            if (_isPaused == paused) return;
            _isPaused = paused;
        }

        AppLogger.Info($"[UdpListener] Listener paused state set to: {paused}.");

        if (paused)
        {
            Stop();
            SetConnected(false);
        }
        else
        {
            Start(_targetServerIp, _retryTimeoutSeconds);
        }
    }

    public void Rebind(int newPort, string? newTargetServerIp = null)
    {
        lock (_lock)
        {
            AppLogger.Info($"[UdpListener] Rebinding listener to port {newPort}, target server: {newTargetServerIp ?? _targetServerIp ?? "Any"}.");
            _port = newPort;
            if (newTargetServerIp != null)
            {
                _targetServerIp = newTargetServerIp;
            }
            _discoveredServers.Clear();
        }

        if (!_isPaused)
        {
            RestartSocket();
        }
    }

    public void SetTargetServer(string? serverIp)
    {
        bool changed = false;
        lock (_lock)
        {
            if (!string.Equals(_targetServerIp, serverIp, StringComparison.OrdinalIgnoreCase))
            {
                _targetServerIp = serverIp;
                _lastTargetPacketTime = DateTime.MinValue;
                changed = true;
            }
        }

        if (changed)
        {
            AppLogger.Info($"[UdpListener] Target server filter set to: '{serverIp ?? "Any"}'.");
            SetConnected(false);
        }
    }

    public void SetRetryTimeout(int seconds)
    {
        lock (_lock)
        {
            _retryTimeoutSeconds = Math.Max(1, seconds);
            AppLogger.Debug($"[UdpListener] Retry timeout set to {_retryTimeoutSeconds}s.");
        }
    }

    private void RestartSocket()
    {
        lock (_lock)
        {
            CloseSocket();

            try
            {
                _cts = new CancellationTokenSource();
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _port));
                _udpClient.JoinMulticastGroup(IPAddress.Parse(ProtocolConstants.DefaultMulticastAddress));
                _udpClient.MulticastLoopback = true;

                AppLogger.Info($"[UdpListener] Bound UDP multicast listener socket on port {_port} (Group: {ProtocolConstants.DefaultMulticastAddress}).");

                var token = _cts.Token;
                Task.Run(() => ReceiveLoopAsync(_udpClient, token), token);
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[UdpListener] Failed to bind UDP listener on port {_port}", ex);
            }
        }
    }

    private void CloseSocket()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _udpClient?.Dispose();
        _udpClient = null;
    }

    private async Task ReceiveLoopAsync(UdpClient client, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                ProcessIncomingDatagram(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) break;
                AppLogger.Warn($"[UdpListener] UDP Receive error on port {_port}: {ex.Message}", ex);
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    internal void ProcessIncomingDatagram(byte[] buffer, IPEndPoint remoteEndPoint)
    {
        if (!StatusPacket.TryParse(buffer, out var packet))
        {
            AppLogger.Debug($"[UdpListener] Dropped non-status or invalid packet ({buffer.Length} bytes) from {remoteEndPoint}.");
            return;
        }

        var senderIp = remoteEndPoint.Address.ToString();
        var serverInfo = new DiscoveredServer(
            packet.ServerId,
            packet.HostName,
            senderIp,
            packet.MicrophoneName,
            packet.State,
            packet.Alerts,
            DateTime.UtcNow);

        bool isNewOrChanged = false;
        if (!_discoveredServers.TryGetValue(senderIp, out var existing))
        {
            isNewOrChanged = true;
            AppLogger.Info($"[UdpListener] Discovered new server '{serverInfo.HostName}' ({senderIp}) - State: {packet.State}, Alerts: {packet.Alerts}, Mic: '{packet.MicrophoneName}'.");
        }
        else if (existing.HostName != serverInfo.HostName ||
                 existing.MicrophoneName != serverInfo.MicrophoneName ||
                 existing.ServerId != serverInfo.ServerId ||
                 existing.LastState != serverInfo.LastState ||
                 existing.LastAlerts != serverInfo.LastAlerts ||
                 (DateTime.UtcNow - existing.LastSeen) > TimeSpan.FromSeconds(_retryTimeoutSeconds))
        {
            isNewOrChanged = true;
            AppLogger.Debug($"[UdpListener] Updated server info for '{serverInfo.HostName}' ({senderIp}) - State: {packet.State}, Alerts: {packet.Alerts}.");
        }

        _discoveredServers[senderIp] = serverInfo;

        if (isNewOrChanged)
        {
            DiscoveredServersUpdated?.Invoke();
        }

        PacketReceived?.Invoke(packet, remoteEndPoint);

        // Check if this packet matches our target server
        bool isTarget = false;
        lock (_lock)
        {
            if (string.IsNullOrEmpty(_targetServerIp))
            {
                // If no specific server target configured, first active server becomes target
                _targetServerIp = senderIp;
                isTarget = true;
                AppLogger.Info($"[UdpListener] Auto-selected active server at {senderIp} ('{packet.HostName}') as target server.");
            }
            else if (string.Equals(_targetServerIp, senderIp, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(_targetServerIp, packet.ServerId, StringComparison.OrdinalIgnoreCase))
            {
                isTarget = true;
            }

            if (isTarget)
            {
                _lastTargetPacketTime = DateTime.UtcNow;
            }
        }

        if (isTarget)
        {
            SetConnected(true);
            SetState(packet.State);
            SetAlerts(packet.Alerts);
        }
    }

    private void SetConnected(bool connected)
    {
        bool changed = false;
        lock (_lock)
        {
            if (_isConnected != connected)
            {
                _isConnected = connected;
                changed = true;
            }
        }

        if (changed)
        {
            AppLogger.Info($"[UdpListener] Target server connection state changed: isConnected={connected} (Server: '{_targetServerIp ?? "None"}').");
            TargetServerConnectionChanged?.Invoke(connected);
        }
    }

    private void SetState(MicState state)
    {
        bool changed = false;
        lock (_lock)
        {
            if (_lastReportedState != state)
            {
                _lastReportedState = state;
                changed = true;
            }
        }

        if (changed)
        {
            AppLogger.Info($"[UdpListener] Target server mic state changed: {_lastReportedState} -> {state}.");
            TargetServerStateChanged?.Invoke(state);
        }
    }

    private void SetAlerts(AlertFlags alerts)
    {
        bool changed = false;
        lock (_lock)
        {
            if (_lastReportedAlerts != alerts)
            {
                _lastReportedAlerts = alerts;
                changed = true;
            }
        }

        if (changed)
        {
            AppLogger.Info($"[UdpListener] Target server alerts changed: {_lastReportedAlerts} -> {alerts}.");
            TargetServerAlertsChanged?.Invoke(alerts);
        }
    }

    private void StartWatchdog()
    {
        lock (_lock)
        {
            _watchdogTimer?.Dispose();
            _watchdogTimer = new System.Threading.Timer(OnWatchdogTick, null, 1000, 1000);
        }
    }

    private void StopWatchdog()
    {
        lock (_lock)
        {
            _watchdogTimer?.Dispose();
            _watchdogTimer = null;
        }
    }

    private void OnWatchdogTick(object? state)
    {
        if (_disposed || _isPaused) return;

        bool timedOut = false;
        lock (_lock)
        {
            if (_isConnected)
            {
                var elapsed = DateTime.UtcNow - _lastTargetPacketTime;
                if (elapsed > TimeSpan.FromSeconds(_retryTimeoutSeconds))
                {
                    timedOut = true;
                }
            }
        }

        if (timedOut)
        {
            AppLogger.Warn($"[UdpListener] Target server '{_targetServerIp}' timed out (no packets for >{_retryTimeoutSeconds}s). Marked as disconnected.");
            SetConnected(false);
            SetState(MicState.Disconnected);
            SetAlerts(AlertFlags.None);
            DiscoveredServersUpdated?.Invoke();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        StopWatchdog();
        CloseSocket();
    }
}
