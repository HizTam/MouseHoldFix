using System.Globalization;
using MouseHoldFix.Configuration;
using MouseHoldFix.Diagnostics;
using MouseHoldFix.Input;
using MouseHoldFix.Localization;

namespace MouseHoldFix.UI;

internal sealed class DiagnosticsForm : Form
{
    private readonly Label _details = UiTheme.Text("", muted: true);
    private readonly Label _error = UiTheme.Text("");
    private readonly Label _mode = UiTheme.Text("", section: true);
    private readonly Label _modeDescription = UiTheme.Text("", muted: true);
    private readonly Label[] _values = Enumerable.Range(0, 4).Select(_ => UiTheme.Text("0")).ToArray();
    private readonly Button _repairTest = new() { Name = "EnableRepairAndLogging", Text = Texts.Get("EnableRepairAndLogging") };
    private readonly Button _logs = new() { Name = "OpenLogFolder", Text = Texts.Get("OpenLogs") };
    private readonly Button _deleteLogs = new() { Name = "DeleteLogs", Text = Texts.Get("DeleteLogs") };
    private readonly TextBox _events = new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9),
        BorderStyle = BorderStyle.None, BackColor = UiTheme.Surface, ForeColor = UiTheme.Ink
    };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly MouseHook _hook;
    private readonly DiagnosticLogger _logger;
    private bool _applying;
    private bool _deleting;
    private string? _logMessage, _logError;
    internal Task LogDeletionTask { get; private set; } = Task.CompletedTask;

    internal DiagnosticsForm(MouseHook hook, DiagnosticLogger logger, Action openLogs,
        Func<AppSettings> getSettings, Func<AppSettings, Task> apply)
    {
        _hook = hook;
        _logger = logger;
        UiTheme.Configure(this, Texts.Get("DiagnosticsTitle"), new Size(840, 586), resizable: true);
        MinimumSize = new Size(840, 586);
        var layout = UiTheme.Stack(7);
        layout.AutoSize = false;
        layout.Padding = new Padding(24, 20, 24, 20);
        layout.RowStyles[2] = new RowStyle(SizeType.Absolute, 104);
        layout.RowStyles[5] = new RowStyle(SizeType.Percent, 100);
        layout.Controls.Add(UiTheme.Header(Texts.Get("DiagnosticsHeading")), 0, 0);
        var status = UiTheme.Stack(2);
        status.Controls.Add(_mode);
        _modeDescription.Margin = Padding.Empty;
        status.Controls.Add(_modeDescription);
        layout.Controls.Add(UiTheme.Card(status), 0, 1);
        var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        var captions = new[] { "MetricRepairs", "MetricDrags", "MetricGap", "MetricFailures" };
        for (var i = 0; i < 4; i++)
        {
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var content = UiTheme.Stack(2);
            _values[i].Font = UiTheme.NumberFont;
            _values[i].Margin = Padding.Empty;
            _values[i].AutoSize = false;
            _values[i].Height = 42;
            _values[i].AutoEllipsis = true;
            _values[i].ForeColor = i == 0 ? UiTheme.Accent : UiTheme.Ink;
            content.Controls.Add(_values[i]);
            var caption = UiTheme.Text(Texts.Get(captions[i]), muted: true);
            caption.Margin = Padding.Empty;
            content.Controls.Add(caption);
            var card = UiTheme.Card(content, padding: 14);
            card.AutoSize = false;
            card.Margin = new Padding(0, 0, i == 3 ? 0 : 10, 12);
            metrics.Controls.Add(card, i, 0);
        }
        layout.Controls.Add(metrics, 0, 2);
        var details = UiTheme.Stack(2);
        details.Controls.Add(_details);
        _error.ForeColor = UiTheme.Danger;
        details.Controls.Add(_error);
        layout.Controls.Add(details, 0, 3);
        layout.Controls.Add(UiTheme.Text(Texts.Get("RecentEvents"), section: true), 0, 4);
        var eventsCard = UiTheme.Card(_events, padding: 12);
        eventsCard.AutoSize = false;
        eventsCard.Margin = Padding.Empty;
        layout.Controls.Add(eventsCard, 0, 5);
        var footer = new TableLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 14, 0, 0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Dock = DockStyle.Fill, Margin = Padding.Empty };
        UiTheme.Style(_repairTest, primary: true);
        _repairTest.Margin = new Padding(0, 0, 8, 0);
        buttons.Controls.Add(_repairTest);
        UiTheme.Style(_logs);
        _logs.Margin = new Padding(0, 0, 8, 0);
        _logs.Click += (_, _) => { if (Directory.Exists(_logger.DirectoryPath)) openLogs(); else RefreshStatus(); };
        buttons.Controls.Add(_logs);
        UiTheme.Style(_deleteLogs);
        _deleteLogs.ForeColor = UiTheme.Danger;
        _deleteLogs.Margin = Padding.Empty;
        _deleteLogs.Click += (_, _) => LogDeletionTask = DeleteLogsAsync();
        buttons.Controls.Add(_deleteLogs);
        footer.Controls.Add(buttons);
        var close = new Button { Text = Texts.Get("Close"), DialogResult = DialogResult.Cancel, CausesValidation = false };
        UiTheme.Style(close);
        close.Click += (_, _) => Close();
        footer.Controls.Add(close);
        CancelButton = close;
        layout.Controls.Add(footer, 0, 6);
        _repairTest.Click += async (_, _) => await ApplyTest(getSettings() with { Enabled = true, DiagnosticLogging = true });

        async Task ApplyTest(AppSettings settings)
        {
            _applying = true;
            RefreshStatus();
            try { await apply(settings); }
            catch (Exception ex)
            {
                if (!IsDisposed) MessageBox.Show(this, ex.Message, Texts.Get("SaveError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { _applying = false; if (!IsDisposed) RefreshStatus(); }
        }
        Controls.Add(layout);
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
        RefreshStatus();
        UiTheme.Finish(this);
    }

    private async Task DeleteLogsAsync()
    {
        if (_deleting || _applying || _logger.OperationPending || _logger.DiagnosticEnabled || !Directory.Exists(_logger.DirectoryPath))
        {
            RefreshStatus();
            return;
        }
        _deleting = true;
        _logError = null;
        _logMessage = Texts.Get("DeletingLogs");
        RefreshStatus();
        try
        {
            var moved = await _logger.RecycleLogsAsync();
            _logMessage = Texts.Get(moved ? "LogsDeleted" : "NoLogsToDelete");
        }
        catch (Exception ex) { _logError = Texts.Format("DeleteLogsError", ex.Message); }
        finally
        {
            _deleting = false;
            if (!IsDisposed) RefreshStatus();
        }
    }

    internal void RefreshStatus()
    {
        _hook.RequestSnapshot();
        var snapshot = _hook.Snapshot;
        _mode.Text = Texts.Format("RepairStatus", Texts.Get(snapshot.Enabled ? "Enabled" : "Disabled"));
        _mode.ForeColor = snapshot.Enabled ? UiTheme.Accent : UiTheme.Danger;
        _modeDescription.Text = Texts.Get(snapshot.Enabled ? "DiagnosticsRepairActive" : "DiagnosticsRepairDisabled");
        _repairTest.Enabled = !_applying && !_deleting && !_logger.OperationPending && (!snapshot.Enabled || !_logger.DiagnosticEnabled);
        var logsExist = Directory.Exists(_logger.DirectoryPath);
        _logs.Enabled = logsExist && !_deleting;
        _deleteLogs.Enabled = logsExist && !_logger.DiagnosticEnabled && !_logger.OperationPending && !_deleting && !_applying;
        var s = snapshot.Statistics;
        _values[0].Text = s.RepairedReleases.ToString("N0", CultureInfo.CurrentCulture);
        _values[1].Text = s.DragStarts.ToString("N0", CultureInfo.CurrentCulture);
        _values[2].Text = s.LastUpDownGapMs is { } gap ? $"{gap} ms" : "—";
        _values[3].Text = s.SendInputFailures.ToString("N0", CultureInfo.CurrentCulture);
        _values[3].ForeColor = s.SendInputFailures > 0 ? UiTheme.Danger : UiTheme.Ink;
        _details.Text = Texts.Format("DiagnosticsDetails", s.TotalLeftDown, s.TotalLeftUp,
            s.ReleaseCandidates, s.SyntheticReleases, _logger.DroppedEntries, Texts.State(snapshot.State),
            Texts.Get(_logger.DiagnosticEnabled ? "Enabled" : "Disabled"));
        var error = snapshot.Error ?? _logError ?? _logger.LastError;
        _error.Text = error ?? _logMessage ?? "";
        _error.ForeColor = error is null ? UiTheme.Muted : UiTheme.Danger;
        _error.Visible = _error.Text.Length > 0;
        var text = string.Join(Environment.NewLine, _logger.RecentLines);
        if (_events.Text == text) return;
        _events.Text = text;
        _events.SelectionStart = _events.TextLength;
        _events.ScrollToCaret();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
