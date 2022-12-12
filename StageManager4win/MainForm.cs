using System.Diagnostics;
using System.Numerics;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using static Windows.Win32.PInvoke;

namespace StageManager4win;

public partial class MainForm : Form
{
    private const int ThumbnailContentWidth = 160;
    private const int ThumbnailContentHeight = 120;
    private const int AppMargin = 30;

    public MainForm()
    {
        InitializeComponent();
    }

    private void MainForm_Load(object sender, EventArgs e)
    {
        var x = new WindowWatcher();
        var stages = new List<StageInfo>();
        foreach (var window in x.Windows)
        {
            stages.Add(new StageInfo
            {
                Windows = new[] { window },
            });
        }

        // FIXME: loop by app
        var windowOffset = new Point(0, 0);
        foreach (var (stage, stageIndex) in stages.Zip(Enumerable.Range(0, 5)))
        {
            Size tallestThumbnailSize = default;
            foreach (var window in stage.Windows.Reverse())
            {
                var hwnd = window.Handle;
                DwmRegisterThumbnail((HWND)Handle, hwnd, out var thumbnailId).ThrowOnFailure();

                DwmQueryThumbnailSourceSize(thumbnailId, out var sourceSize).ThrowOnFailure();
                var thumbnailSize = Size.Truncate((Size)sourceSize * GetFitRatio(sourceSize));
                var thumbnailDest = new RECT(windowOffset, thumbnailSize);
                var thumbnailProps = new DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY | DWM_TNP_VISIBLE | DWM_TNP_RECTDESTINATION,
                    opacity = 255 * 100 / 100,
                    fSourceClientAreaOnly = false,
                    fVisible = true,
                    rcDestination = thumbnailDest,
                };
                DwmUpdateThumbnailProperties(thumbnailId, thumbnailProps).ThrowOnFailure();

                windowOffset = Point.Add(windowOffset, new Size(10, 0));
                tallestThumbnailSize = new[] { tallestThumbnailSize, thumbnailSize }.MaxBy(x => x.Height);
            }

            windowOffset = new Point(0, windowOffset.Y + tallestThumbnailSize.Height + AppMargin);
        }
    }

    private static float GetFitRatio(Size sourceSize)
    {
        var widthRatio = (float)ThumbnailContentWidth / sourceSize.Width;
        var heightRatio = (float)ThumbnailContentHeight / sourceSize.Height;
        return float.Min(widthRatio, heightRatio);
    }
}
