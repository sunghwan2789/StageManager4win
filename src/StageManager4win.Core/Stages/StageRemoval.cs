namespace StageManager4win.Core.Stages;

public static class StageRemoval
{
    public static bool ShouldRemove(
        bool wasOnStage,
        bool sameMonitor,
        int startWidth,
        int startHeight,
        int endWidth,
        int endHeight,
        bool cursorOnTile,
        int tolerancePixels = 2) =>
        wasOnStage
        && sameMonitor
        && cursorOnTile
        && Math.Abs(endWidth - startWidth) <= tolerancePixels
        && Math.Abs(endHeight - startHeight) <= tolerancePixels;
}
