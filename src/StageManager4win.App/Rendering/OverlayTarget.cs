using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using StageManager4win.Capture;
using StageManager4win.Core.Diagnostics;
using StageManager4win.Core.Layout;
using StageManager4win.Core.Windows;
using StageManager4win.Interop;
using Windows.Graphics;
using Windows.Graphics.DirectX;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct2D.Common;
using Windows.Win32.Graphics.DirectComposition;
using Windows.Win32.Graphics.Dxgi.Common;

namespace StageManager4win.Rendering;

internal sealed class OverlayTarget : IDisposable
{
    private const double BadgeWidthRatio = 0.3;
    private const double BadgeMinDip = 28;
    private const double BadgeMaxDip = 52;
    private const double BadgeInsetDip = 6;
    private const double BadgeRadiusDip = 8;
    private const float TileFarScale = 0.86f;
    private const float TileFarAlpha = 0.72f;

    private readonly RenderDevice _device;
    private readonly IDCompositionTarget _target;
    private readonly IDCompositionVisual _root;
    private readonly IDCompositionEffectGroup _rootEffect;
    private readonly List<TileTarget> _tiles = new();
    private readonly uint _dpi;
    private WindowId? _hover;
    private WindowId? _ghost;
    private float _ghostDeltaX;
    private float _ghostDeltaY;
    private IDCompositionRectangleClip? _rootClip;

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

