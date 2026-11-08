using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using StageManager4win.Capture;
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
            var borderPixels = tile.HighContrast
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
                existing.Visual.SetOffsetX((float)(tile.BoundsDip.X * scale));
                existing.Visual.SetOffsetY((float)(tile.BoundsDip.Y * scale));
                existing.IsMinimized = tile.IsMinimized;
                if (existing.BorderPixels != borderPixels || existing.BorderBgra != tile.BorderBgra)
                {
                    existing.BorderPixels = borderPixels;
                    existing.BorderBgra = tile.BorderBgra;
                    if (tile.Icon is { } existingIcon)
                    {
                        TilePainter.Paint(
                            _device,
                            existing.Surface,
                            existingIcon,
                            (uint)existing.Width,
                            (uint)existing.Height,
                            borderPixels,
                            tile.BorderBgra);
                    }
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
                    tile.BorderBgra);
            }

            visual.SetContent(surface);
            // DirectComposition coordinates are physical pixels, not DIPs.
            visual.SetOffsetX((float)(tile.BoundsDip.X * scale));
            visual.SetOffsetY((float)(tile.BoundsDip.Y * scale));

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
        _rootEffect.SetOpacity(visible ? 1f : 0f);
        ApplyHover();
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
                        tile.BorderBgra);
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
        ApplyHover();
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

    private unsafe void ApplyHover()
    {
        foreach (var tile in _tiles)
        {
            var hovered = _hover == tile.Window;
            var baseOpacity = tile.IsMinimized ? 0.5f : 1f;
            tile.Effect.SetOpacity(_hover is null || hovered ? baseOpacity : baseOpacity * 0.92f);
            if (!hovered)
            {
                tile.Visual.SetTransform((IDCompositionTransform?)null);
                continue;
            }

            var center = new Vector2(tile.Width / 2f, tile.Height / 2f);
            var matrix = Matrix3x2.CreateScale(1.05f, center);
            tile.Visual.SetTransform((D2D_MATRIX_3X2_F*)&matrix);
        }
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
