using System.Diagnostics;
using System.Globalization;
using MouseHoldFix.Localization;

namespace MouseHoldFix.UI;

internal sealed class HelpForm : Form
{
    private readonly HelpContentView _content = new();
    private readonly ComboBox _sections = new() { Name = "HelpSections", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = Padding.Empty };
    private readonly Button _back = new() { Name = "BackHelp", Text = Texts.Get("HelpBack"), CausesValidation = false };
    private readonly Stack<string> _history = new();
    private readonly Action<string> _openExternal;
    internal string DocumentName { get; private set; } = "";

    internal HelpForm(Action<string>? openExternal = null)
    {
        _openExternal = openExternal ?? (url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
        UiTheme.Configure(this, Texts.Get("HelpTitle"), new Size(820, 640), resizable: true);
        MinimumSize = new Size(480, 360);
        var layout = UiTheme.Stack(4);
        layout.AutoSize = false;
        layout.Padding = new Padding(24);
        layout.RowStyles[2] = new RowStyle(SizeType.Percent, 100);
        layout.Controls.Add(UiTheme.Header(Texts.Get("HelpMenu")), 0, 0);
        var navigation = new TableLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 12)
        };
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        UiTheme.Style(_back);
        _back.Margin = new Padding(0, 0, 10, 0);
        _back.Click += (_, _) => { if (_history.Count > 0) Navigate(_history.Pop(), remember: false); };
        _sections.AccessibleName = Texts.Get("HelpContents");
        _sections.SelectionChangeCommitted += (_, _) =>
        {
            if (_sections.SelectedIndex > 0) _content.ScrollToSection(_sections.SelectedIndex - 1);
            else _content.AutoScrollPosition = Point.Empty;
        };
        navigation.Controls.Add(_back, 0, 0);
        navigation.Controls.Add(_sections, 1, 0);
        layout.Controls.Add(navigation, 0, 1);
        var card = UiTheme.Card(_content);
        card.AutoSize = false;
        layout.Controls.Add(card, 0, 2);
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty
        };
        var close = new Button { Name = "CloseHelp", Text = Texts.Get("Close"), DialogResult = DialogResult.Cancel, CausesValidation = false };
        UiTheme.Style(close, primary: true);
        close.Click += (_, _) => Close();
        buttons.Controls.Add(close);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        CancelButton = close;
        ActiveControl = close;
        _content.LinkClicked += target =>
        {
            try { FollowLink(target); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Texts.Get("HelpTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        Load += (_, _) =>
        {
            // Create document controls after the form has applied its initial DPI scaling.
            Navigate(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "README.ja.md" : "README.md", remember: false);
            _content.AutoScrollPosition = Point.Empty;
            ActiveControl = close;
        };
        Shown += (_, _) => { _content.AutoScrollPosition = Point.Empty; ActiveControl = close; };
        UiTheme.Finish(this);
    }

    internal void FollowLink(string target)
    {
        if (HelpDocument.Documents.Contains(target, StringComparer.Ordinal)) Navigate(target);
        else if (HelpDocument.IsExternalLink(target)) _openExternal(target);
        else throw new InvalidOperationException($"Unsupported help link: {target}");
    }

    private void Navigate(string name, bool remember = true)
    {
        var text = HelpDocument.Read(name);
        if (remember && DocumentName.Length > 0 && DocumentName != name) _history.Push(DocumentName);
        DocumentName = name;
        _content.Render(text, name.EndsWith(".txt", StringComparison.Ordinal), name.StartsWith("README", StringComparison.Ordinal));
        _sections.Items.Clear();
        _sections.Items.Add(Texts.Get("HelpContents"));
        foreach (var section in _content.Sections) _sections.Items.Add(section.Title);
        _sections.Enabled = _content.Sections.Count > 0;
        if (_sections.Items.Count > 0) _sections.SelectedIndex = 0;
        _back.Enabled = _history.Count > 0;
    }
}
