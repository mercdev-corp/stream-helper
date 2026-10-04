using StreamHelper.Shared.Common;

namespace StreamHelper.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AppLoggerTests
{
    private static readonly object s_testLock = new();

    [TestCleanup]
    public void Cleanup()
    {
        AppLogger.ResetForTesting();
    }

    [TestMethod]
    public void AppLogger_WritesEntries_WithCorrectLevelsAndTimestamps()
    {
        lock (s_testLock)
        {
            var tempFolder = TestDirectory.Create("AppLoggerTest");

            try
            {
                AppLogger.ResetForTesting();
                AppLogger.Initialize("test-app", tempFolder, debugEnabled: false);

                var logFile = AppLogger.LogFilePath;

                // When debugEnabled is false, no file should be created and no entries written
                AppLogger.Info("Disabled informational message");
                AppLogger.Warn("Disabled warning message");
                AppLogger.Error("Disabled error message", new InvalidOperationException("Disabled boom"));
                AppLogger.Debug("Disabled debug message");

                Assert.IsFalse(File.Exists(logFile), "No log file should be created when debug logging is disabled");

                // Now enable debug logging and verify session started + subsequent entries are recorded
                AppLogger.IsDebugEnabled = true;

                AppLogger.Info("Informational test message");
                AppLogger.Warn("Warning test message");
                AppLogger.Error("Error test message", new InvalidOperationException("Inner test boom"));
                AppLogger.Debug("Debug test message");

                Assert.IsTrue(File.Exists(logFile), "Log file should be created after enabling debug logging");

                var content = File.ReadAllText(logFile);
                StringAssert.Contains(content, "session started");
                StringAssert.Contains(content, "[INFO ] Informational test message");
                StringAssert.Contains(content, "[WARN ] Warning test message");
                StringAssert.Contains(content, "[ERROR] Error test message");
                StringAssert.Contains(content, "InvalidOperationException: Inner test boom");
                StringAssert.Contains(content, "[DEBUG] Debug test message");

                Assert.DoesNotContain("Disabled informational message", content);
                Assert.DoesNotContain("Disabled warning message", content);
                Assert.DoesNotContain("Disabled error message", content);
                Assert.DoesNotContain("Disabled debug message", content);

                // Disable debug logging again and verify no further entries are appended
                AppLogger.IsDebugEnabled = false;
                AppLogger.Info("Subsequent message when disabled again");
                AppLogger.Error("Subsequent error when disabled again");

                var updatedContent = File.ReadAllText(logFile);
                Assert.DoesNotContain("Subsequent message when disabled again", updatedContent);
                Assert.DoesNotContain("Subsequent error when disabled again", updatedContent);
            }
            finally
            {
                AppLogger.ResetForTesting();
                if (Directory.Exists(tempFolder))
                {
                    try { Directory.Delete(tempFolder, true); } catch { }
                }
            }
        }
    }

    [TestMethod]
    public void AppLogger_WhenDebugLoggingDisabled_WritesNoLogsAtAll()
    {
        lock (s_testLock)
        {
            var tempFolder = TestDirectory.Create("AppLoggerDisabledTest");

            try
            {
                AppLogger.ResetForTesting();
                AppLogger.Initialize("disabled-app", tempFolder, debugEnabled: false);

                var logFile = AppLogger.LogFilePath;

                AppLogger.Info("Info message when disabled");
                AppLogger.Warn("Warn message when disabled");
                AppLogger.Error("Error message when disabled");
                AppLogger.Debug("Debug message when disabled");
                AppLogger.LogUnhandledException("CrashSource", new Exception("Crash"));

                Assert.IsFalse(File.Exists(logFile), "No log file should be created when debug logging is disabled");
            }
            finally
            {
                AppLogger.ResetForTesting();
                if (Directory.Exists(tempFolder))
                {
                    try { Directory.Delete(tempFolder, true); } catch { }
                }
            }
        }
    }

