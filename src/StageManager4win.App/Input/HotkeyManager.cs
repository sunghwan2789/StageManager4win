using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using StageManager4win.Core.Settings;

namespace StageManager4win.Input;

public sealed class HotkeyManager : IDisposable
{
    private const int WmHotkey = 0x0312;
    private readonly HotkeyWindow _window;
    private bool _registered;

    public HotkeyManager() => _window = new HotkeyWindow(this);

    public event Action? NextRequested;
    public event Action? PreviousRequested;
    public event Action? ShowDesktopRequested;

    public bool Register(HotkeySettings settings, out string? error)
    {
        UnregisterAll();

        var bindings = new (string Name, HotkeyBinding Binding)[]
        {
            ("next", settings.Next),
            ("previous", settings.Previous),
            ("show desktop", settings.ShowDesktop)
        };

        for (var index = 0; index < bindings.Length; index++)
        {
            var (name, binding) = bindings[index];
            var modifiers = (HOT_KEY_MODIFIERS)binding.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;

            if (!PInvoke.RegisterHotKey(new HWND(_window.Handle), index + 1, modifiers, binding.VirtualKey))
            {
                UnregisterAll();
                error = $"Could not register the {name} hotkey. Another application may be using it.";
                return false;
            }
        }

        _registered = true;
        error = null;
        return true;
    }

    public void UnregisterAll()
    {
        if (!_registered)
        {
            return;
        }

        for (var id = 1; id <= 3; id++)
        {
            PInvoke.UnregisterHotKey(new HWND(_window.Handle), id);
        }

        _registered = false;
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.Dispose();
    }

    private void Dispatch(int id)
    {
        switch (id)
        {
            case 1:
                NextRequested?.Invoke();
                break;
            case 2:
                PreviousRequested?.Invoke();
                break;
            case 3:
                ShowDesktopRequested?.Invoke();
                break;
        }
    }

    private sealed class HotkeyWindow(HotkeyManager owner) : Form
    {
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey)
            {
                owner.Dispatch(message.WParam.ToInt32());
                return;
            }

            base.WndProc(ref message);
        }
    }
}
