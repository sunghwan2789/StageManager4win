using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Stages;

public readonly record struct StripGroup(WindowId Front, IReadOnlyList<WindowId> Windows);
