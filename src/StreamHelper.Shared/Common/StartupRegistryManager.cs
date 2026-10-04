using System.Runtime.Versioning;
using Microsoft.Win32;

namespace StreamHelper.Shared.Common;

[SupportedOSPlatform("windows")]
public static class StartupRegistryManager
{
    public const string DefaultRunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string GetKeyName(string appPrefix, string exePath)
    {
        var hash = SingleInstanceMutex.ComputePathHash(Path.GetDirectoryName(exePath) ?? exePath);
        return $"{appPrefix}_{hash}";
    }

    public static bool IsStartupEnabled(string appPrefix, string? exePath = null, string subKeyPath = DefaultRunRegistryKey)
    {
        try
        {
            var path = exePath ?? Environment.ProcessPath ?? string.Empty;
            if (string.IsNullOrEmpty(path)) return false;

            var keyName = GetKeyName(appPrefix, path);

            using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: false);
            if (key == null) return false;

            var value = key.GetValue(keyName) as string;
            if (string.IsNullOrEmpty(value)) return false;

            var unquotedValue = value.Trim('"');
            var unquotedPath = path.Trim('"');

            return string.Equals(unquotedValue, unquotedPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartupEnabled(string appPrefix, bool enable, string? exePath = null, string subKeyPath = DefaultRunRegistryKey)
    {
        var path = exePath ?? Environment.ProcessPath ?? string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            throw new InvalidOperationException("Executable path could not be determined.");
        }

        var keyName = GetKeyName(appPrefix, path);

        using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
        if (key == null)
        {
            throw new InvalidOperationException($"Unable to open registry subkey: {subKeyPath}");
        }

        AppLogger.Info($"[StartupRegistryManager] SetStartupEnabled: appPrefix={appPrefix}, enable={enable}, path='{path}', keyName='{keyName}'");
        if (enable)
        {
            var command = $"\"{path}\"";
            key.SetValue(keyName, command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(keyName, throwOnMissingValue: false);
        }
    }
}