    [TestMethod]
    public void AppLogger_LogUnhandledException_FormatsExceptionDetails()
    {
        lock (s_testLock)
        {
            var tempFolder = TestDirectory.Create("AppLoggerCrashTest");

            try
            {
                AppLogger.ResetForTesting();
                AppLogger.Initialize("crash-test", tempFolder, debugEnabled: true);

                var logFile = AppLogger.LogFilePath;

                try
                {
                    throw new ArgumentException("Parameter is invalid", new NullReferenceException("Root cause null ref"));
                }
                catch (Exception ex)
                {
                    AppLogger.LogUnhandledException("TestCrashSource", ex);
                }

                var content = File.ReadAllText(logFile);
                StringAssert.Contains(content, "FATAL");
                StringAssert.Contains(content, "!!! FATAL / UNHANDLED EXCEPTION [TestCrashSource] !!!");
                StringAssert.Contains(content, "ArgumentException: Parameter is invalid");
                StringAssert.Contains(content, "NullReferenceException: Root cause null ref");
            }
            finally
            {
                AppLogger.ResetForTesting();
                if (Directory.Exists(tempFolder))
                {
                    try { Directory.Delete(tempFolder, true); } catch { }
                }
            }
        }
    }

    [TestMethod]
    public void AppLogger_RotatesFile_WhenSizeExceeds5MB()
    {
        lock (s_testLock)
        {
            var tempFolder = TestDirectory.Create("AppLoggerRotateTest");

            try
            {
                AppLogger.ResetForTesting();
                var logPath = Path.Combine(tempFolder, "rotate-app.log");
                var oldPath = Path.Combine(tempFolder, "rotate-app.old.log");

                // Pre-create a file with > 5MB size
                using (var fs = new FileStream(logPath, FileMode.Create, FileAccess.Write))
                {
                    fs.SetLength(5 * 1024 * 1024 + 1024); // 5MB + 1KB
                }

                Assert.IsTrue(File.Exists(logPath));
                Assert.IsFalse(File.Exists(oldPath));

                // Initialize with debugEnabled: true should detect > 5MB and move to .old.log
                AppLogger.Initialize("rotate-app", tempFolder, debugEnabled: true);

                Assert.IsTrue(File.Exists(oldPath), "Old rotated log should exist");
                Assert.IsTrue(File.Exists(logPath), "New active log file should have been created");

                var newContent = File.ReadAllText(logPath);
                StringAssert.Contains(newContent, "session started");
            }
            finally
            {
                AppLogger.ResetForTesting();
                if (Directory.Exists(tempFolder))
                {
                    try { Directory.Delete(tempFolder, true); } catch { }
                }
            }
        }
    }

    [TestMethod]
    public void AppLogger_WarnAndDebug_FormatsExceptions()
    {
        lock (s_testLock)
        {
            var tempFolder = TestDirectory.Create("AppLoggerExceptionTest");

            try
            {
                AppLogger.ResetForTesting();
                AppLogger.Initialize("exception-app", tempFolder, debugEnabled: true);

                var logFile = AppLogger.LogFilePath;
                var innerEx = new TimeoutException("Socket timed out");
                var outerEx = new IOException("Network stream failed", innerEx);

                AppLogger.Warn("Warning with exception", outerEx);
                AppLogger.Debug("Debug with exception", outerEx);
                AppLogger.Info("Info with exception", outerEx);

                var content = File.ReadAllText(logFile);
                StringAssert.Contains(content, "[WARN ] Warning with exception");
                StringAssert.Contains(content, "[DEBUG] Debug with exception");
                StringAssert.Contains(content, "[INFO ] Info with exception");
                StringAssert.Contains(content, "IOException: Network stream failed");
                StringAssert.Contains(content, "--- Inner Exception [1] ---");
                StringAssert.Contains(content, "TimeoutException: Socket timed out");
            }
            finally
            {
                AppLogger.ResetForTesting();
                if (Directory.Exists(tempFolder))
                {
                    try { Directory.Delete(tempFolder, true); } catch { }
                }
            }
        }
    }
}
