using System.Reflection;

namespace StreamHelper.Shared.Common;

/// <summary>
/// Provides application version information embedded at compile-time.
/// </summary>
public static class AppVersion
{
    private static readonly Lazy<string> _rawVersionLazy = new(ResolveEntryAssemblyRawVersion);
    private static readonly Lazy<string> _displayVersionLazy = new(() => FormatDisplayVersion(_rawVersionLazy.Value));

    /// <summary>
    /// Gets the raw version string (e.g. "0.1.0").
    /// </summary>
    public static string RawVersion => _rawVersionLazy.Value;

    /// <summary>
    /// Gets the display version string formatted with a 'v' prefix (e.g. "v0.1.0").
    /// </summary>
    public static string DisplayVersion => _displayVersionLazy.Value;

    /// <summary>
    /// Extracts and normalizes the version string from the specified assembly.
    /// </summary>
    public static string ExtractVersion(Assembly? assembly)
    {
        if (assembly == null)
        {
            return "0.1.0";
        }

        string? informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return NormalizeRawVersion(informationalVersion);
        }

        var assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion != null)
        {
            string verStr = assemblyVersion.Revision > 0
                ? assemblyVersion.ToString()
                : (assemblyVersion.Build >= 0
                    ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}"
                    : $"{assemblyVersion.Major}.{assemblyVersion.Minor}");
            return NormalizeRawVersion(verStr);
        }

        return "0.1.0";
    }

    /// <summary>
    /// Normalizes a version string by stripping build metadata (after '+'), leading 'v', and whitespace.
    /// </summary>
    public static string NormalizeRawVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "0.1.0";
        }

        string trimmed = raw.Trim();

        int plusIndex = trimmed.IndexOf('+');
        if (plusIndex >= 0)
        {
            trimmed = trimmed[..plusIndex].Trim();
        }

        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..].Trim();
        }

        return string.IsNullOrWhiteSpace(trimmed) ? "0.1.0" : trimmed;
    }

    /// <summary>
    /// Formats the raw version string for display (e.g. "v0.1.0").
    /// </summary>
    public static string FormatDisplayVersion(string? rawVersion)
    {
        string normalized = NormalizeRawVersion(rawVersion);
        return $"v{normalized}";
    }

    private static string ResolveEntryAssemblyRawVersion()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            var name = entryAssembly.GetName().Name ?? string.Empty;
            if (name.StartsWith("StreamHelper", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractVersion(entryAssembly);
            }
        }

        return ExtractVersion(typeof(AppVersion).Assembly);
    }
}
