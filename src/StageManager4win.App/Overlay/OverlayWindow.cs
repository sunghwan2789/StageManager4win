using StageManager4win.Core.Layout;
using StageManager4win.Core.Windows;
using StageManager4win.Rendering;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace StageManager4win.Overlay;

internal sealed class OverlayWindow : Form
{
    private const int WmMouseActivate = 0x0021;
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmMouseWheel = 0x020A;
    private const int WmMouseLeave = 0x02A3;
    private const int WmDpiChanged = 0x02E0;
    private const nint MaNoActivate = 3;
    // Matches the 1.05 hover scale in OverlayTarget; the region must include the growth.
    private const double HoverGrowth = 0.025;

    private readonly List<TilePlacement> _placements = new();
    private uint _dpi;
    private bool _visible = true;
    private bool _trackingLeave;
    private WindowId? _hover;

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

    public event Action<WindowId?>? HoverChanged;

    public event Action<int>? ScrollRequested;

    public new event Action<uint>? DpiChanged;

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
        switch (message.Msg)
        {
            case WmMouseActivate:
                message.Result = MaNoActivate;
                return;
            case WmLeftButtonDown:
                var clicked = HitFromPacked(message.LParam);
                if (clicked is { } window)
                {
                    TileClicked?.Invoke(window);
                }

                return;
            case WmMouseMove:
                TrackLeave();
                SetHover(HitFromPacked(message.LParam));
                break;
            case WmMouseLeave:
                _trackingLeave = false;
                SetHover(null);
                break;
            case WmMouseWheel:
                var delta = (short)((message.WParam.ToInt64() >> 16) & 0xFFFF);
                ScrollRequested?.Invoke(delta);
                break;
            case WmDpiChanged:
                var dpi = (uint)(message.WParam.ToInt64() & 0xFFFF);
                UpdateDpi(dpi);
                DpiChanged?.Invoke(dpi);
                break;
        }

        base.WndProc(ref message);
    }

    private WindowId? HitFromPacked(nint packed)
    {
        var value = packed.ToInt64();
        var x = (short)(value & 0xFFFF);
        var y = (short)((value >> 16) & 0xFFFF);
        return TileHitTest.Hit(_placements, x * 96.0 / _dpi, y * 96.0 / _dpi);
    }

    private void SetHover(WindowId? window)
    {
        if (_hover == window)
        {
            return;
        }

        _hover = window;
        HoverChanged?.Invoke(window);
    }

    private unsafe void TrackLeave()
    {
        if (_trackingLeave)
        {
            return;
        }

        var track = new TRACKMOUSEEVENT
        {
            cbSize = (uint)sizeof(TRACKMOUSEEVENT),
            dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE,
            hwndTrack = new HWND(Handle)
        };
        if (PInvoke.TrackMouseEvent(ref track))
        {
            _trackingLeave = true;
        }
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
                var scale = _dpi / 96.0;
                var padX = (int)Math.Ceiling(bounds.Width * scale * HoverGrowth) + 1;
                var padY = (int)Math.Ceiling(bounds.Height * scale * HoverGrowth) + 1;
                var rectangle = PInvoke.CreateRectRgn(
                    left - padX,
                    top - padY,
                    right + padX,
                    bottom + padY);
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
