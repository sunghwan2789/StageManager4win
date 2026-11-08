using StageManager4win.Accessibility;
using StageManager4win.Core.Diagnostics;
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
    private const int WmLeftButtonUp = 0x0202;
    private const int WmMouseWheel = 0x020A;
    private const int WmCaptureChanged = 0x0215;
    private const int WmMouseLeave = 0x02A3;
    private const int WmDpiChanged = 0x02E0;
    private const int WmGetObject = 0x003D;
    private const nint MaNoActivate = 3;
    // Matches the 1.05 hover scale in OverlayTarget; the region must include the growth.
    private const double HoverGrowth = 0.025;

    private readonly List<TilePlacement> _placements = new();
    private IReadOnlyList<TileVisual> _frontTiles = Array.Empty<TileVisual>();
    private StripUiaProvider? _uiaRoot;
    private uint _dpi;
    private bool _visible = true;
    private bool _trackingLeave;
    private WindowId? _hover;
    private RectD _stripBounds;
    private WindowId? _dragWindow;
    private PointF _dragStart;
    private bool _dragShift;
    private bool _dragging;
    private RectD? _ghost;

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

    public event Action<WindowId>? TileDropped;

    public event Action<WindowId>? TileInvoked;

    public event Action<WindowId?>? HoverChanged;

    public event Action<int>? ScrollRequested;

    public event Action? DragStarted;

    public event Action<WindowId, double, double>? DragMoved;

    public event Action? DragEnded;

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
        _frontTiles = tiles.Where(tile => tile.StackIndex == 0).ToArray();
        _placements.Clear();
        _placements.AddRange(
            _frontTiles.Select(tile => new TilePlacement(tile.Window, TileStack.HitBounds(tile.BoundsDip))));
        _uiaRoot?.Update(_frontTiles, Left, Top, _dpi);
        UpdateRegion();
    }

    public void SetStripBounds(RectD boundsDip) => _stripBounds = boundsDip;

    public void SetVisible(bool visible)
    {
        _visible = visible;
        UpdateRegion();
    }

    public void UpdateDpi(uint dpi)
    {
        _dpi = dpi;
        _uiaRoot?.Update(_frontTiles, Left, Top, _dpi);
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
        try
        {
            switch (message.Msg)
            {
                case WmMouseActivate:
                    message.Result = MaNoActivate;
                    return;
                case WmLeftButtonDown:
                    var pressedPoint = PointFromPacked(message.LParam);
                    if (TileHitTest.Hit(_placements, pressedPoint.X, pressedPoint.Y) is not { } pressed)
                    {
                        return;
                    }

                    _dragWindow = pressed;
                    _dragStart = pressedPoint;
                    _dragShift = (Control.ModifierKeys & Keys.Shift) != 0;
                    _dragging = false;
                    Capture = true;
                    return;
                case WmMouseMove:
                    TrackLeave();
                    if (_dragWindow is { } drag && Capture)
                    {
                        var movePoint = PointFromPacked(message.LParam);
                        var dx = movePoint.X - _dragStart.X;
                        var dy = movePoint.Y - _dragStart.Y;
                        if (!_dragging && !DragGesture.IsClick(dx, dy))
                        {
                            _dragging = true;
                            DragStarted?.Invoke();
                        }

                        if (_dragging)
                        {
                            DragMoved?.Invoke(drag, dx, dy);
                            var front = _frontTiles.FirstOrDefault(tile => tile.Window == drag);
                            _ghost = front is null
                                ? null
                                : new RectD(
                                    front.BoundsDip.X + dx,
                                    front.BoundsDip.Y + dy,
                                    front.BoundsDip.Width,
                                    front.BoundsDip.Height);
                            UpdateRegion();
                            return;
                        }
                    }

                    SetHover(HitFromPacked(message.LParam));
                    break;
                case WmLeftButtonUp:
                    var upPoint = PointFromPacked(message.LParam);
                    var released = _dragWindow;
                    var wasDragging = _dragging;
                    var shift = _dragShift;
                    _dragWindow = null;
                    _dragging = false;
                    _dragShift = false;
                    _ghost = null;
                    Capture = false;
                    UpdateRegion();
                    if (released is { } window)
                    {
                        if (!wasDragging && HitFromPacked(message.LParam) == window)
                        {
                            if (shift)
                            {
                                TileDropped?.Invoke(window);
                            }
                            else
                            {
                                TileClicked?.Invoke(window);
                            }
                        }
                        else if (wasDragging && DragGesture.Outside(_stripBounds, upPoint.X, upPoint.Y))
                        {
                            TileDropped?.Invoke(window);
                        }
                    }

                    if (wasDragging)
                    {
                        DragEnded?.Invoke();
                    }

                    return;
                case WmCaptureChanged:
                    if (_dragWindow is not null || _dragging)
                    {
                        _dragWindow = null;
                        _dragging = false;
                        _dragShift = false;
                        _ghost = null;
                        UpdateRegion();
                        DragEnded?.Invoke();
                    }

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
                    FileLogger.Default.Write("overlay", $"dpi={dpi}");
                    UpdateDpi(dpi);
                    // The overlay is a full-monitor surface, so the suggested
                    // rect is not authoritative. Keep the monitor bounds and
                    // skip DefWindowProc's resize.
                    PInvoke.SetWindowPos(
                        new HWND(Handle),
                        new HWND(0),
                        Monitor.Bounds.Left,
                        Monitor.Bounds.Top,
                        Monitor.Bounds.Width,
                        Monitor.Bounds.Height,
                        SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
                    DpiChanged?.Invoke(dpi);
                    return;
                case WmGetObject:
                    // UiaReturnRawElementProvider checks lParam itself; filtering here
                    // would hide the provider from the MSAA hit-test path.
                    if (EnsureUiaRoot() is { } root)
                    {
                        var result = UiaInterop.UiaReturnRawElementProvider(
                            Handle,
                            message.WParam,
                            message.LParam,
                            root);
                        if (result != 0)
                        {
                            message.Result = result;
                            return;
                        }
                    }

                    break;
            }

            base.WndProc(ref message);
        }
        catch (Exception exception)
        {
            FileLogger.Default.Write(
                "overlay",
                $"msg=0x{message.Msg:X4} hr=0x{exception.HResult:X8} {exception.GetType().Name}");
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_uiaRoot is not null)
        {
            UiaInterop.UiaReturnRawElementProvider(Handle, 0, 0, null);
            _uiaRoot = null;
        }

        base.OnHandleDestroyed(e);
    }

    private StripUiaProvider? EnsureUiaRoot()
    {
        if (!IsHandleCreated)
        {
            return null;
        }

        _uiaRoot ??= new StripUiaProvider(Handle, InvokeTile);
        _uiaRoot.Update(_frontTiles, Left, Top, _dpi);
        return _uiaRoot;
    }

    private void InvokeTile(WindowId window)
    {
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        try
        {
            BeginInvoke(() => TileInvoked?.Invoke(window));
        }
        catch (InvalidOperationException)
        {
            // The overlay is closing.
        }
    }

    private WindowId? HitFromPacked(nint packed)
    {
        var point = PointFromPacked(packed);
        return TileHitTest.Hit(_placements, point.X, point.Y);
    }

    private PointF PointFromPacked(nint packed)
    {
        var value = packed.ToInt64();
        var x = (short)(value & 0xFFFF);
        var y = (short)((value >> 16) & 0xFFFF);
        return new PointF((float)(x * 96.0 / _dpi), (float)(y * 96.0 / _dpi));
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
                AddRect(region, TileStack.HitBounds(placement.Bounds));
            }

            if (_ghost is { } ghost)
            {
                AddRect(region, ghost);
            }
        }

        // SetWindowRgn owns the region on success.
        if (PInvoke.SetWindowRgn(new HWND(Handle), region, true) == 0)
        {
            PInvoke.DeleteObject(new HGDIOBJ(region.Value));
        }
    }

    private unsafe void AddRect(HRGN region, RectD bounds)
    {
        var scale = _dpi / 96.0;
        var padX = (int)Math.Ceiling(bounds.Width * scale * HoverGrowth) + 1;
        var padY = (int)Math.Ceiling(bounds.Height * scale * HoverGrowth) + 1;
        var rectangle = PInvoke.CreateRectRgn(
            (int)Math.Round(bounds.X * scale) - padX,
            (int)Math.Round(bounds.Y * scale) - padY,
            (int)Math.Round((bounds.X + bounds.Width) * scale) + padX,
            (int)Math.Round((bounds.Y + bounds.Height) * scale) + padY);
        PInvoke.CombineRgn(region, region, rectangle, RGN_COMBINE_MODE.RGN_OR);
        PInvoke.DeleteObject(new HGDIOBJ(rectangle.Value));
    }
}
