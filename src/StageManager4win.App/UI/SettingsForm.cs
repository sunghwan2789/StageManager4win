using StageManager4win.Core.Layout;
using StageManager4win.Core.Settings;
using StageManager4win.Core.Stages;

namespace StageManager4win.UI;

public sealed class SettingsForm : Form
{
    public SettingsForm(AppSettings settings)
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

        var enabled = new CheckBox { Text = "Enabled", Checked = settings.Enabled, AutoSize = true };
        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        mode.Items.AddRange(new object[] { "One at a Time", "All at Once", "Automatic" });
        mode.SelectedIndex = (int)settings.WindowMode;
        var autoHide = new CheckBox { Text = "Auto-hide", Checked = settings.AutoHide, AutoSize = true };
        var edge = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        edge.Items.AddRange(new object[] { "Left", "Right" });
        edge.SelectedIndex = (int)settings.StripEdge;
        var maxItems = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 12,
            Value = Math.Clamp(settings.MaxVisibleStripItems, 1, 12),
            Width = 80
        };
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

        var toggle = NewHotkeyBox(settings.Hotkeys.Toggle);
        var next = NewHotkeyBox(settings.Hotkeys.Next);
        var previous = NewHotkeyBox(settings.Hotkeys.Previous);
        var showDesktop = NewHotkeyBox(settings.Hotkeys.ShowDesktop);

        var row = 0;
        AddRow(layout, "Enabled", enabled, row++);
        AddRow(layout, "Window mode", mode, row++);
        AddRow(layout, "Auto-hide", autoHide, row++);
        AddRow(layout, "Strip edge", edge, row++);
        AddRow(layout, "Max visible strip items", maxItems, row++);
        AddRow(layout, "Preview FPS", fps, row++);
        AddRow(layout, "Excluded app IDs (one per line)", excluded, row++);
        AddRow(layout, "Toggle hotkey", toggle, row++);
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
            Result = BuildResult(settings, enabled, mode, autoHide, edge, maxItems, fps, excluded,
                toggle, next, previous, showDesktop);
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
        CheckBox enabled,
        ComboBox mode,
        CheckBox autoHide,
        ComboBox edge,
        NumericUpDown maxItems,
        ComboBox fps,
        TextBox excluded,
        HotkeyTextBox toggle,
        HotkeyTextBox next,
        HotkeyTextBox previous,
        HotkeyTextBox showDesktop) => original with
        {
            Enabled = enabled.Checked,
            WindowMode = (WindowMode)mode.SelectedIndex,
            AutoHide = autoHide.Checked,
            StripEdge = (StripEdge)edge.SelectedIndex,
            MaxVisibleStripItems = (int)maxItems.Value,
            PreviewFps = (int)(fps.SelectedItem ?? 30),
            Hotkeys = new HotkeySettings(
                BindingOf(toggle, original.Hotkeys.Toggle),
                BindingOf(next, original.Hotkeys.Next),
                BindingOf(previous, original.Hotkeys.Previous),
                BindingOf(showDesktop, original.Hotkeys.ShowDesktop)),
            ExcludedAppIds = excluded.Lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToArray()
        };

    private static HotkeyBinding BindingOf(HotkeyTextBox box, HotkeyBinding fallback) =>
        box.Tag as HotkeyBinding ?? fallback;
}
