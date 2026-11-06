namespace StageManager4win.Core.Windows;

public readonly record struct WindowBounds(int Left, int Top, int Width, int Height)
{
    public double CenterX => Left + Width / 2.0;
    public double CenterY => Top + Height / 2.0;
}
