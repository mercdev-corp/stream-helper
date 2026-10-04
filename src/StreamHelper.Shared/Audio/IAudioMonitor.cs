namespace StreamHelper.Shared.Audio;

public interface IAudioMonitor : IDisposable
{
    IReadOnlyList<AudioDeviceInfo> GetActiveCaptureDevices();
    IReadOnlyList<AudioDeviceInfo> GetActiveRenderDevices();
    AudioDeviceInfo? GetDefaultCaptureDevice();

    void StartMonitoring(string? targetDeviceId, int retryTimeoutSeconds = 5);
    void StopMonitoring();

    bool IsMuted { get; }
    bool IsConnected { get; }
    string? CurrentDeviceId { get; }
    string? CurrentDeviceName { get; }

    event Action<bool>? MuteChanged;
    event Action<bool>? ConnectionChanged;
    event Action? DevicesChanged;
}
