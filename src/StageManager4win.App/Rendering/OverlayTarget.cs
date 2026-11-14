using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using StageManager4win.Capture;
using StageManager4win.Core.Diagnostics;
using StageManager4win.Core.Layout;
using StageManager4win.Core.Stages;
using StageManager4win.Interop;
using StageManager4win.Overlay;
using Windows.Graphics;
using Windows.Graphics.DirectX;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Dxgi.Common;

namespace StageManager4win.Rendering;

internal sealed class OverlayTarget : IDisposable
{
    private const double BadgeSurfaceDip = 36;
    private const double BadgeRestDip = 28;
    private const double BadgeRadiusDip = 8;
    private const double ListingRadiusDip = 6;
    private const float InactiveDim = 0.65f;

    private readonly RenderDevice _device;
    private readonly IDCompositionTarget _target;
    private readonly IDCompositionVisual _root;
    private readonly IDCompositionEffectGroup _rootEffect;
    private readonly List<ElementTarget> _elements = new();
    private readonly uint _dpi;
    private HitTarget? _hover;
    private string? _ghostSetId;
    private string? _badgeHoverSetId;
    private float _ghostDeltaX;
    private float _ghostDeltaY;
    private float _badgeHoverProgress;
    private IDCompositionRectangleClip? _rootClip;
    private RectD _clipDip;
    private OverlayPresentation _presentation = OverlayPresentation.Hidden;
    private bool _listingOpen;

    public OverlayTarget(RenderDevice device, nint hwnd, string monitorId, uint dpi)
    {
        _device = device;
        _dpi = dpi;
        MonitorId = monitorId;
        device.DComp.CreateTargetForHwnd(new HWND(hwnd), true, out _target);
        device.DComp.CreateVisual(out _root);
        _target.SetRoot(_root);
        device.DComp.CreateEffectGroup(out _rootEffect);
        _root.SetEffect(_rootEffect);
    }

    public string MonitorId { get; }

