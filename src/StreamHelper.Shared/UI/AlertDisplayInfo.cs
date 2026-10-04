using StreamHelper.Shared.Protocol;

namespace StreamHelper.Shared.UI;

public static class AlertDisplayInfo
{
    public static string GetDescription(AlertFlags flag) => flag switch
    {
        AlertFlags.ServerDisconnected => "Server not found",
        AlertFlags.ObsDisconnected => "OBS is not running",
        AlertFlags.ObsReconnecting => "OBS reconnecting",
        AlertFlags.ObsNetworkIssue => "OBS network issue",
        AlertFlags.ObsRenderIssue => "OBS render issue",
        AlertFlags.MicMuted => "Microphone muted",
        AlertFlags.MicDisconnected => "Microphone disconnected",
        AlertFlags.ObsCaptureDeviceDisconnected => "OBS capture device disconnected",
        AlertFlags.ObsCaptureDeviceMuted => "OBS capture device muted",
        AlertFlags.ObsSoundCaptureIssue => "OBS sound capture issue",
        AlertFlags.GameAudioNotSelected => "Game audio not selected",
        AlertFlags.GameAudioNotFound => "Game audio not found",
        _ => "Helper is active"
    };

    public static string GetAssetFileName(AlertFlags flag) => flag switch
    {
        AlertFlags.ServerDisconnected => "server-disconnected.png",
        AlertFlags.ObsDisconnected => "obs-disconnected.png",
        AlertFlags.ObsReconnecting => "obs-reconnect.png",
        AlertFlags.ObsNetworkIssue => "obs-network-issue.png",
        AlertFlags.ObsRenderIssue => "obs-render-issue.png",
        AlertFlags.MicMuted => "mic-muted.png",
        AlertFlags.MicDisconnected => "device-disconnected.png",
        AlertFlags.ObsCaptureDeviceDisconnected => "device-disconnected.png",
        AlertFlags.ObsCaptureDeviceMuted => "sound-muted.png",
        AlertFlags.ObsSoundCaptureIssue => "sound-issue.png",
        AlertFlags.GameAudioNotSelected => "sound-muted.png",
        AlertFlags.GameAudioNotFound => "sound-issue.png",
        _ => "obs-logo-blue.png"
    };

    public static List<AlertFlags> GetActiveAlertList(AlertFlags alerts)
    {
        var list = new List<AlertFlags>();
        if (alerts == AlertFlags.None) return list;

        // Order of alerts in cycling:
        AlertFlags[] orderedFlags =
        [
            AlertFlags.ServerDisconnected,
            AlertFlags.ObsDisconnected,
            AlertFlags.ObsReconnecting,
            AlertFlags.ObsNetworkIssue,
            AlertFlags.ObsRenderIssue,
            AlertFlags.MicMuted,
            AlertFlags.MicDisconnected,
            AlertFlags.ObsCaptureDeviceDisconnected,
            AlertFlags.ObsCaptureDeviceMuted,
            AlertFlags.ObsSoundCaptureIssue,
            AlertFlags.GameAudioNotSelected,
            AlertFlags.GameAudioNotFound
        ];

        foreach (var flag in orderedFlags)
        {
            if (alerts.HasFlag(flag))
            {
                list.Add(flag);
            }
        }

        return list;
    }

    public static string FormatTooltip(AlertFlags alerts, bool isPaused)
    {
        if (isPaused) return "Helper is paused";
        var active = GetActiveAlertList(alerts);
        if (active.Count == 0) return "Helper is active";
        if (active.Count == 1) return GetDescription(active[0]);

        var lines = active.Select(a => $"• {GetDescription(a)}");
        var text = string.Join(Environment.NewLine, lines);
        return text.Length <= 63 ? text : text[..60] + "...";
    }
}
