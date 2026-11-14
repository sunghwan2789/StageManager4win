namespace StageManager4win.Core.Settings;

public sealed record HotkeySettings(
    HotkeyBinding Next,
    HotkeyBinding Previous,
    HotkeyBinding ShowDesktop)
{
    public static HotkeySettings Default { get; } = new(
        new HotkeyBinding(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, 0xBE),
        new HotkeyBinding(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, 0xBC),
        new HotkeyBinding(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, 0x44));
}
