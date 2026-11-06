using StageManager4win.Core.Layout;
using StageManager4win.Core.Settings;
using StageManager4win.Core.Windows;
using StageManager4win.Overlay;
using StageManager4win.Rendering;
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

        if (args.Contains("--overlay-spike"))
        {
            RunOverlaySpike();
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

    private static void RunOverlaySpike()
    {
        var monitor = new DisplayManager().GetMonitors()[0];
        var log = Path.Combine(Path.GetTempPath(), "sm-overlay-spike.log");
        using var render = new RenderThread();
        using var overlay = new OverlayWindow(monitor);
        overlay.TileClicked += window => File.AppendAllText(log, $"{window.Value:X}\n");
        render.Start();
        overlay.Show();
        render.Post(new RenderCommand.CreateOverlay(monitor.Id, overlay.Handle, monitor.Dpi));

        var tiles = new[]
        {
            new TileVisual(
                new WindowId(1),
                new RectD(100, 100, 160, 120),
                12,
                IconBitmap.Solid(1, 1, 0xFF3B82F6),
                false,
                0),
            new TileVisual(
                new WindowId(2),
                new RectD(100, 250, 160, 90),
                12,
                IconBitmap.Solid(1, 1, 0xFF22C55E),
                false,
                0)
        };
        overlay.SetTiles(tiles);
        render.Post(new RenderCommand.UpdateOverlay(monitor.Id, tiles, true));
        Application.Run(overlay);
    }
}
