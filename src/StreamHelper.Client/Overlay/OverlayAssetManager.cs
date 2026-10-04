using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using StreamHelper.Shared.Protocol;
using StreamHelper.Shared.UI;

namespace StreamHelper.Client.Overlay;

public sealed class OverlayAssetManager : IDisposable
{
    public static readonly string[] ManagedAssetFiles =
    [
        "mic-muted.png",
        "device-disconnected.png",
        "server-disconnected.png",
        "obs-disconnected.png",
        "obs-reconnect.png",
        "obs-network-issue.png",
        "obs-render-issue.png",
        "sound-issue.png",
        "sound-muted.png"
    ];

    private readonly string _cacheDirectory;
    private readonly Dictionary<string, Bitmap> _masterAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap> _cachedAssets = new(StringComparer.OrdinalIgnoreCase);
    private Size _cachedSize;

    public Size CurrentSize => _cachedSize;

    public OverlayAssetManager(string? baseDirectory = null)
    {
        var baseDir = baseDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
        _cacheDirectory = Path.Combine(baseDir, "cache");

        LoadMasterAssets(baseDir);
    }

    private void LoadMasterAssets(string baseDir)
    {
        foreach (var file in ManagedAssetFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(file);
            var candidates = new[]
            {
                $"{baseName}-2048.png",
                $"{baseName}-256.png",
                file
            };

            var bmp = LoadBitmap(baseDir, candidates);
            if (bmp == null)
            {
                // Fallbacks from StatusIconGenerator
                bmp = file switch
                {
                    "mic-muted.png" => StatusIconGenerator.GetMicMutedBitmap(),
                    "device-disconnected.png" => StatusIconGenerator.GetDeviceDisconnectedBitmap(),
                    "server-disconnected.png" => StatusIconGenerator.GetServerDisconnectedBitmap(),
                    "obs-disconnected.png" => StatusIconGenerator.GetObsDisconnectedBitmap(),
                    "obs-reconnect.png" => StatusIconGenerator.GetObsReconnectBitmap(),
                    "obs-network-issue.png" => StatusIconGenerator.GetObsNetworkIssueBitmap(),
                    "obs-render-issue.png" => StatusIconGenerator.GetObsRenderIssueBitmap(),
                    "sound-issue.png" => StatusIconGenerator.GetSoundIssueBitmap(),
                    "sound-muted.png" => StatusIconGenerator.GetSoundMutedBitmap(),
                    _ => null
                };
            }

            bmp ??= GenerateDefaultMaster(baseName, 256);
            _masterAssets[file] = bmp;
        }
    }

    private static Bitmap? LoadBitmap(string baseDir, params string[] filenames)
    {
        foreach (var filename in filenames)
        {
            var diskPaths = new[]
            {
                Path.Combine(baseDir, "assets", filename),
                Path.Combine(baseDir, "..", "..", "..", "assets", filename),
                Path.Combine(Directory.GetCurrentDirectory(), "assets", filename)
            };

            foreach (var p in diskPaths)
            {
                if (File.Exists(p))
                {
                    try
                    {
                        using var stream = File.OpenRead(p);
                        using var temp = new Bitmap(stream);
                        return new Bitmap(temp);
                    }
                    catch
                    {
                    }
                }
            }
        }

        return null;
    }

    public void PreRenderAndCache(int width, int height)
    {
        width = Math.Max(32, Math.Min(width, 1024));
        height = Math.Max(32, Math.Min(height, 1024));

        _cachedSize = new Size(width, height);

        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }

            // Clean up any legacy dimension-suffixed cache files (e.g. *-*x*.png)
            foreach (var legacyFile in Directory.EnumerateFiles(_cacheDirectory, "*-*x*.png"))
            {
                try { File.Delete(legacyFile); } catch { }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to prepare asset cache directory: {ex.Message}");
        }

        foreach (var file in ManagedAssetFiles)
        {
            if (_masterAssets.TryGetValue(file, out var master) && master != null)
            {
                if (_cachedAssets.TryGetValue(file, out var oldBmp))
                {
                    oldBmp?.Dispose();
                }

                var scaled = ScaleBitmapHighQuality(master, width, height);
                _cachedAssets[file] = scaled;

                try
                {
                    var baseName = Path.GetFileNameWithoutExtension(file);
                    var cachePath = Path.Combine(_cacheDirectory, $"{baseName}_resized.png");
                    SaveBitmapToFile(scaled, cachePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write asset cache for {file}: {ex.Message}");
                }
            }
        }
    }

    private static void SaveBitmapToFile(Bitmap bmp, string path)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        File.WriteAllBytes(path, ms.ToArray());
    }

    public Bitmap? GetBitmap(string filename)
    {
        return _cachedAssets.TryGetValue(filename, out var bmp) ? bmp : null;
    }

    public Bitmap? GetBitmap(MicState state)
    {
        return state switch
        {
            MicState.Muted => GetBitmap("mic-muted.png"),
            MicState.Disconnected => GetBitmap("server-disconnected.png") ?? GetBitmap("device-disconnected.png"),
            _ => null
        };
    }

    public Bitmap? GetAlertBitmap(AlertFlags flag)
    {
        string filename = AlertDisplayInfo.GetAssetFileName(flag);
        return GetBitmap(filename) ?? GetBitmap("mic-muted.png");
    }

    public Bitmap? GetMutedBitmap() => GetBitmap("mic-muted.png");
    public Bitmap? GetDisconnectedBitmap() => GetBitmap("server-disconnected.png");

    public static Bitmap ScaleBitmapHighQuality(Bitmap source, int width, int height)
    {
        var dest = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(dest);

        g.Clear(Color.Transparent);
        g.CompositingMode = CompositingMode.SourceOver;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float scale = Math.Min((float)width / source.Width, (float)height / source.Height);
        int targetW = Math.Max(1, (int)Math.Round(source.Width * scale));
        int targetH = Math.Max(1, (int)Math.Round(source.Height * scale));
        int targetX = (width - targetW) / 2;
        int targetY = (height - targetH) / 2;

        using var wrapMode = new ImageAttributes();
        wrapMode.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(source, new Rectangle(targetX, targetY, targetW, targetH), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, wrapMode);

        return dest;
    }

    private static Bitmap GenerateDefaultMaster(string name, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color col = name.Contains("muted") || name.Contains("issue") || name.Contains("disconnected")
            ? Color.FromArgb(220, 53, 69)
            : Color.FromArgb(0, 120, 215);

        using var brush = new SolidBrush(col);
        g.FillEllipse(brush, 10, 10, size - 20, size - 20);

        return bmp;
    }

    public void Dispose()
    {
        foreach (var bmp in _masterAssets.Values) bmp.Dispose();
        _masterAssets.Clear();

        foreach (var bmp in _cachedAssets.Values) bmp.Dispose();
        _cachedAssets.Clear();
    }
}
