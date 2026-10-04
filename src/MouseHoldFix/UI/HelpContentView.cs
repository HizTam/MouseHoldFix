using System.Text.RegularExpressions;

namespace MouseHoldFix.UI;

internal sealed class HelpContentView : FlowLayoutPanel
{
    private bool _arranging;
    internal event Action<string>? LinkClicked;
    internal List<(string Title, Control Control)> Sections { get; } = [];

    internal HelpContentView()
    {
        Name = "HelpContent";
        Dock = DockStyle.Fill;
        AutoScroll = true;
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        BackColor = UiTheme.Surface;
        Margin = Padding.Empty;
        Padding = new Padding(4);
        DoubleBuffered = true;
    }

    internal void Render(string source, bool plainText = false, bool readme = false)
    {
        SuspendLayout();
        try
        {
            var previous = Controls.Cast<Control>().ToArray();
            Controls.Clear();
            foreach (var control in previous) control.Dispose();
            Sections.Clear();
            AutoScrollPosition = Point.Empty;
            var lines = source.ReplaceLineEndings("\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                // The window header already displays the app name and icon.
                if (readme && (line == "# MouseHoldFix" || line.StartsWith("<img src=\"assets/MouseHoldFix.png\"", StringComparison.Ordinal))) continue;
                if (!plainText && line.StartsWith('#'))
                {
                    var level = line.TakeWhile(c => c == '#').Count();
                    var title = line[level..].Trim();
                    var heading = AddText(title, level <= 2 ? UiTheme.HeadingFont : UiTheme.SectionFont);
                    heading.Margin = new Padding(0, Scale(12), 0, Scale(10));
                    Sections.Add((HelpDocument.Format(title).Text, heading));
                }
                else if (!plainText && Regex.Match(line, "^<img src=\"(assets/[^\"]+)\" alt=\"([^\"]*)\" width=\"(\\d+)\"\\s*/?>$") is { Success: true } image)
                {
                    var picture = new PictureBox
                    {
                        Name = image.Groups[1].Value, Image = HelpDocument.LoadImage(image.Groups[1].Value),
                        AccessibleName = image.Groups[2].Value, SizeMode = PictureBoxSizeMode.Zoom,
                        Tag = int.Parse(image.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                        Margin = new Padding(0, 0, 0, Scale(14)), TabStop = false
                    };
                    picture.Disposed += (_, _) => picture.Image?.Dispose();
                    Controls.Add(picture);
                }
                else if (!plainText && line.StartsWith('|'))
                {
                    var rows = new List<string[]>();
                    do
                    {
                        var cells = lines[i].Trim().Trim('|').Split('|').Select(x => x.Trim()).ToArray();
                        if (!cells.All(x => Regex.IsMatch(x, @"^:?-+:?$"))) rows.Add(cells);
                        i++;
                    } while (i < lines.Length && lines[i].Trim().StartsWith('|'));
                    i--;
                    AddTable(rows);
                }
                else if (!plainText && (line.StartsWith("- ", StringComparison.Ordinal) || Regex.IsMatch(line, @"^\d+\. ")))
                {
                    AddText(line.StartsWith("- ", StringComparison.Ordinal) ? $"• {line[2..]}" : line);
                }
                else
                {
                    var paragraph = line;
                    while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0)
                    {
                        var next = lines[i + 1].Trim();
                        if (!plainText && (next.StartsWith('#') || next.StartsWith('|') || next.StartsWith("<img", StringComparison.Ordinal)
                            || next.StartsWith("- ", StringComparison.Ordinal) || Regex.IsMatch(next, @"^\d+\. "))) break;
                        paragraph += " " + next;
                        i++;
                    }
                    AddText(paragraph, !plainText && paragraph.StartsWith("**", StringComparison.Ordinal) && paragraph.EndsWith("**", StringComparison.Ordinal)
                        ? UiTheme.SectionFont : null, plainText);
                }
            }
        }
        finally { ResumeLayout(performLayout: true); }
    }

