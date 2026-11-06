namespace StageManager4win.Core.Settings;

public sealed record HotkeySettings(
    HotkeyBinding Toggle,
    HotkeyBinding Next,
    HotkeyBinding Previous,
    HotkeyBinding ShowDesktop)
{
    public static HotkeySettings Default { get; } = new(
        new HotkeyBinding(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, 0x20),
        new HotkeyBinding(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, 0xBE),
        new HotkeyBinding(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, 0xBC),
        new HotkeyBinding(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, 0x44));
}