    public void Update(
        IReadOnlyList<SetTileVisual> sets,
        IReadOnlyList<TilePlacement> placements,
        OverlayPresentation presentation,
        RectD clipDip)
    {
        _ = placements;
        _presentation = presentation;
        _clipDip = clipDip;
        _listingOpen = sets.Any(set => set.ListingOpen);
        var scale = _dpi / 96.0;
        ApplyRootClip(scale);

        var old = _elements.ToArray();
        var next = new List<ElementTarget>();
        foreach (var set in sets)
        {
            foreach (var card in set.Cards)
            {
                var width = Math.Max(1, (int)Math.Ceiling(card.BoundsDip.Width * scale));
                var height = Math.Max(1, (int)Math.Ceiling(card.BoundsDip.Height * scale));
                var borderPixels = set.HighContrast && card.StackIndex == 0
                    ? (uint)Math.Max(1, Math.Round(2 * scale))
                    : 0;
                var key = $"card|{set.SetId}|{card.Window.Window.Value}|{card.Window.ProcessId}|{width}x{height}|{card.Title}";
                var element = Find(old, key);
                if (element is null)
                {
                    element = CreateElement(
                        key,
                        ElementKind.Card,
                        set.SetId,
                        card.Window,
                        card.StackIndex,
                        0,
                        card.BoundsDip,
                        width,
                        height,
                        set.CornerRadiusDip,
                        card.Icon,
                        card.IsMinimized,
                        card.SourceHwnd,
                        set.HighContrast,
                        set.BorderBgra,
                        borderPixels,
                        scale);
                }
                else
                {
                    UpdateElement(
                        element,
                        card.BoundsDip,
                        card.Icon,
                        card.IsMinimized,
                        card.SourceHwnd,
                        set.HighContrast,
                        set.BorderBgra,
                        borderPixels,
                        scale);
                }

                next.Add(element);
            }

            foreach (var badge in set.Badges)
            {
                var width = Math.Max(1, (int)Math.Ceiling(badge.BoundsDip.Width * scale));
                var height = Math.Max(1, (int)Math.Ceiling(badge.BoundsDip.Height * scale));
                var key = $"badge|{set.SetId}|{badge.AppId}|{badge.IsOverflow}|{badge.Count}|{width}x{height}";
                var element = Find(old, key);
                if (element is null)
                {
                    element = CreateElement(
                        key,
                        ElementKind.Badge,
                        set.SetId,
                        default,
                        0,
                        0,
                        badge.BoundsDip,
                        width,
                        height,
                        BadgeRadiusDip,
                        badge.Icon,
                        false,
                        0,
                        set.HighContrast,
                        0,
                        0,
                        scale,
                        badge);
                }
                else
                {
                    UpdateElement(
                        element,
                        badge.BoundsDip,
                        badge.Icon,
                        false,
                        0,
                        set.HighContrast,
                        0,
                        0,
                        scale,
                        badge);
                }

                next.Add(element);
            }
        }

        foreach (var set in sets.Where(set => set.ListingOpen))
        {
            foreach (var row in set.ListingRows)
            {
                var width = Math.Max(1, (int)Math.Ceiling(row.BoundsDip.Width * scale));
                var height = Math.Max(1, (int)Math.Ceiling(row.BoundsDip.Height * scale));
                var key = $"row|{set.SetId}|{row.RowIndex}|{row.Window.Window.Value}|{width}x{height}|{row.Title}|{row.AppName}";
                var element = Find(old, key);
                if (element is null)
                {
                    element = CreateElement(
                        key,
                        ElementKind.ListingRow,
                        set.SetId,
                        row.Window,
                        0,
                        row.RowIndex,
                        row.BoundsDip,
                        width,
                        height,
                        ListingRadiusDip,
                        row.Icon,
                        false,
                        0,
                        set.HighContrast,
                        0,
                        0,
                        scale,
                        listing: row);
                }
                else
                {
                    UpdateElement(
                        element,
                        row.BoundsDip,
                        row.Icon,
                        false,
                        0,
                        set.HighContrast,
                        0,
                        0,
                        scale,
                        listing: row);
                }

                next.Add(element);
            }
        }

        foreach (var stale in old)
        {
            if (!next.Contains(stale))
            {
                Release(stale);
            }
        }

        _elements.Clear();
        _elements.AddRange(next);
        _root.RemoveAllVisuals();
        IDCompositionVisual? previous = null;
        foreach (var element in _elements)
        {
            if (previous is null)
            {
                _root.AddVisual(element.Visual, false, null);
            }
            else
            {
                _root.AddVisual(element.Visual, true, previous);
            }

            previous = element.Visual;
        }

        _rootEffect.SetOpacity(presentation switch
        {
            OverlayPresentation.Hidden => 0f,
            OverlayPresentation.Occluded => 0.85f,
            _ => 1f
        });
        ApplyTransforms();
    }

    private static ElementTarget? Find(IEnumerable<ElementTarget> elements, string key) =>
        elements.FirstOrDefault(element => element.Key == key);

    private ElementTarget CreateElement(
        string key,
        ElementKind kind,
        string setId,
        WindowKey window,
        int stackIndex,
        int rowIndex,
        RectD boundsDip,
        int width,
        int height,
        double radiusDip,
        IconBitmap? icon,
        bool isMinimized,
        nint sourceHwnd,
        bool highContrast,
        uint borderBgra,
        uint borderPixels,
        double scale,
        BadgeChipVisual? badge = null,
        ListingRowVisual? listing = null)
    {
        _device.DComp.CreateSurface(
            (uint)width,
            (uint)height,
            DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_PREMULTIPLIED,
            out var surface);
        _device.DComp.CreateVisual(out var visual);
        visual.SetContent(surface);

        _device.DComp.CreateRectangleClip(out var clip);
        clip.SetLeft(0);
        clip.SetTop(0);
        clip.SetRight(width);
        clip.SetBottom(height);
        var radius = (float)Math.Max(0, Math.Min(radiusDip * scale, Math.Min(width, height) / 2.0));
        clip.SetTopLeftRadiusX(radius);
        clip.SetTopLeftRadiusY(radius);
        clip.SetTopRightRadiusX(radius);
        clip.SetTopRightRadiusY(radius);
        clip.SetBottomLeftRadiusX(radius);
        clip.SetBottomLeftRadiusY(radius);
        clip.SetBottomRightRadiusX(radius);
        clip.SetBottomRightRadiusY(radius);
        visual.SetClip(clip);

        _device.DComp.CreateEffectGroup(out var effect);
        visual.SetEffect(effect);
        var element = new ElementTarget(
            key,
            kind,
            setId,
            window,
            stackIndex,
            rowIndex,
            visual,
            surface,
            clip,
            effect,
            (float)(boundsDip.X * scale),
            (float)(boundsDip.Y * scale),
            width,
            height,
            icon,
            isMinimized,
            sourceHwnd,
            highContrast,
            borderBgra,
            borderPixels,
            badge,
            listing);
        Paint(element, scale);
        return element;
    }

