using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

namespace StreamHelper.Shared.Common;

public static class DonateUrlProvider
{
    public const string DefaultDonateUrl = "https://ko-fi.com/mercdev_pro";

    public static string GetDonateUrl()
    {
        // 1. Read embedded resource
        string? content = LoadResourceContent("DONATE");

        // 2. Parse and validate URL, or return default fallback
        return ParseAndValidateUrl(content, DefaultDonateUrl);
    }

    public static string ResolveDonateUrl(string? rawContent = null)
    {
        if (rawContent != null)
        {
            return ParseAndValidateUrl(rawContent, DefaultDonateUrl);
        }

        return GetDonateUrl();
    }

    public static string ParseAndValidateUrl(string? rawText, string fallback = DefaultDonateUrl)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return fallback;
        }

        string trimmed = rawText.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return trimmed;
        }

        return fallback;
    }

    public static bool IsValidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static bool OpenDonationPage()
    {
        string url = GetDonateUrl();
        return OpenUrl(url);
    }

    public static bool OpenUrl(string url)
    {
        try
        {
            if (!IsValidUrl(url))
            {
                AppLogger.Error($"Invalid donation URL format: {url}");
                MessageBox.Show(
                    "Unable to open donation link because the URL format is invalid.",
                    "Stream Helper",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            var psi = new ProcessStartInfo(url.Trim())
            {
                UseShellExecute = true
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Failed to open URL in browser: {url}", ex);
            MessageBox.Show(
                $"Failed to open the browser for donation link:\n{url}\n\nError: {ex.Message}",
                "Stream Helper",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    private static string? LoadResourceContent(string resourceName)
    {
        var assembly = typeof(DonateUrlProvider).Assembly;
        var names = assembly.GetManifestResourceNames();
        var match = names.FirstOrDefault(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            using var stream = assembly.GetManifestResourceStream(match);
            if (stream != null)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
        }

        return null;
    }
}
