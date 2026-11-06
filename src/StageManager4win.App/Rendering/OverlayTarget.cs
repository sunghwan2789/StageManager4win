using System.Numerics;
using System.Runtime.InteropServices;
using StageManager4win.Core.Windows;
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
        ClearTiles();
        var scale = _dpi / 96.0;
        foreach (var tile in tiles)
        {
            var width = Math.Max(1, (int)Math.Ceiling(tile.BoundsDip.Width * scale));
            var height = Math.Max(1, (int)Math.Ceiling(tile.BoundsDip.Height * scale));

            _device.DComp.CreateVisual(out var visual);
            _device.DComp.CreateSurface(
                (uint)width,
                (uint)height,
                DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                DXGI_ALPHA_MODE.DXGI_ALPHA_MODE_IGNORE,
                out var surface);
            if (tile.Icon is { } icon)
            {
                TilePainter.Paint(_device, surface, icon, (uint)width, (uint)height);
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
            _tiles.Add(new TileTarget(tile.Window, (float)width, (float)height, visual, surface, clip, effect));
        }

        _rootEffect.SetOpacity(visible ? 1f : 0f);
        ApplyHover();
    }

    public void SetHover(WindowId? window)
    {
        _hover = window;
        ApplyHover();
    }

    public void Dispose()
    {
        ClearTiles();
        Marshal.ReleaseComObject(_rootEffect);
        Marshal.ReleaseComObject(_root);
        Marshal.ReleaseComObject(_target);
    }

    private unsafe void ApplyHover()
    {
        foreach (var tile in _tiles)
        {
            var hovered = _hover == tile.Window;
            tile.Effect.SetOpacity(_hover is null || hovered ? 1f : 0.92f);
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

    private void ClearTiles()
    {
        foreach (var tile in _tiles)
        {
            _root.RemoveVisual(tile.Visual);
            Marshal.ReleaseComObject(tile.Effect);
            Marshal.ReleaseComObject(tile.Clip);
            Marshal.ReleaseComObject(tile.Surface);
            Marshal.ReleaseComObject(tile.Visual);
        }

        _tiles.Clear();
    }

    private sealed record TileTarget(
        WindowId Window,
        float Width,
        float Height,
        IDCompositionVisual Visual,
        IDCompositionSurface Surface,
        IDCompositionRectangleClip Clip,
        IDCompositionEffectGroup Effect);
}
