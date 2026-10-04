using System.Runtime.Versioning;
using StreamHelper.Server.Config;
using StreamHelper.Shared.Common;

namespace StreamHelper.Server;

[SupportedOSPlatform("windows")]
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            AppLogger.Initialize("stream-helper-server");
            var settings = ServerSettings.Load();
            AppLogger.IsDebugEnabled = settings.DebugLogging;

            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                AppLogger.LogUnhandledException("Application.ThreadException", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                AppLogger.LogUnhandledException("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                AppLogger.LogUnhandledException("TaskScheduler.UnobservedTaskException", e.Exception);
                e.SetObserved();
            };

            using var singleInstance = SingleInstanceMutex.TryAcquire("StreamHelperServer");
            if (singleInstance == null)
            {
                AppLogger.Warn("Another instance is already running; notifying user.");
                MessageBox.Show(
                    "Stream Helper Server is already running in your system tray.\n\n" +
                    "Look for the Stream Helper icon in the notification area (click the '^' arrow on your taskbar near the clock) or double-click it to open Settings.",
                    "Stream Helper Server",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            bool isFirstRun = !File.Exists(ServerSettings.GetFilePath());
            var context = new ServerTrayApplicationContext();
            if (isFirstRun || (args != null && (args.Contains("--settings") || args.Contains("/settings"))))
            {
                context.ShowSettings();
            }

            Application.Run(context);
        }
        catch (Exception ex)
        {
            AppLogger.LogUnhandledException("Program.Main", ex);
            MessageBox.Show(
                $"Failed to start Stream Helper Server:\n\n{ex.Message}\n\nCheck stream-helper-server.log for details.",
                "Stream Helper Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}