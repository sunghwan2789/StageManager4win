using StageManager4win.Core.Settings;
using StageManager4win.Input;
using StageManager4win.UI;

namespace StageManager4win;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _store;
    private readonly HotkeyManager _hotkeys;
    private readonly StageCoordinator _coordinator;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private AppSettings _settings;
    private bool _syncingEnabled;
    private bool _errorShown;

    public TrayApplicationContext(SettingsStore store)
    {
        _store = store;
        _settings = store.Load();

        _coordinator = new StageCoordinator(store);
        _hotkeys = new HotkeyManager();
        _hotkeys.ToggleRequested += () => _coordinator.ToggleStripUnderPointer();
        _hotkeys.NextRequested += () => _coordinator.Cycle(1);
        _hotkeys.PreviousRequested += () => _coordinator.Cycle(-1);
        _hotkeys.ShowDesktopRequested += () => _coordinator.ShowDesktop();

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

        _coordinator.StatusMessage += message =>
            _tray.ShowBalloonTip(4000, "StageManager4win", message, ToolTipIcon.Warning);
        _coordinator.Start();

        if (!_hotkeys.Register(_settings.Hotkeys, out var error))
        {
            MessageBox.Show(error, "StageManager4win", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public event Action? EnabledChanged;

    public void NotifyErrorOnce(string message)
    {
        if (_errorShown)
        {
            return;
        }

        _errorShown = true;
        _tray.ShowBalloonTip(4000, "StageManager4win", message, ToolTipIcon.Error);
    }

    private void ExitApplication()
    {
        _tray.Visible = false;
        Dispose();
        ExitThread();
    }

    private void ApplyEnabled(bool enabled)
    {
        if (_syncingEnabled)
        {
            return;
        }

        ApplySettings(_settings with { Enabled = enabled });
    }

    private void ApplySettings(AppSettings settings)
    {
        var next = settings.Normalize();
        if (!_hotkeys.Register(next.Hotkeys, out var error))
        {
            _hotkeys.Register(_settings.Hotkeys, out _);
            MessageBox.Show(error, "StageManager4win", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var enabledChanged = _settings.Enabled != next.Enabled;
        _settings = next;
        _store.Save(_settings);
        _syncingEnabled = true;
        _enabledItem.Checked = _settings.Enabled;
        _syncingEnabled = false;
        _coordinator.ApplySettings(_settings);

        if (enabledChanged)
        {
            EnabledChanged?.Invoke();
        }
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        ApplySettings(form.Result);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _coordinator.Dispose();
            _hotkeys.Dispose();
            _tray.Dispose();
        }

        base.Dispose(disposing);
    }
}
