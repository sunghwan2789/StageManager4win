using StageManager4win.Core.Windows;

namespace StageManager4win.Capture;

public readonly record struct CaptureRequest(WindowId Window, nint Hwnd);
