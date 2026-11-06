using System.Diagnostics;
using StageManager4win.Core.Settings;
using StageManager4win.Input;
using StageManager4win.UI;

namespace StageManager4win;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _store;
    private readonly HotkeyManager _hotkeys;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private AppSettings _settings;

    public TrayApplicationContext(SettingsStore store)
    {
        _store = store;
        _settings = store.Load();

        _hotkeys = new HotkeyManager();
        _hotkeys.ToggleRequested += () => Debug.WriteLine("Toggle");
        _hotkeys.NextRequested += () => Debug.WriteLine("Next");
        _hotkeys.PreviousRequested += () => Debug.WriteLine("Previous");
        _hotkeys.ShowDesktopRequested += () => Debug.WriteLine("ShowDesktop");

        _enabledItem = new ToolStripMenuItem("Enabled")
        {
            CheckOnClick = true,
            Checked = _settings.Enabled
        };
        _enabledItem.CheckedChanged += (_, _) => ApplyEnabled(_enabledItem.Checked);

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => ShowSettings();
        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _tray = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "StageManager4win",
            Visible = true,
            ContextMenuStrip = menu
        };

        if (!_hotkeys.Register(_settings.Hotkeys, out var error))
        {
            MessageBox.Show(error, "StageManager4win", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public event Action? EnabledChanged;

    private void ExitApplication()
    {
        _tray.Visible = false;
        Dispose();
        ExitThread();
    }

    private void ApplyEnabled(bool enabled)
    {
        _settings = _settings with { Enabled = enabled };
        _store.Save(_settings);
        EnabledChanged?.Invoke();
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        var next = form.Result.Normalize();

        if (!_hotkeys.Register(next.Hotkeys, out var error))
        {
            _hotkeys.Register(_settings.Hotkeys, out _);
            MessageBox.Show(error, "StageManager4win", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var enabledChanged = _settings.Enabled != next.Enabled;
        _settings = next;
        _store.Save(_settings);
        _enabledItem.Checked = _settings.Enabled;

        if (enabledChanged)
        {
            EnabledChanged?.Invoke();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _hotkeys.Dispose();
        }

        base.Dispose(disposing);
    }
}
