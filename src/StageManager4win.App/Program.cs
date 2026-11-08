using StageManager4win.Core.Diagnostics;
using StageManager4win.Core.Settings;
using StageManager4win.Windows;

namespace StageManager4win;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            MessageBox.Show(
                "StageManager4win requires Windows 11 22H2 or later.",
                "StageManager4win",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        if (args.Contains("--dump-windows"))
        {
            DumpWindows();
            return;
        }

        using var mutex = new Mutex(true, @"Local\StageManager4win", out var createdNew);
        if (!createdNew)
        {
            FileLogger.Default.Write("app", "already running");
            MessageBox.Show(
                "StageManager4win is already running.",
                "StageManager4win",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StageManager4win");
        Directory.CreateDirectory(directory);

        TrayApplicationContext? context = null;
        Application.ThreadException += (_, eventArgs) =>
        {
            FileLogger.Default.Write("error", eventArgs.Exception.ToString());
            context?.NotifyErrorOnce(eventArgs.Exception.Message);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            FileLogger.Default.Write(
                "fatal",
                eventArgs.ExceptionObject is Exception exception
                    ? exception.ToString()
                    : eventArgs.ExceptionObject.GetType().FullName ?? "unknown");

        FileLogger.Default.Write("app", "started");
        try
        {
            context = new TrayApplicationContext(new SettingsStore(directory));
            Application.Run(context);
        }
        finally
        {
            FileLogger.Default.Write("app", "stopped");
        }
    }

    private static void DumpWindows()
    {
        var monitors = new DisplayManager().GetMonitors();
        var windows = new WindowSource().Enumerate(monitors, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var path = Path.Combine(Path.GetTempPath(), "sm-windows.txt");
        File.WriteAllLines(path, windows.Select(window =>
            $"0x{window.Id.Value:X} {window.ProcessId} {window.AppId} {window.MonitorId} {window.Title} " +
            $"({window.Bounds.Left},{window.Bounds.Top},{window.Bounds.Width},{window.Bounds.Height})"));
    }
}
