namespace StreamHelper.Shared.Obs;

public enum ObsConnectionState
{
    Disconnected,
    Reconnecting,
    Connected
}

public sealed record ObsAudioMeterEventArgs(
    string InputName,
    double RmsDbfs,
    double PeakDbfs,
    DateTime Timestamp);

public interface IObsMonitor : IDisposable
{
    bool IsConnected { get; }
    bool IsStreaming { get; }
    bool IsRecording { get; }
    bool IsReconnecting { get; }
    bool HasNetworkIssue { get; }
    bool HasRenderIssue { get; }
    bool IsCaptureDeviceDisconnected { get; }
    bool IsCaptureDeviceMuted { get; }
    IReadOnlyList<string> AvailableAudioInputs { get; }

    ObsConnectionState ConnectionState { get; }
    bool HasNetworkCongestion => HasNetworkIssue;
    bool HasRenderLag => HasRenderIssue;
    bool HasEncodingLag => false;
    bool IsAudioSourceMissing => IsCaptureDeviceDisconnected;
    bool IsAudioSourceMuted => IsCaptureDeviceMuted;
    IReadOnlyList<string> GetAudioInputNames() => AvailableAudioInputs;

    event Action<bool>? ConnectionChanged;
    event Action? StateChanged;
    event Action<ObsAudioMeterEventArgs>? AudioMeterReceived;
    event Action<IReadOnlyList<string>>? AudioInputsChanged;

    event Action<ObsConnectionState>? ConnectionStateChanged;
    event Action? StatusUpdated;
    event Action<double, double>? AudioMeterUpdated;

    void Start();
    void Stop();
    void UpdateConfig(
        string host,
        int port,
        string? password,
        int retryTimeoutSeconds,
        int skippedFramesThreshold,
        string? audioDeviceName);

    void ConnectAsync(string host, int port, string? password);
    void DisconnectAsync();
    void SetAudioInputName(string? name);
    void SetSkippedFramesThreshold(int threshold);
}
