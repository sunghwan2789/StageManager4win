using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Stages;

public readonly record struct WindowKey(WindowId Window, int ProcessId)
{
    public static WindowKey For(WindowSnapshot snapshot) =>
        new(snapshot.Id, snapshot.ProcessId);
}
