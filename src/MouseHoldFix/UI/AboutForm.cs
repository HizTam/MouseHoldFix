using MouseHoldFix.Localization;

namespace MouseHoldFix.UI;

internal sealed class AboutForm : Form
{
    private readonly Label _copyright = UiTheme.Text("", muted: true);
    private readonly Func<DateTime> _now;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 60_000 };

    internal AboutForm(Func<DateTime>? now = null)
    {
        _now = now ?? (() => DateTime.Now);
        RefreshCopyright();
        UiTheme.Configure(this, Texts.Get("AboutTitle"), new Size(600, 480), resizable: true);
        MinimumSize = new Size(480, 360);
        var layout = UiTheme.Stack(3);
        layout.AutoSize = false;
        layout.Padding = new Padding(24);
        layout.RowStyles[1] = new RowStyle(SizeType.Percent, 100);
        _copyright.Name = "Copyright";
        layout.Controls.Add(UiTheme.Header(AppInfo.Version, imageSize: 64, detail: _copyright), 0, 0);
        var license = UiTheme.Stack(2);
        license.AutoSize = false;
        license.RowStyles[1] = new RowStyle(SizeType.Percent, 100);
        license.Controls.Add(UiTheme.Text(Texts.Get("LicenseInformation"), section: true), 0, 0);
        var licenseText = new TextBox
        {
            Name = "LicenseText", Text = AppInfo.LicenseText.ReplaceLineEndings(Environment.NewLine), Multiline = true, ReadOnly = true,
            Dock = DockStyle.Fill, Margin = Padding.Empty, BorderStyle = BorderStyle.None,
            BackColor = UiTheme.Surface, ForeColor = UiTheme.Ink, Font = UiTheme.BodyFont,
            ScrollBars = ScrollBars.Vertical, WordWrap = true,
            AccessibleName = Texts.Get("LicenseInformation")
        };
        license.Controls.Add(licenseText, 0, 1);
        var licenseCard = UiTheme.Card(license);
        licenseCard.AutoSize = false;
        layout.Controls.Add(licenseCard, 0, 1);
        var buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty };
        var close = new Button { Name = "CloseAbout", Text = Texts.Get("Close"), DialogResult = DialogResult.Cancel, CausesValidation = false };
        UiTheme.Style(close, primary: true);
        close.Click += (_, _) => Close();
        buttons.Controls.Add(close);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
        AcceptButton = close;
        CancelButton = close;
        ActiveControl = close;
        Shown += (_, _) =>
        {
            licenseText.Select(0, 0);
            ActiveControl = close;
        };
        Activated += (_, _) => RefreshCopyright();
        _timer.Tick += (_, _) => RefreshCopyright();
        _timer.Start();
        RefreshCopyright();
        UiTheme.Finish(this);
    }

    internal void RefreshCopyright() => _copyright.Text = AppInfo.CopyrightForYear(_now().Year);
    internal string Summary => Texts.Format("AboutText", AppInfo.Version, AppInfo.Author, AppInfo.License, _copyright.Text);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
