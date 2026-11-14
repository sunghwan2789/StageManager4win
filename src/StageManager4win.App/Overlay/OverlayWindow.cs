using StageManager4win.Accessibility;
using StageManager4win.Core.Diagnostics;
using StageManager4win.Core.Layout;
using StageManager4win.Core.Stages;
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
    // The overlay never activates, so GetKeyState reads Shift as up.
    private const int MkShift = 0x0004;
    private const int WmMouseWheel = 0x020A;
    private const int WmCaptureChanged = 0x0215;
    private const int WmMouseLeave = 0x02A3;
    private const int WmDpiChanged = 0x02E0;
    private const int WmGetObject = 0x003D;
    private const nint MaNoActivate = 3;
    private const uint WsExTransparent = 0x00000020;
    private const double HoverGrowth = 0.025;

    private readonly List<TilePlacement> _placements = new();
    private IReadOnlyList<SetTileVisual> _sets = Array.Empty<SetTileVisual>();
    private IReadOnlyList<ListingRowPlacement> _listingRows = Array.Empty<ListingRowPlacement>();
    private StripUiaProvider? _uiaRoot;
    private uint _dpi;
    private bool _trackingLeave;
    private HitTarget? _hover;
    private HitTarget? _pressed;
    private RectD _stripBounds;
    private RectD? _modalDip;
    private OverlayPresentation _presentation = OverlayPresentation.Hidden;
    private WindowKey? _dragWindow;
    private string? _dragSetId;
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
        TopMost = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(
            monitor.Bounds.Left,
            monitor.Bounds.Top,
            monitor.Bounds.Width,
            monitor.Bounds.Height);
    }

    public MonitorDescriptor Monitor { get; }

    public event Action<WindowKey>? TileClicked;

    public event Action<WindowKey>? ShiftAddRequested;

    public event Action<WindowKey>? TileDropped;

    public event Action<string>? BadgeClicked;

    public event Action<WindowKey>? ListingRowClicked;

    public event Action? ListingDismissed;

    public event Action<HitTarget?>? HoverChanged;

    public event Action<int>? ScrollRequested;

    public event Action<string, int>? ListingScrollRequested;

    public event Action? DragStarted;

    public event Action<string, double, double>? DragMoved;

    public event Action? DragEnded;

    public new event Action<uint>? DpiChanged;

    private bool ListingOpen => _sets.Any(set => set.ListingOpen);

    private string? OpenListingSetId =>
        _sets.FirstOrDefault(set => set.ListingOpen)?.SetId;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.Style = unchecked((int)0x80000000); // WS_POPUP
            parameters.ExStyle |= 0x00000080  // WS_EX_TOOLWINDOW
                | 0x08000000                  // WS_EX_NOACTIVATE
                | 0x00200000;                 // WS_EX_NOREDIRECTIONBITMAP
            return parameters;
        }
    }

    public void SetTiles(
        IReadOnlyList<SetTileVisual> sets,
        IReadOnlyList<TilePlacement> placements)
    {
        _sets = sets;
        _placements.Clear();
        _placements.AddRange(placements);
        _listingRows = sets
            .Where(set => set.ListingOpen)
            .SelectMany(set => set.ListingRows.Select(row =>
                new ListingRowPlacement(set.SetId, row.Window, row.RowIndex, row.BoundsDip)))
            .ToArray();
        UpdateUia();
        UpdateRegion();
    }

    public void SetStripBounds(RectD boundsDip)
    {
        _stripBounds = boundsDip;
        UpdateUia();
        UpdateRegion();
    }

    public void SetPresentation(OverlayPresentation presentation, RectD? modalDip)
    {
        _presentation = presentation;
        _modalDip = modalDip;
        ApplyPresentation();
        UpdateUia();
        UpdateRegion();
    }

    public void UpdateDpi(uint dpi)
    {
        _dpi = dpi;
        UpdateUia();
        UpdateRegion();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        PInvoke.SetWindowPos(
            new HWND(Handle),
            new HWND(0),
            Monitor.Bounds.Left,
            Monitor.Bounds.Top,
            Monitor.Bounds.Width,
            Monitor.Bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                | SET_WINDOW_POS_FLAGS.SWP_NOZORDER
                | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
        ApplyPresentation();
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
                    OnLeftButtonDown(message);
                    return;
                case WmMouseMove:
                    OnMouseMove(message);
                    return;
                case WmLeftButtonUp:
                    OnLeftButtonUp(message);
                    return;
                case WmCaptureChanged:
                    if (_pressed is not null || _dragging)
                    {
                        var wasDragging = _dragging;
                        ClearPress();
                        UpdateRegion();
                        if (wasDragging)
                        {
                            DragEnded?.Invoke();
                        }
                    }

                    break;
                case WmMouseLeave:
                    _trackingLeave = false;
                    SetHover(null);
                    break;
                case WmMouseWheel:
                    var delta = (short)((message.WParam.ToInt64() >> 16) & 0xFFFF);
                    if (OpenListingSetId is { } listingSetId)
                    {
                        ListingScrollRequested?.Invoke(listingSetId, delta);
                    }
                    else
                    {
                        ScrollRequested?.Invoke(delta);
                    }

                    break;
                case WmDpiChanged:
                    var dpi = (uint)(message.WParam.ToInt64() & 0xFFFF);
                    FileLogger.Default.Write("overlay", $"dpi={dpi}");
                    UpdateDpi(dpi);
                    // The overlay is a full-monitor surface; keep the monitor bounds.
                    PInvoke.SetWindowPos(
                        new HWND(Handle),
                        new HWND(0),
                        Monitor.Bounds.Left,
                        Monitor.Bounds.Top,
                        Monitor.Bounds.Width,
                        Monitor.Bounds.Height,
                        SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                            | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
                    DpiChanged?.Invoke(dpi);
                    return;
                case WmGetObject:
                    // UiaReturnRawElementProvider checks lParam itself.
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

    private void OnLeftButtonDown(Message message)
    {
        var point = PointFromPacked(message.LParam);
        var target = HitFromPacked(message.LParam);
        if (ListingOpen)
        {
            if (target is not { Kind: HitKind.ListingRow or HitKind.Badge })
            {
                Raise(ListingDismissed);
                return;
            }
        }

        if (target is not { } pressed)
        {
            return;
        }

        _pressed = pressed;
        _dragSetId = pressed.SetId;
        _dragWindow = pressed.Kind == HitKind.Card ? pressed.Window : null;
        _dragStart = point;
        _dragShift = ((int)message.WParam & MkShift) != 0;
        _dragging = false;
        Capture = true;
    }

    private void OnMouseMove(Message message)
    {
        TrackLeave();
        if (_pressed is { } && Capture)
        {
            if (_dragWindow is { } drag)
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
                    var placement = PlacementFor(_dragSetId);
                    if (placement is null)
                    {
                        _ghost = null;
                    }
                    else
                    {
                        var (clampedDx, clampedDy) = ClampGhost(placement, dx, dy);
                        DragMoved?.Invoke(placement.SetId, clampedDx, clampedDy);
                        _ghost = new RectD(
                            placement.UnionBoundsDip.X + clampedDx,
                            placement.UnionBoundsDip.Y + clampedDy,
                            placement.UnionBoundsDip.Width,
                            placement.UnionBoundsDip.Height);
                    }

                    UpdateRegion();
                    return;
                }
            }
        }

        SetHover(HitFromPacked(message.LParam));
    }

    private void OnLeftButtonUp(Message message)
    {
        var upPoint = PointFromPacked(message.LParam);
        var pressed = _pressed;
        var wasDragging = _dragging;
        var shift = _dragShift;
        var dragWindow = _dragWindow;
        ClearPress();
        UpdateRegion();
        if (wasDragging)
        {
            if (dragWindow is { } window && ShouldDrop(upPoint))
            {
                Raise(TileDropped, window);
            }

            DragEnded?.Invoke();
            return;
        }

        if (pressed is not { } released
            || HitFromPacked(message.LParam) is not { } hit
            || !released.Equals(hit))
        {
            return;
        }

        switch (released.Kind)
        {
            case HitKind.Badge:
                Raise(BadgeClicked, released.SetId);
                break;
            case HitKind.ListingRow when released.Window is { } row:
                Raise(ListingRowClicked, row);
                break;
            case HitKind.Card when released.Window is { } card:
                if (shift)
                {
                    Raise(ShiftAddRequested, card);
                }
                else
                {
                    Raise(TileClicked, card);
                }

                break;
            case HitKind.SetBackground when FrontWindow(released.SetId) is { } front:
                if (shift)
                {
                    Raise(ShiftAddRequested, front);
                }
                else
                {
                    Raise(TileClicked, front);
                }

                break;
        }
    }

    private void ClearPress()
    {
        _pressed = null;
        _dragWindow = null;
        _dragSetId = null;
        _dragging = false;
        _dragShift = false;
        _ghost = null;
        Capture = false;
    }

    private StripUiaProvider? EnsureUiaRoot()
    {
        if (!IsHandleCreated)
        {
            return null;
        }

        _uiaRoot ??= new StripUiaProvider(
            Handle,
            window => Raise(TileClicked, window),
            window => Raise(ShiftAddRequested, window),
            setId => Raise(BadgeClicked, setId));
        UpdateUia();
        return _uiaRoot;
    }

    private void UpdateUia()
    {
        _uiaRoot?.Update(
            _sets,
            _placements,
            Left,
            Top,
            _dpi,
            _presentation is OverlayPresentation.Revealed or OverlayPresentation.Shielded,
            ListingOpen ? null : _stripBounds,
            _modalDip);
    }

    private void Raise<T>(Action<T>? handler, T value)
    {
        if (handler is null || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        try
        {
            BeginInvoke(() => handler(value));
        }
        catch (InvalidOperationException)
        {
            // The overlay is closing.
        }
    }

    private void Raise(Action? handler)
    {
        if (handler is null || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        try
        {
            BeginInvoke(() => handler());
        }
        catch (InvalidOperationException)
        {
            // The overlay is closing.
        }
    }

    private HitTarget? HitFromPacked(nint packed)
    {
        var point = PointFromPacked(packed);
        return TileHitTest.Hit(_placements, _listingRows, point.X, point.Y);
    }

    private PointF PointFromPacked(nint packed)
    {
        var value = packed.ToInt64();
        var x = (short)(value & 0xFFFF);
        var y = (short)((value >> 16) & 0xFFFF);
        return new PointF((float)(x * 96.0 / _dpi), (float)(y * 96.0 / _dpi));
    }

    private void SetHover(HitTarget? target)
    {
        if (Nullable.Equals(_hover, target))
        {
            return;
        }

        _hover = target;
        HoverChanged?.Invoke(target);
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

    private (double Dx, double Dy) ClampGhost(TilePlacement placement, double dx, double dy)
    {
        var width = ClientSize.Width * 96.0 / _dpi;
        var height = ClientSize.Height * 96.0 / _dpi;
        var hit = placement.UnionBoundsDip;
        return (
            Math.Clamp(dx, -hit.X, Math.Max(-hit.X, width - hit.X - hit.Width)),
            Math.Clamp(dy, -hit.Y, Math.Max(-hit.Y, height - hit.Y - hit.Height)));
    }

    private bool ShouldDrop(PointF point) =>
        DragGesture.ShouldDrop(
            new RectD(
                0,
                0,
                ClientSize.Width * 96.0 / _dpi,
                ClientSize.Height * 96.0 / _dpi),
            _stripBounds,
            point.X,
            point.Y);

    private TilePlacement? PlacementFor(string? setId)
    {
        if (setId is null)
        {
            return null;
        }

        foreach (var placement in _placements)
        {
            if (placement.SetId == setId)
            {
                return placement;
            }
        }

        return null;
    }

    private WindowKey? FrontWindow(string setId)
    {
        var placement = PlacementFor(setId);
        return placement is { Cards.Count: > 0 } value ? value.Cards[^1].Window : null;
    }

    private void ApplyPresentation()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        SetTransparent(_presentation is OverlayPresentation.Hidden or OverlayPresentation.Occluded);
        if (_presentation is OverlayPresentation.Hidden or OverlayPresentation.Occluded)
        {
            PInvoke.SetWindowPos(
                new HWND(Handle),
                new HWND(-2), // HWND_NOTOPMOST
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE
                    | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
                    | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
            PInvoke.SetWindowPos(
                new HWND(Handle),
                new HWND(1), // HWND_BOTTOM
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE
                    | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
                    | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }
        else
        {
            PInvoke.SetWindowPos(
                new HWND(Handle),
                new HWND(-1), // HWND_TOPMOST
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE
                    | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
                    | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        }
    }

    private void SetTransparent(bool transparent)
    {
        var hwnd = new HWND(Handle);
        var style = (uint)PInvoke.GetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var next = transparent ? style | WsExTransparent : style & ~WsExTransparent;
        if (next != style)
        {
            PInvoke.SetWindowLong(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (int)next);
        }
    }

    private unsafe void UpdateRegion()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var region = PInvoke.CreateRectRgn(0, 0, 0, 0);
        if (_presentation is not OverlayPresentation.Hidden)
        {
            foreach (var placement in _placements)
            {
                var bounds = Intersect(placement.HitBoundsDip, _stripBounds);
                if (bounds.Width > 0 && bounds.Height > 0)
                {
                    AddRect(region, Pad(bounds));
                }
            }

            foreach (var row in _listingRows)
            {
                if (row.BoundsDip.Width > 0 && row.BoundsDip.Height > 0)
                {
                    AddRect(region, Pad(row.BoundsDip));
                }
            }

            if (_ghost is { } ghost)
            {
                AddRect(region, ghost);
            }

            if (_modalDip is { } modal)
            {
                var popup = PInvoke.CreateRectRgn(
                    (int)Math.Round(modal.X * _dpi / 96.0),
                    (int)Math.Round(modal.Y * _dpi / 96.0),
                    (int)Math.Round((modal.X + modal.Width) * _dpi / 96.0),
                    (int)Math.Round((modal.Y + modal.Height) * _dpi / 96.0));
                PInvoke.CombineRgn(region, region, popup, RGN_COMBINE_MODE.RGN_DIFF);
                PInvoke.DeleteObject(new HGDIOBJ(popup.Value));
            }
        }

        // SetWindowRgn owns the region on success.
        if (PInvoke.SetWindowRgn(new HWND(Handle), region, true) == 0)
        {
            PInvoke.DeleteObject(new HGDIOBJ(region.Value));
        }
    }

    private static RectD Intersect(RectD a, RectD b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        return right > left && bottom > top
            ? new RectD(left, top, right - left, bottom - top)
            : default;
    }

    private static RectD Pad(RectD bounds)
    {
        var x = bounds.Width * HoverGrowth;
        var y = bounds.Height * HoverGrowth;
        return new RectD(bounds.X - x, bounds.Y - y, bounds.Width + x * 2, bounds.Height + y * 2);
    }

    private unsafe void AddRect(HRGN region, RectD bounds)
    {
        var scale = _dpi / 96.0;
        var rectangle = PInvoke.CreateRectRgn(
            (int)Math.Round(bounds.X * scale),
            (int)Math.Round(bounds.Y * scale),
            (int)Math.Round((bounds.X + bounds.Width) * scale),
            (int)Math.Round((bounds.Y + bounds.Height) * scale));
        PInvoke.CombineRgn(region, region, rectangle, RGN_COMBINE_MODE.RGN_OR);
        PInvoke.DeleteObject(new HGDIOBJ(rectangle.Value));
    }
}