    private void UpdateElement(
        ElementTarget element,
        RectD boundsDip,
        IconBitmap? icon,
        bool isMinimized,
        nint sourceHwnd,
        bool highContrast,
        uint borderBgra,
        uint borderPixels,
        double scale,
        BadgeChipVisual? badge = null,
        ListingRowVisual? listing = null)
    {
        var repaint = element.Icon != icon
                      || element.BorderPixels != borderPixels
                      || element.BorderBgra != borderBgra
                      || element.Badge != badge
                      || element.Listing != listing;
        element.BaseX = (float)(boundsDip.X * scale);
        element.BaseY = (float)(boundsDip.Y * scale);
        element.Icon = icon;
        element.IsMinimized = isMinimized;
        element.SourceHwnd = sourceHwnd;
        element.HighContrast = highContrast;
        element.BorderBgra = borderBgra;
        element.BorderPixels = borderPixels;
        element.Badge = badge;
        element.Listing = listing;
        if (repaint)
        {
            Paint(element, scale);
        }
    }

    private void Paint(ElementTarget element, double scale)
    {
        switch (element.Kind)
        {
            case ElementKind.Card:
                TilePainter.Paint(
                    _device,
                    element.Surface,
                    element.Icon ?? IconBitmap.Solid(1, 1, 0xFF2A2A33),
                    (uint)element.Width,
                    (uint)element.Height,
                    element.BorderPixels,
                    element.BorderBgra,
                    coverage: 0.6f);
                break;
            case ElementKind.Badge:
                var badge = element.Badge!;
                var bitmap = badge.IsOverflow
                    ? IconBitmap.Chip(element.Width, $"+{badge.Count}")
                    : badge.Icon ?? IconBitmap.Solid(element.Width, element.Height, 0xFF3B82F6);
                TilePainter.Paint(
                    _device,
                    element.Surface,
                    bitmap,
                    (uint)element.Width,
                    (uint)element.Height,
                    0,
                    0,
                    coverage: 1f);
                break;
            case ElementKind.ListingRow:
                var row = element.Listing!;
                var rowBitmap = IconBitmap.ListingRow(
                    element.Width,
                    element.Height,
                    row.Icon,
                    row.Title,
                    row.AppName,
                    element.HighContrast);
                TilePainter.Paint(
                    _device,
                    element.Surface,
                    rowBitmap,
                    (uint)element.Width,
                    (uint)element.Height,
                    0,
                    0,
                    coverage: 1f);
                break;
        }
    }

    private void ApplyRootClip(double scale)
    {
        if (_rootClip is not null)
        {
            SetRootClip(null);
            Marshal.ReleaseComObject(_rootClip);
            _rootClip = null;
        }

        if (_listingOpen || _ghostSetId is not null || _clipDip.Width <= 0 || _clipDip.Height <= 0)
        {
            SetRootClip(null);
            return;
        }

        _device.DComp.CreateRectangleClip(out _rootClip);
        _rootClip.SetLeft((float)(_clipDip.X * scale));
        _rootClip.SetTop((float)(_clipDip.Y * scale));
        _rootClip.SetRight((float)((_clipDip.X + _clipDip.Width) * scale));
        _rootClip.SetBottom((float)((_clipDip.Y + _clipDip.Height) * scale));
        SetRootClip(_rootClip);
    }

    private void SetRootClip(IDCompositionClip? clip) => _root.SetClip(clip);