    internal void ScrollToSection(int index)
    {
        var heading = Sections[index].Control;
        AutoScrollPosition = new Point(0, Math.Max(0, heading.Top - AutoScrollPosition.Y - Padding.Top));
    }

    private LinkLabel MakeText(string source, Font? font = null, bool plainText = false)
    {
        var content = plainText ? new HelpText(source, []) : HelpDocument.Format(source);
        var label = new LinkLabel
        {
            Text = content.Text, Font = font ?? UiTheme.BodyFont, ForeColor = UiTheme.Ink, BackColor = UiTheme.Surface,
            LinkColor = UiTheme.Accent, ActiveLinkColor = UiTheme.Accent, VisitedLinkColor = UiTheme.Accent,
            UseMnemonic = false, AutoSize = false, LinkArea = new LinkArea(0, 0),
            Margin = new Padding(0, 0, 0, Scale(10)), TabStop = content.Links.Count > 0
        };
        foreach (var link in content.Links) label.Links.Add(link.Start, link.Length, link.Target);
        label.LinkClicked += (_, args) => { if (args.Link?.LinkData is string target) LinkClicked?.Invoke(target); };
        return label;
    }

    private LinkLabel AddText(string source, Font? font = null, bool plainText = false)
    {
        var label = MakeText(source, font, plainText);
        Controls.Add(label);
        return label;
    }

    private void AddTable(List<string[]> rows)
    {
        var table = new TableLayoutPanel
        {
            ColumnCount = rows.Max(x => x.Length), RowCount = rows.Count, BackColor = UiTheme.Border,
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single, Margin = new Padding(0, 0, 0, Scale(14))
        };
        for (var column = 0; column < table.ColumnCount; column++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, column == 0 ? 38 : 62f / (table.ColumnCount - 1)));
        for (var row = 0; row < rows.Count; row++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute));
            for (var column = 0; column < table.ColumnCount; column++)
            {
                var cell = MakeText(column < rows[row].Length ? rows[row][column] : "", row == 0 ? UiTheme.SectionFont : null);
                cell.Dock = DockStyle.Fill;
                cell.Padding = new Padding(Scale(8));
                cell.Margin = Padding.Empty;
                table.Controls.Add(cell, column, row);
            }
        }
        Controls.Add(table);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (!_arranging)
        {
            _arranging = true;
            try
            {
                // Reserve a scrollbar width even before the scrollbar appears to avoid width oscillation.
                var width = Math.Max(1, ClientSize.Width - Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
                foreach (Control block in Controls)
                {
                    if (block is PictureBox { Image: { } image, Tag: int requestedWidth } picture)
                    {
                        var imageWidth = Math.Min(width, Scale(requestedWidth));
                        picture.Size = new Size(imageWidth, Math.Max(1, (int)Math.Round(imageWidth * (double)image.Height / image.Width)));
                    }
                    else if (block is TableLayoutPanel table)
                    {
                        table.Width = width;
                        var height = table.RowCount + 1;
                        for (var row = 0; row < table.RowCount; row++)
                        {
                            var rowHeight = 1;
                            for (var column = 0; column < table.ColumnCount; column++)
                            {
                                var cell = (LinkLabel)table.GetControlFromPosition(column, row)!;
                                var cellWidth = Math.Max(1, (int)((width - table.ColumnCount - 1) * table.ColumnStyles[column].Width / 100));
                                rowHeight = Math.Max(rowHeight, TextHeight(cell, cellWidth));
                            }
                            table.RowStyles[row].Height = rowHeight;
                            height += rowHeight;
                        }
                        table.Height = height;
                    }
                    else if (block is LinkLabel label) label.Size = new Size(width, TextHeight(label, width));
                }
            }
            finally { _arranging = false; }
        }
        base.OnLayout(levent);
    }

    private static int TextHeight(LinkLabel label, int width) => label.GetPreferredSize(new Size(Math.Max(1, width), int.MaxValue)).Height + 2;

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96d);
}
