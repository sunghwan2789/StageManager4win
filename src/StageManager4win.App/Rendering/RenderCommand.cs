using StageManager4win.Core.Windows;

namespace StageManager4win.Rendering;

internal abstract record RenderCommand
{
    internal sealed record CreateOverlay(string MonitorId, nint Hwnd, uint Dpi) : RenderCommand;

    internal sealed record UpdateOverlay(
        string MonitorId,
        IReadOnlyList<TileVisual> Tiles,
        bool Visible) : RenderCommand;

    internal sealed record SetOverlayHover(string MonitorId, WindowId? Window) : RenderCommand;

    internal sealed record SetPreviewFps(int Fps) : RenderCommand;

    internal sealed record RemoveOverlay(string MonitorId) : RenderCommand;

    internal sealed record StopRenderThread : RenderCommand;
}
