using StageManager4win.Core.Layout;
using StageManager4win.Core.Settings;
using StageManager4win.Core.Stages;

namespace StageManager4win.UI;

public sealed class SettingsForm : Form
{
    public SettingsForm(AppSettings settings, IReadOnlyList<MonitorDescriptor> monitors)
    {
        Result = settings;

        Text = "StageManager4win Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(12);

        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var monitorRows = new List<MonitorRow>();
        var monitorsPanel = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill
        };
        for (var column = 0; column < 3; column++)
        {
            monitorsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }

        monitorsPanel.Controls.Add(new Label { Text = "Monitor", AutoSize = true }, 0, 0);
        monitorsPanel.Controls.Add(new Label { Text = "Managed", AutoSize = true }, 1, 0);
        monitorsPanel.Controls.Add(new Label { Text = "Strip edge", AutoSize = true }, 2, 0);
        for (var index = 0; index < monitors.Count; index++)
        {
            var monitor = monitors[index];
            var existing = settings.ForMonitor(monitor.Id);
            var managed = new CheckBox { Checked = existing?.Managed ?? true, AutoSize = true };
            var monitorEdge = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 110
            };
            monitorEdge.Items.AddRange(new object[] { "Use global", "Left", "Right" });
            monitorEdge.SelectedIndex = existing?.StripEdge switch
            {
                StripEdge.Left => 1,
                StripEdge.Right => 2,
                _ => 0
            };
            var line = index + 1;
            monitorsPanel.Controls.Add(
                new Label
                {
                    Text = MonitorLabel(monitor),
                    AutoSize = true,
                    Anchor = AnchorStyles.Left
                },
                0,
                line);
            monitorsPanel.Controls.Add(managed, 1, line);
            monitorsPanel.Controls.Add(monitorEdge, 2, line);
            monitorRows.Add(new MonitorRow(monitor.Id, managed, monitorEdge));
        }

        var enabled = new CheckBox { Text = "Enabled", Checked = settings.Enabled, AutoSize = true };
        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        mode.Items.AddRange(new object[] { "One at a Time", "All at Once", "Automatic" });
        mode.SelectedIndex = (int)settings.WindowMode;
        var edge = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        edge.Items.AddRange(new object[] { "Left", "Right" });
        edge.SelectedIndex = (int)settings.StripEdge;
        var fps = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        fps.Items.AddRange(new object[] { 15, 30, 60 });
        fps.SelectedItem = settings.PreviewFps;
        if (fps.SelectedIndex < 0)
        {
            fps.SelectedItem = 30;
        }

        var excluded = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Width = 320,
            Height = 80,
            Text = string.Join(Environment.NewLine, settings.ExcludedAppIds)
        };

        var next = NewHotkeyBox(settings.Hotkeys.Next);
        var previous = NewHotkeyBox(settings.Hotkeys.Previous);
        var showDesktop = NewHotkeyBox(settings.Hotkeys.ShowDesktop);

        var row = 0;
        var monitorHeader = new Label { Text = "Monitors", AutoSize = true };
        layout.Controls.Add(monitorHeader, 0, row);
        layout.SetColumnSpan(monitorHeader, 2);
        row++;
        layout.Controls.Add(monitorsPanel, 0, row);
        layout.SetColumnSpan(monitorsPanel, 2);
        row++;
        AddRow(layout, "Enabled", enabled, row++);
        AddRow(layout, "Window mode", mode, row++);
        AddRow(layout, "Strip edge", edge, row++);
        AddRow(layout, "Preview FPS", fps, row++);
        AddRow(layout, "Excluded app IDs (one per line)", excluded, row++);
        AddRow(layout, "Next hotkey", next, row++);
        AddRow(layout, "Previous hotkey", previous, row++);
        AddRow(layout, "Show desktop hotkey", showDesktop, row++);

        var ok = new Button { Text = "OK", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 0, row);
        layout.SetColumnSpan(buttons, 2);

        ok.Click += (_, _) =>
        {
            Result = BuildResult(settings, monitorRows, enabled, mode, edge, fps,
                excluded, next, previous, showDesktop);
            DialogResult = DialogResult.OK;
        };

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(layout);
    }

    public AppSettings Result { get; private set; }

    private static HotkeyTextBox NewHotkeyBox(HotkeyBinding binding) => new()
    {
        Width = 200,
        Tag = binding,
        Text = HotkeyTextBox.Format(binding)
    };

    private static void AddRow(TableLayoutPanel layout, string label, Control control, int row)
    {
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private static AppSettings BuildResult(
        AppSettings original,
        IReadOnlyList<MonitorRow> monitorRows,
        CheckBox enabled,
        ComboBox mode,
        ComboBox edge,
        ComboBox fps,
        TextBox excluded,
        HotkeyTextBox next,
        HotkeyTextBox previous,
        HotkeyTextBox showDesktop) => original with
        {
            Enabled = enabled.Checked,
            WindowMode = (WindowMode)mode.SelectedIndex,
            StripEdge = (StripEdge)edge.SelectedIndex,
            PreviewFps = (int)(fps.SelectedItem ?? 30),
            Hotkeys = new HotkeySettings(
                BindingOf(next, original.Hotkeys.Next),
                BindingOf(previous, original.Hotkeys.Previous),
                BindingOf(showDesktop, original.Hotkeys.ShowDesktop)),
            ExcludedAppIds = excluded.Lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToArray(),
            Monitors = MergeMonitors(original, monitorRows)
        };

    private static string MonitorLabel(MonitorDescriptor monitor)
    {
        var number = new string(monitor.GdiDeviceName.Where(char.IsDigit).ToArray());
        var name = monitor.Name;
        if (string.IsNullOrWhiteSpace(name)
            || name.StartsWith("Generic", StringComparison.OrdinalIgnoreCase))
        {
            name = int.TryParse(number, out var display) ? $"Display {display}" : "Display";
        }

        var label = $"{name} — {monitor.Bounds.Width}×{monitor.Bounds.Height}";
        return monitor.IsPrimary ? label + " (primary)" : label;
    }

    private static IReadOnlyList<MonitorSettings> MergeMonitors(
        AppSettings original,
        IReadOnlyList<MonitorRow> rows)
    {
        var connected = rows
            .Select(row => row.MonitorId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var monitors = original.Monitors
            .Where(entry => !connected.Contains(entry.MonitorId))
            .ToList();
        foreach (var row in rows)
        {
            var managed = row.Managed.Checked;
            var edge = row.Edge.SelectedIndex switch
            {
                1 => StripEdge.Left,
                2 => StripEdge.Right,
                _ => (StripEdge?)null
            };
            if (managed && edge is null)
            {
                continue;
            }

            monitors.Add(new MonitorSettings(row.MonitorId, managed, edge));
        }

        return monitors;
    }

    private sealed record MonitorRow(
        string MonitorId,
        CheckBox Managed,
        ComboBox Edge);

    private static HotkeyBinding BindingOf(HotkeyTextBox box, HotkeyBinding fallback) =>
        box.Tag as HotkeyBinding ?? fallback;
}