    public void Update(IReadOnlyList<TileVisual> tiles, bool visible, RectD clipDip)
    {
        var scale = _dpi / 96.0;
        ApplyRootClip(clipDip, scale);
        var next = new List<TileTarget>(tiles.Count);
        foreach (var tile in tiles)
        {
            var card = TileStack.Card(tile.StackIndex);
            var front = tile.Group == default ? tile.Window : tile.Group;
            var hasBadge = tile.StackIndex == 0 && tile.Icon is { Width: > 1 };
            var baseOffsetX = (float)(tile.BoundsDip.X * scale);
            var baseOffsetY = (float)(tile.BoundsDip.Y * scale);
            var borderPixels = tile.HighContrast && tile.StackIndex == 0
                ? (uint)Math.Max(1, Math.Round(2 * scale))
                : 0;
            var width = Math.Max(1, (int)Math.Ceiling(tile.BoundsDip.Width * scale));
            var height = Math.Max(1, (int)Math.Ceiling(tile.BoundsDip.Height * scale));
            var tilt = TileStack.TiltSlope(
                tile.BoundsDip.Y + tile.BoundsDip.Height / 2,
                clipDip.Y + clipDip.Height / 2,
                clipDip.Height / 2,
                tile.BoundsDip.Width,
                tile.BoundsDip.Height);
            var existing = _tiles.Find(candidate =>
                candidate.Window == tile.Window
                && candidate.Width == width
                && candidate.Height == height
                && (candidate.Badge is not null) == hasBadge);
            if (existing is not null)
            {
                _tiles.Remove(existing);
                existing.Front = front;
                existing.CardOffsetX = card.OffsetX;
                existing.CardOffsetY = card.OffsetY;
                existing.CardScale = card.Scale;
                existing.BaseOffsetX = baseOffsetX;
                existing.BaseOffsetY = baseOffsetY;
                existing.Tilt = (float)tilt;
                existing.IsMinimized = tile.IsMinimized;
                var repaint = existing.Dim != card.Dim
                              || existing.BorderPixels != borderPixels
                              || existing.BorderBgra != tile.BorderBgra;
                existing.Dim = card.Dim;
                existing.BorderPixels = borderPixels;
                existing.BorderBgra = tile.BorderBgra;
                if (repaint && tile.Icon is { } existingIcon)
                {
                    TilePainter.Paint(
                        _device,
                        existing.Surface,
                        existingIcon,
                        (uint)existing.Width,
                        (uint)existing.Height,
                        borderPixels,
                        tile.BorderBgra,
                        card.Dim,
                        farScale: TileFarScale,
                        farAlpha: TileFarAlpha);
                }

                next.Add(existing);
                continue;
            }

            _device.DComp.CreateSurface(
                (uint)width,
                (uint)height,
                DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_PREMULTIPLIED,
                out var surface);
            if (tile.Icon is { } icon)
            {
                TilePainter.Paint(
                    _device,
                    surface,
                    icon,
                    (uint)width,
                    (uint)height,
                    borderPixels,
                    tile.BorderBgra,
                    card.Dim,
                    farScale: TileFarScale,
                    farAlpha: TileFarAlpha);
            }

            _device.DComp.CreateVisual(out var cardVisual);
            cardVisual.SetContent(surface);
            // DirectComposition coordinates are physical pixels, not DIPs.
            cardVisual.SetOffsetX(0);
            cardVisual.SetOffsetY(0);

            _device.DComp.CreateRectangleClip(out var clip);
            clip.SetLeft(0);
            clip.SetTop(0);
            clip.SetRight(width);
            clip.SetBottom(height);
            var radius = (float)Math.Max(
                0,
                Math.Min(tile.CornerRadiusDip * scale, Math.Min(width, height) / 2.0));
            clip.SetTopLeftRadiusX(radius);
            clip.SetTopLeftRadiusY(radius);
            clip.SetTopRightRadiusX(radius);
            clip.SetTopRightRadiusY(radius);
            clip.SetBottomLeftRadiusX(radius);
            clip.SetBottomLeftRadiusY(radius);
            clip.SetBottomRightRadiusX(radius);
            clip.SetBottomRightRadiusY(radius);
            cardVisual.SetClip(clip);

            _device.DComp.CreateVisual(out var visual);
            visual.AddVisual(cardVisual, true, null);
            _device.DComp.CreateEffectGroup(out var effect);
            visual.SetEffect(effect);
            var badge = hasBadge
                ? CreateBadge(tile.Icon!, tile.BoundsDip.Width, height, scale)
                : null;
            if (badge is not null)
            {
                // Add above the card explicitly; adding both with a null
                // reference does not guarantee the badge stays in front.
                visual.AddVisual(badge.Visual, true, cardVisual);
            }

            next.Add(new TileTarget(
                tile.Window,
                front,
                card,
                baseOffsetX,
                baseOffsetY,
                width,
                height,
                visual,
                cardVisual,
                surface,
                clip,
                effect,
                tile.IsMinimized,
                borderPixels,
                tile.BorderBgra)
            {
                Tilt = (float)tilt,
                Badge = badge
            });
        }

        foreach (var stale in _tiles)
        {
            Release(stale);
        }

        _tiles.Clear();
        _tiles.AddRange(next);
        // AddVisual fails if the visual is already a child, so rebuild the child list.
        // Each new child lands in front, so add front-to-back to keep each group's
        // front card on top.
        _root.RemoveAllVisuals();
        for (var index = next.Count - 1; index >= 0; index--)
        {
            _root.AddVisual(next[index].Visual, true, null);
        }

        _rootEffect.SetOpacity(visible ? 1f : 0f);
        ApplyTransforms();
    }

    private void SetRootClip(IDCompositionClip? clip) => _root.SetClip(clip);

    private void ApplyRootClip(RectD clipDip, double scale)
    {
        if (_rootClip is not null)
        {
            SetRootClip(null);
            Marshal.ReleaseComObject(_rootClip);
            _rootClip = null;
        }

        if (clipDip.Width <= 0 || clipDip.Height <= 0)
        {
            SetRootClip(null);
            return;
        }

        _device.DComp.CreateRectangleClip(out _rootClip);
        _rootClip.SetLeft((float)(clipDip.X * scale));
        _rootClip.SetTop((float)(clipDip.Y * scale));
        _rootClip.SetRight((float)((clipDip.X + clipDip.Width) * scale));
        _rootClip.SetBottom((float)((clipDip.Y + clipDip.Height) * scale));
        SetRootClip(_ghost is null ? _rootClip : null);
    }

