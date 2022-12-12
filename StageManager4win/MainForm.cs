using Windows.Win32.Foundation;

namespace StageManager4win;

public partial class MainForm : Form
{
    private const int AppMargin = 30;
    private const int WindowMargin = 10;

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
                var thumbnail = new Thumbnail((HWND)Handle, window.Handle);
                thumbnail.Location = windowOffset;
                thumbnail.Update();

                windowOffset.Offset(WindowMargin, 0);
                tallestThumbnailSize = new[] { tallestThumbnailSize, thumbnail.Size }.MaxBy(x => x.Height);
            }

            windowOffset.X = 0;
            windowOffset.Offset(0, tallestThumbnailSize.Height + AppMargin);
        }
    }
}
