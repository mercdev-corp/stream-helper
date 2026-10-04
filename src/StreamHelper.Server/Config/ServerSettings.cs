using System.Text.Json;
using System.Text.Json.Serialization;
using StreamHelper.Shared.Common;
using StreamHelper.Shared.Protocol;

namespace StreamHelper.Server.Config;

[JsonSerializable(typeof(ServerSettings))]
internal partial class ServerSettingsJsonContext : JsonSerializerContext
{
}

public sealed class ServerSettings
{
    public const string SettingsFileName = "stream-helper-server-settings.json";

    public bool RunOnStartup { get; set; }
    public string? MicrophoneId { get; set; }
    public string? MicrophoneName { get; set; }
    public int Port { get; set; } = ProtocolConstants.DefaultPort;
    public int RetryTimeout { get; set; } = ProtocolConstants.DefaultRetryTimeoutSeconds;
    public bool IsPaused { get; set; }
    public bool DebugLogging { get; set; }

    public string ObsIp { get; set; } = "127.0.0.1";
    public int ObsPort { get; set; } = 4455;
    public string ObsPassword { get; set; } = string.Empty;
    public int SkippedFramesThreshold { get; set; } = 60;
    public string? ObsAudioDevice { get; set; }

    public static string GetFilePath(string? directory = null)
    {
        var baseDir = directory ?? AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, SettingsFileName);
    }

    public static ServerSettings Load(string? directory = null)
    {
        var path = GetFilePath(directory);
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize(json, ServerSettingsJsonContext.Default.ServerSettings);
                if (settings != null)
                {
                    if (settings.Port is < 1 or > 65535) settings.Port = ProtocolConstants.DefaultPort;
                    if (settings.RetryTimeout < 1) settings.RetryTimeout = ProtocolConstants.DefaultRetryTimeoutSeconds;
                    if (settings.DebugLogging) AppLogger.IsDebugEnabled = true;
                    AppLogger.Info($"[ServerSettings] Loaded server settings from '{path}'.");
                    AppLogger.Debug($"[ServerSettings] Port={settings.Port}, Mic='{settings.MicrophoneName}', ObsIp={settings.ObsIp}:{settings.ObsPort}, DebugLogging={settings.DebugLogging}, RunOnStartup={settings.RunOnStartup}, SkippedFrames={settings.SkippedFramesThreshold}");
                    return settings;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[ServerSettings] Failed to load server settings from '{path}'", ex);
            }
        }
        else
        {
            AppLogger.Info($"[ServerSettings] Settings file '{path}' not found; using defaults.");
        }

        return new ServerSettings();
    }

    public void Save(string? directory = null)
    {
        var path = GetFilePath(directory);
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, ServerSettingsJsonContext.Default.ServerSettings);
            File.WriteAllText(path, json);
            AppLogger.Info($"[ServerSettings] Saved server settings to '{path}'.");
            AppLogger.Debug($"[ServerSettings] Port={Port}, Mic='{MicrophoneName}', ObsIp={ObsIp}:{ObsPort}, DebugLogging={DebugLogging}, RunOnStartup={RunOnStartup}, SkippedFrames={SkippedFramesThreshold}");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[ServerSettings] Failed to save server settings to '{path}'", ex);
        }
    }
}
