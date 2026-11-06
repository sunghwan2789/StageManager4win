using StageManager4win.Core.Settings;

namespace StageManager4win;

internal static class Program
{
    [STAThread]
    private static void Main()
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
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StageManager4win");
        Directory.CreateDirectory(directory);

        Application.Run(new TrayApplicationContext(new SettingsStore(directory)));
    }
}
