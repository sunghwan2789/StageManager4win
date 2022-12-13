using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace StageManager4win;

public partial class MainForm : Form
{
    private const int AppMargin = 30;
    private const int WindowMargin = 10;

    private readonly WindowWatcher _windowWatcher;
    private readonly Dictionary<HWND, Thumbnail> _thumbnails = new();
    private readonly List<StageInfo> _stages = new();

    private IEnumerable<Thumbnail>? _inflatedThumbnails;

    public MainForm()
    {
        InitializeComponent();

        _windowWatcher = new WindowWatcher((HWND)Handle);
        _windowWatcher.WindowCreated += window =>
        {
            _thumbnails[window.Handle] = new Thumbnail((HWND)Handle, window.Handle);

            _stages.Add(new StageInfo
            {
                Windows = new[] { window },
            });

            Invoke(Invalidate);
        };
        _windowWatcher.WindowDestroyed += window =>
        {
            if (_thumbnails.Remove(window.Handle, out var thumbnail))
            {
                thumbnail.Dispose();
            }

            _stages.RemoveAll(x => x.Windows.Any(x => x.Handle == window.Handle));

            Invoke(Invalidate);
        };
    }

    private void MainForm_Load(object sender, EventArgs e)
    {
        foreach (var window in _windowWatcher.Windows)
        {
            _thumbnails[window.Handle] = new Thumbnail((HWND)Handle, window.Handle);
            _stages.Add(new StageInfo
            {
                Windows = new[] { window },
            });
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // FIXME: loop by app
        var windowOffset = new Point(WindowMargin, WindowMargin);
        foreach (var (stage, stageIndex) in _stages.Zip(Enumerable.Range(0, 5)))
        {
            Size tallestThumbnailSize = default;
            foreach (var window in stage.Windows.Reverse())
            {
                var thumbnail = _thumbnails[window.Handle];
                thumbnail.Location = windowOffset;
                thumbnail.Update();

                windowOffset.Offset(WindowMargin, 0);
                tallestThumbnailSize = new[] { tallestThumbnailSize, thumbnail.Size }.MaxBy(x => x.Height);
            }

            windowOffset.X = WindowMargin;
            windowOffset.Offset(0, tallestThumbnailSize.Height + AppMargin);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var location = e.Location;

        var hit = _stages.Find(stage => stage
            .Windows
            .Select(window => _thumbnails[window.Handle])
            .Any(thumbnail => thumbnail.Rectangle.Contains(location))
        );

        if (_inflatedThumbnails is { })
        {
            foreach (var thumbnail in _inflatedThumbnails)
            {
                thumbnail.Inflate = false;
            }
        }

        if (hit is null)
        {
            _inflatedThumbnails = null;
            Invalidate();
            return;
        }

        _inflatedThumbnails = hit.Windows.Select(window => _thumbnails[window.Handle]).ToList();
        foreach (var thumbnail in _inflatedThumbnails)
        {
            thumbnail.Inflate = true;
        }
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        var location = e.Location;

        var hit = _stages.Find(stage => stage
            .Windows
            .Select(window => _thumbnails[window.Handle])
            .Any(thumbnail => thumbnail.Rectangle.Contains(location))
        );

        if (hit is null)
        {
            return;
        }

        foreach (var window in hit.Windows)
        {
            PInvoke.ShowWindow(window.Handle, SHOW_WINDOW_CMD.SW_NORMAL);
            PInvoke.SetForegroundWindow(window.Handle);
        }
    }
}