    public bool DrawLiveFrames(CaptureManager capture, long timestamp, long interval)
    {
        var updated = UpdateBadgeHover(interval);
        if (_presentation is OverlayPresentation.Hidden or OverlayPresentation.Occluded)
        {
            return updated;
        }

        foreach (var element in _elements)
        {
            if (element.Kind != ElementKind.Card
                || element.IsMinimized
                || element.SourceHwnd == 0
                || element.StackIndex > 2)
            {
                continue;
            }

            var cardInterval = element.StackIndex == 0 ? interval : interval * 6;
            if (timestamp - element.LastFrame < cardInterval)
            {
                continue;
            }

            var frame = capture.TryGetFrame(element.Window.Window);
            if (frame is null)
            {
                continue;
            }

            try
            {
                var description = frame.Surface.Description;
                if (description.Format != DirectXPixelFormat.B8G8R8A8UIntNormalized)
                {
                    capture.ReportFailure(element.Window.Window);
                    continue;
                }

                var content = frame.ContentSize;
                var contentWidth = content.Width > 0 ? content.Width : description.Width;
                var contentHeight = content.Height > 0 ? content.Height : description.Height;
                var uvScale = new Vector2(
                    description.Width > 0 ? (float)contentWidth / description.Width : 1f,
                    description.Height > 0 ? (float)contentHeight / description.Height : 1f);

                var texture = GraphicsCaptureInterop.GetTexture(frame.Surface);
                try
                {
                    TilePainter.PaintFrame(
                        _device,
                        element.Surface,
                        texture,
                        (uint)element.Width,
                        (uint)element.Height,
                        uvScale,
                        element.BorderPixels,
                        element.BorderBgra);
                }
                finally
                {
                    Marshal.ReleaseComObject(texture);
                }

                element.LastFrame = timestamp;
                if (contentWidth != element.ContentWidth || contentHeight != element.ContentHeight)
                {
                    element.ContentWidth = contentWidth;
                    element.ContentHeight = contentHeight;
                    capture.RecreatePool(element.Window.Window, new SizeInt32
                    {
                        Width = contentWidth,
                        Height = contentHeight
                    });
                }

                updated = true;
            }
            catch (Exception exception) when (!DeviceLoss.IsDeviceLost(exception))
            {
                Debug.WriteLine(exception);
                FileLogger.Default.Write(
                    "preview",
                    $"window={element.Window.Window.Value} hr=0x{exception.HResult:X8} {exception.GetType().Name}");
                capture.ReportFailure(element.Window.Window);
            }
            finally
            {
                frame.Dispose();
            }
        }

        return updated;
    }

    private bool UpdateBadgeHover(long interval)
    {
        var target = _badgeHoverSetId is null ? 0f : 1f;
        if (Math.Abs(_badgeHoverProgress - target) < 0.001f)
        {
            return false;
        }

        var elapsed = (float)(interval * 1000.0 / Stopwatch.Frequency);
        var step = elapsed / 120f;
        _badgeHoverProgress = Math.Abs(target - _badgeHoverProgress) <= step
            ? target
            : _badgeHoverProgress + Math.Sign(target - _badgeHoverProgress) * step;
        ApplyTransforms();
        return true;
    }

    public void SetHover(HitTarget? target)
    {
        _hover = target;
        var nextBadge = target is { Kind: HitKind.Badge } ? target.Value.SetId : null;
        if (nextBadge != _badgeHoverSetId)
        {
            _badgeHoverProgress = 0f;
        }

        _badgeHoverSetId = nextBadge;
        ApplyTransforms();
    }

    public void SetGhost(string? setId, double deltaX, double deltaY)
    {
        _ghostSetId = setId;
        _ghostDeltaX = (float)(deltaX * _dpi / 96.0);
        _ghostDeltaY = (float)(deltaY * _dpi / 96.0);
        ApplyRootClip(_dpi / 96.0);
        ApplyTransforms();
    }

