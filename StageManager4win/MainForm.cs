using System.Diagnostics;
using System.Numerics;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using static Windows.Win32.PInvoke;

namespace StageManager4win;

public partial class MainForm : Form
{
    public MainForm()
    {
        InitializeComponent();
    }

    private void MainForm_Load(object sender, EventArgs e)
    {
        var x = new WindowWatcher();
        foreach (var (window, acc) in x.Windows.Reverse().Zip(Enumerable.Range(0, int.MaxValue)))
        {
            var hwnd = window.Handle;

            using var process = Process.GetProcessById(window.ProcessId);

            DwmRegisterThumbnail((HWND)Handle, hwnd, out var thumbnailId).ThrowOnFailure();

            DwmQueryThumbnailSourceSize(thumbnailId, out var sourceSize).ThrowOnFailure();
            var thumbnailSizeVector = new Vector2(sourceSize.Width, sourceSize.Height);
            thumbnailSizeVector = Vector2.Multiply(thumbnailSizeVector, 0.3f);
            var thumbnailDest = new RECT
            {
                left = acc * 10,
                top = acc * 10 + 100,
                right = acc * 10 + (int)thumbnailSizeVector.X,
                bottom = acc * 10 + 100 + (int)thumbnailSizeVector.Y,
            };
            //thumbnailSizeVector = Vector2.Clamp(thumbnailSizeVector, )
            var thumbnailProps = new DWM_THUMBNAIL_PROPERTIES
            {
                dwFlags = DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY | DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION,
                opacity = 255 * 100 / 100,
                fSourceClientAreaOnly = false,
                fVisible = true,
                rcDestination = thumbnailDest,
            };
            DwmUpdateThumbnailProperties(thumbnailId, thumbnailProps).ThrowOnFailure();

            flowLayoutPanel1.Controls.Add(new Label()
            {
                Text = $"{process.ProcessName}-{process.Id}({process.MainWindowTitle})",
                AutoSize = true,
            });
        }
    }
}
