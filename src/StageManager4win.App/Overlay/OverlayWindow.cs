using StageManager4win.Core.Layout;
using StageManager4win.Core.Windows;
using StageManager4win.Rendering;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace StageManager4win.Overlay;

internal sealed class OverlayWindow : Form
{
    private const int WmMouseActivate = 0x0021;
    private const int WmLeftButtonDown = 0x0201;
    private const nint MaNoActivate = 3;

    private readonly List<TilePlacement> _placements = new();
    private uint _dpi;
    private bool _visible = true;

    public OverlayWindow(MonitorDescriptor monitor)
    {
        Monitor = monitor;
        _dpi = monitor.Dpi;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(
            monitor.Bounds.Left,
            monitor.Bounds.Top,
            monitor.Bounds.Width,
            monitor.Bounds.Height);
    }

    public MonitorDescriptor Monitor { get; }

    public event Action<WindowId>? TileClicked;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style = unchecked((int)0x80000000); // WS_POPUP
            parameters.ExStyle |= 0x00000080  // WS_EX_TOOLWINDOW
                | 0x00000008                  // WS_EX_TOPMOST
                | 0x08000000                  // WS_EX_NOACTIVATE
                | 0x00200000;                 // WS_EX_NOREDIRECTIONBITMAP
            return parameters;
        }
    }

    public void SetTiles(IReadOnlyList<TileVisual> tiles)
    {
        _placements.Clear();
        _placements.AddRange(tiles.Select(tile => new TilePlacement(tile.Window, tile.BoundsDip)));
        UpdateRegion();
    }

    public void SetVisible(bool visible)
    {
        _visible = visible;
        UpdateRegion();
    }

    public void UpdateDpi(uint dpi)
    {
        _dpi = dpi;
        UpdateRegion();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PInvoke.SetWindowPos(
            new HWND(Handle),
            new HWND(-1), // HWND_TOPMOST
            Monitor.Bounds.Left,
            Monitor.Bounds.Top,
            Monitor.Bounds.Width,
            Monitor.Bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        UpdateRegion();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmMouseActivate)
        {
            message.Result = MaNoActivate;
            return;
        }

        if (message.Msg == WmLeftButtonDown)
        {
            var packed = message.LParam.ToInt64();
            var x = (short)(packed & 0xFFFF);
            var y = (short)((packed >> 16) & 0xFFFF);
            var dipX = x * 96.0 / _dpi;
            var dipY = y * 96.0 / _dpi;
            foreach (var placement in _placements)
            {
                var bounds = placement.Bounds;
                if (dipX >= bounds.X && dipX < bounds.X + bounds.Width &&
                    dipY >= bounds.Y && dipY < bounds.Y + bounds.Height)
                {
                    TileClicked?.Invoke(placement.Window);
                    break;
                }
            }
        }

        base.WndProc(ref message);
    }

    private unsafe void UpdateRegion()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var region = PInvoke.CreateRectRgn(0, 0, 0, 0);
        if (_visible)
        {
            foreach (var placement in _placements)
            {
                var bounds = placement.Bounds;
                var left = (int)Math.Round(bounds.X * _dpi / 96.0);
                var top = (int)Math.Round(bounds.Y * _dpi / 96.0);
                var right = (int)Math.Round((bounds.X + bounds.Width) * _dpi / 96.0);
                var bottom = (int)Math.Round((bounds.Y + bounds.Height) * _dpi / 96.0);
                var rectangle = PInvoke.CreateRectRgn(left, top, right, bottom);
                PInvoke.CombineRgn(region, region, rectangle, RGN_COMBINE_MODE.RGN_OR);
                PInvoke.DeleteObject(new HGDIOBJ(rectangle.Value));
            }
        }

        // SetWindowRgn owns the region on success.
        if (PInvoke.SetWindowRgn(new HWND(Handle), region, true) == 0)
        {
            PInvoke.DeleteObject(new HGDIOBJ(region.Value));
        }
    }
}