    private unsafe void ApplyTransforms()
    {
        foreach (var element in _elements)
        {
            var ghosted = _ghostSetId is not null && element.SetId == _ghostSetId;
            element.Visual.SetOffsetX(element.BaseX + (ghosted ? _ghostDeltaX : 0f));
            element.Visual.SetOffsetY(element.BaseY + (ghosted ? _ghostDeltaY : 0f));

            var matrix = Matrix3x2.Identity;
            var center = new Vector2(element.Width / 2f, element.Height / 2f);
            if (element.Kind == ElementKind.Card
                && _hover is { Kind: HitKind.Card } hoveredCard
                && hoveredCard.SetId == element.SetId
                && hoveredCard.Window == element.Window)
            {
                matrix = Matrix3x2.CreateScale(1.03f, center);
            }
            else if (element.Kind == ElementKind.Badge
                     && _badgeHoverSetId == element.SetId)
            {
                var rest = (float)(BadgeRestDip / BadgeSurfaceDip);
                matrix = Matrix3x2.CreateScale(
                    rest + (1f - rest) * _badgeHoverProgress,
                    center);
            }

            element.Visual.SetTransform((D2D_MATRIX_3X2_F*)&matrix);
            element.Effect.SetOpacity(
                _ghostSetId is not null && !ghosted ? InactiveDim : 1f);
        }
    }

    public void Dispose()
    {
        foreach (var element in _elements)
        {
            Release(element);
        }

        _elements.Clear();
        SetRootClip(null);
        if (_rootClip is not null)
        {
            Marshal.ReleaseComObject(_rootClip);
            _rootClip = null;
        }

        Marshal.ReleaseComObject(_rootEffect);
        Marshal.ReleaseComObject(_root);
        Marshal.ReleaseComObject(_target);
    }

    private void Release(ElementTarget element)
    {
        _root.RemoveVisual(element.Visual);
        Marshal.ReleaseComObject(element.Effect);
        Marshal.ReleaseComObject(element.Clip);
        Marshal.ReleaseComObject(element.Surface);
        Marshal.ReleaseComObject(element.Visual);
    }

    private enum ElementKind
    {
        Card,
        Badge,
        ListingRow
    }

    private sealed class ElementTarget
    {
        public ElementTarget(
            string key,
            ElementKind kind,
            string setId,
            WindowKey window,
            int stackIndex,
            int rowIndex,
            IDCompositionVisual visual,
            IDCompositionSurface surface,
            IDCompositionRectangleClip clip,
            IDCompositionEffectGroup effect,
            float baseX,
            float baseY,
            int width,
            int height,
            IconBitmap? icon,
            bool isMinimized,
            nint sourceHwnd,
            bool highContrast,
            uint borderBgra,
            uint borderPixels,
            BadgeChipVisual? badge,
            ListingRowVisual? listing)
        {
            Key = key;
            Kind = kind;
            SetId = setId;
            Window = window;
            StackIndex = stackIndex;
            RowIndex = rowIndex;
            Visual = visual;
            Surface = surface;
            Clip = clip;
            Effect = effect;
            BaseX = baseX;
            BaseY = baseY;
            Width = width;
            Height = height;
            Icon = icon;
            IsMinimized = isMinimized;
            SourceHwnd = sourceHwnd;
            HighContrast = highContrast;
            BorderBgra = borderBgra;
            BorderPixels = borderPixels;
            Badge = badge;
            Listing = listing;
        }

        public string Key { get; }

        public ElementKind Kind { get; }

        public string SetId { get; }

        public WindowKey Window { get; }

        public int StackIndex { get; }

        public int RowIndex { get; }

        public IDCompositionVisual Visual { get; }

        public IDCompositionSurface Surface { get; }

        public IDCompositionRectangleClip Clip { get; }

        public IDCompositionEffectGroup Effect { get; }

        public float BaseX { get; set; }

        public float BaseY { get; set; }

        public int Width { get; }

        public int Height { get; }

        public IconBitmap? Icon { get; set; }

        public bool IsMinimized { get; set; }

        public nint SourceHwnd { get; set; }

        public bool HighContrast { get; set; }

        public uint BorderBgra { get; set; }

        public uint BorderPixels { get; set; }

        public BadgeChipVisual? Badge { get; set; }

        public ListingRowVisual? Listing { get; set; }

        public long LastFrame { get; set; }

        public int ContentWidth { get; set; }

        public int ContentHeight { get; set; }
    }
}
