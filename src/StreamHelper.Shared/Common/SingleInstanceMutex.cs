using System.Security.Cryptography;
using System.Text;

namespace StreamHelper.Shared.Common;

public sealed class SingleInstanceMutex : IDisposable
{
    private Mutex? _mutex;
    private bool _hasHandle;
    private bool _disposed;

    public string MutexName { get; }
    public string PathHash { get; }
    public bool IsAcquired => _hasHandle;

    private SingleInstanceMutex(Mutex mutex, bool hasHandle, string mutexName, string pathHash)
    {
        _mutex = mutex;
        _hasHandle = hasHandle;
        MutexName = mutexName;
        PathHash = pathHash;
    }

    public static string ComputePathHash(string path)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        var bytes = Encoding.UTF8.GetBytes(normalizedPath);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes)[..16];
    }

    public static string GenerateMutexName(string appPrefix, string? directory = null)
    {
        var baseDir = directory ?? AppDomain.CurrentDomain.BaseDirectory;
        var hash = ComputePathHash(baseDir);
        return $"{appPrefix}_{hash}";
    }

    public static SingleInstanceMutex? TryAcquire(string appPrefix, string? directory = null)
    {
        var baseDir = directory ?? AppDomain.CurrentDomain.BaseDirectory;
        var hash = ComputePathHash(baseDir);
        var mutexName = $"{appPrefix}_{hash}";

        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: true, name: mutexName, out bool createdNew);
            if (!createdNew)
            {
                if (!mutex.WaitOne(0, false))
                {
                    AppLogger.Warn($"[SingleInstanceMutex] Could not acquire mutex '{mutexName}' - another instance is already running.");
                    mutex.Dispose();
                    return null;
                }
            }

            AppLogger.Info($"[SingleInstanceMutex] Acquired mutex '{mutexName}'.");
            return new SingleInstanceMutex(mutex, true, mutexName, hash);
        }
        catch (AbandonedMutexException)
        {
            if (mutex != null)
            {
                AppLogger.Warn($"[SingleInstanceMutex] Acquired abandoned mutex '{mutexName}'.");
                return new SingleInstanceMutex(mutex, true, mutexName, hash);
            }
            return null;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[SingleInstanceMutex] Exception while acquiring mutex '{mutexName}'", ex);
            mutex?.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hasHandle && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
                AppLogger.Debug($"[SingleInstanceMutex] Released mutex '{MutexName}'.");
            }
            catch (ApplicationException)
            {
            }
            _hasHandle = false;
        }

        _mutex?.Dispose();
        _mutex = null;
    }
}
