namespace StageManager4win.Core.Input;

public static class DesktopClick
{
    public const int MaxMoveDip = 4;
    public const int MaxMilliseconds = 500;

    public static bool IsToggle(double dx, double dy, long elapsedMilliseconds) =>
        dx * dx + dy * dy <= MaxMoveDip * MaxMoveDip
        && elapsedMilliseconds <= MaxMilliseconds;

    public static bool IsDesktopClass(string className) =>
        className is "Progman" or "WorkerW";
}
