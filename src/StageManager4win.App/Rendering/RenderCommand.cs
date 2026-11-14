using StageManager4win.Core.Layout;
using StageManager4win.Overlay;

namespace StageManager4win.Rendering;

internal abstract record RenderCommand
{
    internal sealed record CreateOverlay(string MonitorId, nint Hwnd, uint Dpi) : RenderCommand;

    internal sealed record UpdateOverlay(
        string MonitorId,
        IReadOnlyList<SetTileVisual> Sets,
        IReadOnlyList<TilePlacement> Placements,
        OverlayPresentation Presentation,
        RectD ClipDip) : RenderCommand;

    internal sealed record SetOverlayHover(string MonitorId, HitTarget? Target) : RenderCommand;

    internal sealed record UpdateDragGhost(
        string MonitorId,
        string? SetId,
        double DeltaX,
        double DeltaY) : RenderCommand;

    internal sealed record SetPreviewFps(int Fps) : RenderCommand;

    internal sealed record SetCapturePaused(bool Paused) : RenderCommand;

    internal sealed record RemoveOverlay(string MonitorId) : RenderCommand;

    internal sealed record StopRenderThread : RenderCommand;
}
