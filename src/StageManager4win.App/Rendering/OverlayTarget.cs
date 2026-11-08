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

    public void Update(IReadOnlyList<TileVisual> tiles, bool visible)
    {
        var scale = _dpi / 96.0;
        var next = new List<TileTarget>(tiles.Count);
        foreach (var tile in tiles)
        {
            var card = TileStack.Card(tile.StackIndex);
            var front = tile.Group == default ? tile.Window : tile.Group;
            var baseOffsetX = (float)(tile.BoundsDip.X * scale);
            var baseOffsetY = (float)(tile.BoundsDip.Y * scale);
            var borderPixels = tile.HighContrast && tile.StackIndex == 0
                ? (uint)Math.Max(1, Math.Round(2 * scale))
                : 0;
            var width = Math.Max(1, (int)Math.Ceiling(tile.BoundsDip.Width * scale));
            var height = Math.Max(1, (int)Math.Ceiling(tile.BoundsDip.Height * scale));
            var existing = _tiles.Find(candidate =>
                candidate.Window == tile.Window
                && candidate.Width == width
                && candidate.Height == height);
            if (existing is not null)
            {
                _tiles.Remove(existing);
                existing.Front = front;
                existing.CardOffsetY = card.OffsetY;
                existing.CardScale = card.Scale;
                existing.CardRotation = card.RotationDegrees;
                existing.BaseOffsetX = baseOffsetX;
                existing.BaseOffsetY = baseOffsetY;
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
                        card.Dim);
                }

                next.Add(existing);
                continue;
            }

            _device.DComp.CreateVisual(out var visual);
            _device.DComp.CreateSurface(
                (uint)width,
                (uint)height,
                DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_IGNORE,
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
                    card.Dim);
            }

            visual.SetContent(surface);
            // DirectComposition coordinates are physical pixels, not DIPs.
            visual.SetOffsetX(baseOffsetX);
            visual.SetOffsetY(baseOffsetY);

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
            visual.SetClip(clip);

            _device.DComp.CreateEffectGroup(out var effect);
            visual.SetEffect(effect);
            _root.AddVisual(visual, true, null);
            next.Add(new TileTarget(
                tile.Window,
                front,
                card,
                baseOffsetX,
                baseOffsetY,
                width,
                height,
                visual,
                surface,
                clip,
                effect,
                tile.IsMinimized,
                borderPixels,
                tile.BorderBgra));
        }

        foreach (var stale in _tiles)
        {
            Release(stale);
        }

        _tiles.Clear();
        _tiles.AddRange(next);
        foreach (var tile in next)
        {
            _root.AddVisual(tile.Visual, true, null);
        }

        _rootEffect.SetOpacity(visible ? 1f : 0f);
        ApplyTransforms();
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
                        tile.Dim);
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
        ApplyTransforms();
    }

    public void Dispose()
    {
        foreach (var tile in _tiles)
        {
            Release(tile);
        }

        _tiles.Clear();
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
        foreach (var tile in _tiles)
        {
            var hovered = _ghost is null && _hover == tile.Window;
            var active = _ghost is not null || _hover is null || hovered;
            var baseOpacity = tile.IsMinimized ? 0.5f : 1f;
            tile.Effect.SetOpacity(active ? baseOpacity : baseOpacity * 0.92f);

            var matrix = CardTransform(tile, _dpi / 96.0);
            if (hovered)
            {
                var center = new Vector2(tile.Width / 2f, tile.Height / 2f);
                matrix = Matrix3x2.CreateScale(1.05f, center) * matrix;
            }

            tile.Visual.SetTransform((D2D_MATRIX_3X2_F*)&matrix);
        }
    }

    private static Matrix3x2 CardTransform(TileTarget tile, double dpiScale)
    {
        var center = new Vector2(tile.Width / 2f, tile.Height / 2f);
        var radians = (float)(tile.CardRotation * Math.PI / 180.0);
        return Matrix3x2.CreateScale((float)tile.CardScale, center)
               * Matrix3x2.CreateRotation(radians, center)
               * Matrix3x2.CreateTranslation(0, (float)(tile.CardOffsetY * dpiScale));
    }

    private void Release(TileTarget tile)
    {
        _root.RemoveVisual(tile.Visual);
        Marshal.ReleaseComObject(tile.Effect);
        Marshal.ReleaseComObject(tile.Clip);
        Marshal.ReleaseComObject(tile.Surface);
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
            IDCompositionSurface surface,
            IDCompositionRectangleClip clip,
            IDCompositionEffectGroup effect,
            bool isMinimized,
            uint borderPixels,
            uint borderBgra)
        {
            Window = window;
            Front = front;
            CardOffsetY = card.OffsetY;
            CardScale = card.Scale;
            CardRotation = card.RotationDegrees;
            Dim = card.Dim;
            BaseOffsetX = baseOffsetX;
            BaseOffsetY = baseOffsetY;
            Width = width;
            Height = height;
            Visual = visual;
            Surface = surface;
            Clip = clip;
            Effect = effect;
            IsMinimized = isMinimized;
            BorderPixels = borderPixels;
            BorderBgra = borderBgra;
        }

        public WindowId Window { get; }

        public WindowId Front { get; set; }

        public double CardOffsetY { get; set; }

        public double CardScale { get; set; }

        public double CardRotation { get; set; }

        public float Dim { get; set; }

        public float BaseOffsetX { get; set; }

        public float BaseOffsetY { get; set; }

        public float Width { get; }

        public float Height { get; }

        public IDCompositionVisual Visual { get; }

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
}
