namespace StageManager4win.Core.Settings;

public sealed record HotkeyBinding(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 1;
    public const uint ModControl = 2;
    public const uint ModShift = 4;
    public const uint ModWin = 8;
}