    private BadgeVisual CreateBadge(IconBitmap icon, double widthDip, int tileHeight, double scale)
    {
        var sizeDip = Math.Clamp(widthDip * BadgeWidthRatio, BadgeMinDip, BadgeMaxDip);
        var size = Math.Max(1, (int)Math.Ceiling(sizeDip * scale));
        _device.DComp.CreateVisual(out var visual);
        _device.DComp.CreateSurface(
            (uint)size,
            (uint)size,
            DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
            DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_IGNORE,
            out var surface);
        TilePainter.Paint(
            _device,
            surface,
            icon,
            (uint)size,
            (uint)size,
            0,
            0,
            coverage: 1f);
        visual.SetContent(surface);
        var inset = (int)Math.Round(BadgeInsetDip * scale);
        visual.SetOffsetX(inset);
        visual.SetOffsetY(Math.Max(0, tileHeight - size - inset));

        _device.DComp.CreateRectangleClip(out var clip);
        clip.SetLeft(0);
        clip.SetTop(0);
        clip.SetRight(size);
        clip.SetBottom(size);
        var radius = (float)Math.Min(BadgeRadiusDip * scale, size / 2.0);
        clip.SetTopLeftRadiusX(radius);
        clip.SetTopLeftRadiusY(radius);
        clip.SetTopRightRadiusX(radius);
        clip.SetTopRightRadiusY(radius);
        clip.SetBottomLeftRadiusX(radius);
        clip.SetBottomLeftRadiusY(radius);
        clip.SetBottomRightRadiusX(radius);
        clip.SetBottomRightRadiusY(radius);
        visual.SetClip(clip);
        return new BadgeVisual(visual, surface, clip);
    }

    public bool DrawLiveFrames(CaptureManager capture, long timestamp, long interval)
    {
        var updated = false;
        foreach (var tile in _tiles)
        {
            // Minimized windows keep their last frame at half opacity.
            if (tile.IsMinimized || timestamp - tile.LastFrame < interval)
            {
                continue;
            }

            var frame = capture.TryGetFrame(tile.Window);
            if (frame is null)
            {
                continue;
            }

            try
            {
                var description = frame.Surface.Description;
                if (description.Format != DirectXPixelFormat.B8G8R8A8UIntNormalized)
                {
                    capture.ReportFailure(tile.Window);
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
                        tile.Surface,
                        texture,
                        (uint)tile.Width,
                        (uint)tile.Height,
                        uvScale,
                        tile.BorderPixels,
                        tile.BorderBgra,
                        tile.Dim,
                        farScale: TileFarScale,
                        farAlpha: TileFarAlpha);
                }
                finally
                {
                    Marshal.ReleaseComObject(texture);
                }

                tile.LastFrame = timestamp;
                ApplyHover();
                if (contentWidth != tile.ContentWidth || contentHeight != tile.ContentHeight)
                {
                    tile.ContentWidth = contentWidth;
                    tile.ContentHeight = contentHeight;
                    capture.RecreatePool(tile.Window, new SizeInt32
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
                    $"window={tile.Window.Value} hr=0x{exception.HResult:X8} {exception.GetType().Name}");
                capture.ReportFailure(tile.Window);
            }
            finally
            {
                frame.Dispose();
            }
        }

        return updated;
    }

    public void SetHover(WindowId? window)
    {
        _hover = window;
        ApplyTransforms();
    }

    public void SetGhost(WindowId? front, double deltaX, double deltaY)
    {
        _ghost = front;
        _ghostDeltaX = (float)(deltaX * _dpi / 96.0);
        _ghostDeltaY = (float)(deltaY * _dpi / 96.0);
        SetRootClip(_ghost is null ? _rootClip : null);
        ApplyTransforms();
    }

