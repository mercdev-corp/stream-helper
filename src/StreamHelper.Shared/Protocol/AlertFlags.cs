namespace StreamHelper.Shared.Protocol;

[Flags]
public enum AlertFlags : uint
{
    None = 0,
    MicMuted = 1 << 0,
    MicDisconnected = 1 << 1,
    ObsDisconnected = 1 << 2,
    ObsReconnecting = 1 << 3,
    ObsNetworkIssue = 1 << 4,
    ObsRenderIssue = 1 << 5,
    ObsCaptureDeviceDisconnected = 1 << 6,
    ObsCaptureDeviceMuted = 1 << 7,
    ObsSoundCaptureIssue = 1 << 8,
    GameAudioNotSelected = 1 << 9,
    GameAudioNotFound = 1 << 10,
    ServerDisconnected = 1 << 11
}
