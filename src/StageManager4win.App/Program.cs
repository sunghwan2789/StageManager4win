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

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StageManager4win");
        Directory.CreateDirectory(directory);

        Application.Run(new TrayApplicationContext(new SettingsStore(directory)));
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