    public void Dispose()
    {
        foreach (var tile in _tiles)
        {
            Release(tile);
        }

        _tiles.Clear();
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

    private void ApplyTransforms()
    {
        foreach (var tile in _tiles)
        {
            var ghosted = _ghost is { } front && tile.Front == front;
            tile.Visual.SetOffsetX(tile.BaseOffsetX + (ghosted ? _ghostDeltaX : 0f));
            tile.Visual.SetOffsetY(tile.BaseOffsetY + (ghosted ? _ghostDeltaY : 0f));
        }

        ApplyHover();
    }

    private unsafe void ApplyHover()
    {
        var scale = _dpi / 96.0;
        foreach (var tile in _tiles)
        {
            var hovered = _ghost is null && _hover == tile.Window;
            var active = _ghost is not null || _hover is null || hovered;
            tile.Effect.SetOpacity(active ? 1f : 0.92f);

            var center = new Vector2(tile.Width / 2f, tile.Height / 2f);
            var matrix = Matrix3x2.CreateScale((float)tile.CardScale, center)
                         * Matrix3x2.CreateTranslation(
                             (float)(tile.CardOffsetX * scale),
                             (float)(tile.CardOffsetY * scale));
            if (hovered)
            {
                matrix = Matrix3x2.CreateScale(1.05f, center) * matrix;
            }

            tile.Visual.SetTransform((D2D_MATRIX_3X2_F*)&matrix);

            // Lean the far edge toward the middle; the badge stays upright
            // because only the card visual is sheared.
            var shear = new Matrix3x2(1f, tile.Tilt, 0f, 1f, 0f, 0f);
            tile.Card.SetTransform((D2D_MATRIX_3X2_F*)&shear);
        }
    }

    private void Release(TileTarget tile)
    {
        if (tile.Badge is { } badge)
        {
            tile.Visual.RemoveVisual(badge.Visual);
            Marshal.ReleaseComObject(badge.Clip);
            Marshal.ReleaseComObject(badge.Surface);
            Marshal.ReleaseComObject(badge.Visual);
        }

        tile.Visual.RemoveVisual(tile.Card);
        _root.RemoveVisual(tile.Visual);
        Marshal.ReleaseComObject(tile.Effect);
        Marshal.ReleaseComObject(tile.Clip);
        Marshal.ReleaseComObject(tile.Surface);
        Marshal.ReleaseComObject(tile.Card);
        Marshal.ReleaseComObject(tile.Visual);
    }

    private sealed class TileTarget
    {
        public TileTarget(
            WindowId window,
            WindowId front,
            StackCard card,
            float baseOffsetX,
            float baseOffsetY,
            float width,
            float height,
            IDCompositionVisual visual,
            IDCompositionVisual cardVisual,
            IDCompositionSurface surface,
            IDCompositionRectangleClip clip,
            IDCompositionEffectGroup effect,
            bool isMinimized,
            uint borderPixels,
            uint borderBgra)
        {
            Window = window;
            Front = front;
            CardOffsetX = card.OffsetX;
            CardOffsetY = card.OffsetY;
            CardScale = card.Scale;
            Dim = card.Dim;
            BaseOffsetX = baseOffsetX;
            BaseOffsetY = baseOffsetY;
            Width = width;
            Height = height;
            Visual = visual;
            Card = cardVisual;
            Surface = surface;
            Clip = clip;
            Effect = effect;
            IsMinimized = isMinimized;
            BorderPixels = borderPixels;
            BorderBgra = borderBgra;
        }

        public WindowId Window { get; }

        public WindowId Front { get; set; }

        public double CardOffsetX { get; set; }

        public double CardOffsetY { get; set; }

        public double CardScale { get; set; }

        public float Dim { get; set; }

        public float Tilt { get; set; }

        public BadgeVisual? Badge { get; set; }

        public float BaseOffsetX { get; set; }

        public float BaseOffsetY { get; set; }

        public float Width { get; }

        public float Height { get; }

        public IDCompositionVisual Visual { get; }

        public IDCompositionVisual Card { get; }

        public IDCompositionSurface Surface { get; }

        public IDCompositionRectangleClip Clip { get; }

        public IDCompositionEffectGroup Effect { get; }

        public bool IsMinimized { get; set; }

        public uint BorderPixels { get; set; }

        public uint BorderBgra { get; set; }

        public long LastFrame { get; set; }

        public int ContentWidth { get; set; }

        public int ContentHeight { get; set; }
    }

    private sealed record BadgeVisual(
        IDCompositionVisual Visual,
        IDCompositionSurface Surface,
        IDCompositionRectangleClip Clip);
}
