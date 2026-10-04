using MouseHoldFix.Configuration;
using MouseHoldFix.Localization;

namespace MouseHoldFix.UI;

internal sealed class SettingsForm : Form
{
    private readonly CheckBox _enabled = new() { Text = Texts.Get("EnableRepair"), AutoSize = true };
    private readonly NumericUpDown _grace = new()
    {
        Name = "ReleaseGraceMs", Minimum = AppSettings.MinimumGraceMs, Maximum = AppSettings.MaximumGraceMs, Width = 90
    };
    private readonly CheckBox _dragOnly = new() { Text = Texts.Get("DragOnly"), AutoSize = true };
    private readonly CheckBox _diagnostics = new() { Text = Texts.Get("DiagnosticLogging"), AutoSize = true };
    private readonly CheckBox _startup = new() { Text = Texts.Get("StartupCheckbox"), AutoSize = true };
    private readonly Button _save = new() { Text = Texts.Get("Save"), AutoSize = true };
    private readonly Func<AppSettings, Task> _apply;
    private AppSettings _displayedSettings;

    internal SettingsForm(AppSettings settings, Func<AppSettings, Task> apply)
    {
        UiTheme.Configure(this, Texts.Get("SettingsTitle"), new Size(568, 460));
        _apply = apply;
        _displayedSettings = settings;
        var layout = UiTheme.Stack(5);
        layout.AutoSize = false;
        layout.Padding = new Padding(24, 20, 24, 20);
        layout.RowStyles[3] = new RowStyle(SizeType.Percent, 100);
        layout.Controls.Add(UiTheme.Header(Texts.Get("SettingsHeading")), 0, 0);
        var repair = UiTheme.Stack(5);
        repair.Controls.Add(UiTheme.Text(Texts.Get("RepairSection"), section: true));
        UiTheme.Style(_enabled);
        repair.Controls.Add(_enabled);
        var period = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 2, 0, 8) };
        period.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        period.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var graceLabel = UiTheme.Text(Texts.Get("GraceLabel"));
        graceLabel.Anchor = AnchorStyles.Left;
        graceLabel.Dock = DockStyle.None;
        graceLabel.Margin = Padding.Empty;
        period.Controls.Add(graceLabel);
        var value = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
        _grace.Width = 100;
        _grace.Font = UiTheme.SectionFont;
        _grace.Margin = Padding.Empty;
        value.Controls.Add(_grace);
        value.Controls.Add(new Label { Text = "ms", AutoSize = true, ForeColor = UiTheme.Muted, Margin = new Padding(8, 4, 0, 0) });
        period.Controls.Add(value);
        repair.Controls.Add(period);
        repair.Controls.Add(UiTheme.Text(Texts.Get("GraceHelp"), muted: true));
        UiTheme.Style(_dragOnly);
        _dragOnly.Margin = Padding.Empty;
        repair.Controls.Add(_dragOnly);
        layout.Controls.Add(UiTheme.Card(repair), 0, 1);
        var options = UiTheme.Stack(3);
        options.Controls.Add(UiTheme.Text(Texts.Get("OptionsSection"), section: true));
        UiTheme.Style(_diagnostics);
        UiTheme.Style(_startup);
        _startup.Margin = Padding.Empty;
        options.Controls.Add(_diagnostics);
        options.Controls.Add(_startup);
        layout.Controls.Add(UiTheme.Card(options), 0, 2);
        var footer = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var hint = UiTheme.Text(Texts.Get("SettingsSaveHint"), muted: true);
        hint.MaximumSize = new Size(250, 0);
        hint.Anchor = AnchorStyles.Left;
        hint.Dock = DockStyle.None;
        hint.Margin = Padding.Empty;
        footer.Controls.Add(hint);
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
        var cancel = new Button
        {
            Name = "CancelSettings", Text = Texts.Get("Cancel"), AutoSize = true,
            DialogResult = DialogResult.Cancel, CausesValidation = false
        };
        // DialogResult alone only closes modal forms; settings use Show().
        cancel.Click += (_, _) => Close();
        UiTheme.Style(cancel);
        UiTheme.Style(_save, primary: true);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_save);
        footer.Controls.Add(buttons);
        layout.Controls.Add(footer, 0, 4);
        Controls.Add(layout);
        _enabled.Checked = settings.Enabled;
        _grace.Value = settings.ReleaseGraceMs;
        _dragOnly.Checked = settings.DragOnly;
        _diagnostics.Checked = settings.DiagnosticLogging;
        _startup.Checked = settings.StartWithWindows;
        AcceptButton = _save;
        CancelButton = cancel;
        _save.Click += Save;
        UiTheme.Finish(this);
    }

    internal AppSettings ReadSettings() => new()
    {
        Enabled = _enabled.Checked, ReleaseGraceMs = (int)_grace.Value, DragOnly = _dragOnly.Checked,
        DiagnosticLogging = _diagnostics.Checked, StartWithWindows = _startup.Checked
    };

    internal void Synchronize(AppSettings settings)
    {
        // Refresh untouched fields while preserving the user's in-progress edits.
        var draft = ReadSettings();
        if (draft.Enabled == _displayedSettings.Enabled) _enabled.Checked = settings.Enabled;
        if (draft.ReleaseGraceMs == _displayedSettings.ReleaseGraceMs) _grace.Value = settings.ReleaseGraceMs;
        if (draft.DragOnly == _displayedSettings.DragOnly) _dragOnly.Checked = settings.DragOnly;
        if (draft.DiagnosticLogging == _displayedSettings.DiagnosticLogging) _diagnostics.Checked = settings.DiagnosticLogging;
        if (draft.StartWithWindows == _displayedSettings.StartWithWindows) _startup.Checked = settings.StartWithWindows;
        _displayedSettings = settings;
    }

    private async void Save(object? sender, EventArgs args)
    {
        if (!_dragOnly.Checked && MessageBox.Show(this,
            Texts.Get("ExperimentalPrompt"), Texts.Get("ExperimentalTitle"),
            MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
        _save.Enabled = false;
        try
        {
            await _apply(ReadSettings());
            if (!IsDisposed && !Disposing) Close();
        }
        catch (Exception ex)
        {
            // Closing the modeless window does not cancel an already-started save.
            if (IsDisposed || Disposing) return;
            MessageBox.Show(this, ex.Message, Texts.Get("SaveError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            if (!IsDisposed && !Disposing) _save.Enabled = true;
        }
    }
}
