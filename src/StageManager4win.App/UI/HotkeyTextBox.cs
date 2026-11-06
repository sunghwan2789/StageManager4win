using StageManager4win.Core.Settings;

namespace StageManager4win.UI;

public sealed class HotkeyTextBox : TextBox
{
    public HotkeyTextBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            base.OnKeyDown(e);
            return;
        }

        var modifiers = 0u;
        if (e.Control)
        {
            modifiers |= HotkeyBinding.ModControl;
        }

        if (e.Alt)
        {
            modifiers |= HotkeyBinding.ModAlt;
        }

        if (e.Shift)
        {
            modifiers |= HotkeyBinding.ModShift;
        }

        var binding = new HotkeyBinding(modifiers, (uint)e.KeyValue);
        Tag = binding;
        Text = Format(binding);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    public static string Format(HotkeyBinding binding)
    {
        var parts = new List<string>();

        if ((binding.Modifiers & HotkeyBinding.ModControl) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((binding.Modifiers & HotkeyBinding.ModAlt) != 0)
        {
            parts.Add("Alt");
        }

        if ((binding.Modifiers & HotkeyBinding.ModShift) != 0)
        {
            parts.Add("Shift");
        }

        if ((binding.Modifiers & HotkeyBinding.ModWin) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(binding.VirtualKey));
        return string.Join("+", parts);
    }

    private static string KeyName(uint virtualKey) => virtualKey switch
    {
        0x20 => "Space",
        0xBC => ",",
        0xBE => ".",
        _ => ((Keys)virtualKey).ToString()
    };
}
