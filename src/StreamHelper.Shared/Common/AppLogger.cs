using System.Diagnostics;
using System.Text;

namespace StreamHelper.Shared.Common;

public static class AppLogger
{
    private static readonly object s_lock = new();
    private static string? s_logFilePath;
    private static string? s_appName;
    private static bool s_debugEnabled;
    private static bool s_initialized;
    private static bool s_sessionHeaderWritten;

    public static string LogFilePath => s_logFilePath ?? string.Empty;

    public static bool IsDebugEnabled
    {
        get { lock (s_lock) return s_debugEnabled; }
        set
        {
            lock (s_lock)
            {
                if (s_debugEnabled == value) return;
                s_debugEnabled = value;
                if (s_debugEnabled)
                {
                    EnsureRotatedAndHeaderWrittenLocked();
                }
            }
        }
    }

    public static void ResetForTesting()
    {
        lock (s_lock)
        {
            s_logFilePath = null;
            s_appName = null;
            s_debugEnabled = false;
            s_initialized = false;
            s_sessionHeaderWritten = false;
        }
    }

    public static void Initialize(string appName, string? directory = null, bool debugEnabled = false)
    {
        lock (s_lock)
        {
            var baseDir = directory ?? AppDomain.CurrentDomain.BaseDirectory;
            s_appName = appName;
            s_logFilePath = Path.Combine(baseDir, $"{appName.ToLowerInvariant()}.log");
            s_debugEnabled = debugEnabled;

            if (!s_initialized)
            {
                s_initialized = true;

                if (s_debugEnabled)
                {
                    EnsureRotatedAndHeaderWrittenLocked();
                }
            }
        }
    }

    private static void EnsureRotatedAndHeaderWrittenLocked()
    {
        if (s_sessionHeaderWritten) return;
        s_sessionHeaderWritten = true;

        if (!string.IsNullOrEmpty(s_logFilePath))
        {
            try
            {
                if (File.Exists(s_logFilePath))
                {
                    var info = new FileInfo(s_logFilePath);
                    if (info.Length > 5 * 1024 * 1024)
                    {
                        var dir = Path.GetDirectoryName(s_logFilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                        var oldPath = Path.Combine(dir, $"{s_appName?.ToLowerInvariant() ?? "app"}.old.log");
                        File.Move(s_logFilePath, oldPath, overwrite: true);
                    }
                }
            }
            catch
            {
            }
        }

        if (!string.IsNullOrEmpty(s_appName))
        {
            WriteEntryToFileLocked("INFO", $"=== {s_appName} session started (PID={Environment.ProcessId}, OS={Environment.OSVersion}, .NET={Environment.Version}) ===");
        }
    }

    public static void Debug(string message, Exception? ex = null)
    {
        lock (s_lock)
        {
            if (!s_debugEnabled) return;
        }
        var sb = new StringBuilder(message);
        if (ex != null)
        {
            sb.AppendLine();
            sb.Append(FormatFullException(ex));
        }
        WriteEntry("DEBUG", sb.ToString());
    }

    public static void Info(string message, Exception? ex = null)
    {
        lock (s_lock)
        {
            if (!s_debugEnabled) return;
        }
        var sb = new StringBuilder(message);
        if (ex != null)
        {
            sb.AppendLine();
            sb.Append(FormatFullException(ex));
        }
        WriteEntry("INFO", sb.ToString());
    }

    public static void Warn(string message, Exception? ex = null)
    {
        lock (s_lock)
        {
            if (!s_debugEnabled) return;
        }
        var sb = new StringBuilder(message);
        if (ex != null)
        {
            sb.AppendLine();
            sb.Append(FormatFullException(ex));
        }
        WriteEntry("WARN", sb.ToString());
    }

    public static void Error(string message, Exception? ex = null)
    {
        lock (s_lock)
        {
            if (!s_debugEnabled) return;
        }
        var sb = new StringBuilder(message);
        if (ex != null)
        {
            sb.AppendLine();
            sb.Append(FormatFullException(ex));
        }
        WriteEntry("ERROR", sb.ToString());
    }

    public static void LogUnhandledException(string source, Exception? ex)
    {
        lock (s_lock)
        {
            if (!s_debugEnabled) return;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"!!! FATAL / UNHANDLED EXCEPTION [{source}] !!!");
        if (ex != null)
        {
            sb.Append(FormatFullException(ex));
        }
        else
        {
            sb.Append("No exception object available.");
        }
        WriteEntry("FATAL", sb.ToString());
    }

    private static string FormatFullException(Exception ex)
    {
        var sb = new StringBuilder();
        Exception? cur = ex;
        int level = 0;
        while (cur != null)
        {
            if (level > 0) sb.AppendLine($"--- Inner Exception [{level}] ---");
            sb.AppendLine($"{cur.GetType().FullName}: {cur.Message}");
            if (cur.StackTrace != null)
            {
                sb.AppendLine(cur.StackTrace);
            }
            cur = cur.InnerException;
            level++;
        }
        return sb.ToString().TrimEnd();
    }

    private static void WriteEntry(string level, string message)
    {
        lock (s_lock)
        {
            if (!s_debugEnabled || string.IsNullOrEmpty(s_logFilePath)) return;
            WriteEntryToFileLocked(level, message);
        }
    }

    private static void WriteEntryToFileLocked(string level, string message)
    {
        if (string.IsNullOrEmpty(s_logFilePath)) return;

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string threadId = Environment.CurrentManagedThreadId.ToString().PadLeft(2);
        string line = $"[{timestamp}] [T{threadId}] [{level,-5}] {message}";

        System.Diagnostics.Debug.WriteLine(line);

        try
        {
            var dir = Path.GetDirectoryName(s_logFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(s_logFilePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
        }
    }
}
