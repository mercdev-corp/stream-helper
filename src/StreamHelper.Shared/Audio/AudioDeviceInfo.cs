namespace StreamHelper.Shared.Audio;

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    bool IsDefault = false,
    bool IsDefaultConsole = false,
    bool IsDefaultCommunications = false);
