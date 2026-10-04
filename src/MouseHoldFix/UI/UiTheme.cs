using System.Drawing.Drawing2D;

namespace MouseHoldFix.UI;

internal static class UiTheme
{
    internal static Color Canvas => SystemInformation.HighContrast ? SystemColors.Control : Color.FromArgb(242, 246, 250);
    internal static Color Surface => SystemColors.Window;
    internal static Color Ink => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(27, 43, 62);
    internal static Color Muted => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(91, 108, 125);
    internal static Color Accent => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(8, 116, 124);
    internal static Color Border => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(216, 226, 235);
    internal static Color Danger => SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(173, 48, 48);
    internal static readonly Font BodyFont = new("Segoe UI", 9.5f);
    internal static readonly Font HeadingFont = new("Segoe UI", 18, FontStyle.Bold);
    internal static readonly Font SectionFont = new("Segoe UI", 10, FontStyle.Bold);
    internal static readonly Font NumberFont = new("Segoe UI", 21, FontStyle.Bold);

    internal static void Configure(Form form, string title, Size size, bool resizable = false)
    {
        form.SuspendLayout();
        form.Text = title;
        form.Font = BodyFont;
        form.BackColor = Canvas;
        form.ForeColor = Ink;
        var icon = AppIcons.Load();
        form.Icon = icon;
        form.Disposed += (_, _) => icon.Dispose();
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ClientSize = size;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.FormBorderStyle = resizable ? FormBorderStyle.Sizable : FormBorderStyle.FixedDialog;
        form.MaximizeBox = resizable;
    }

    internal static void Finish(Form form)
    {
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.ResumeLayout(performLayout: true);
    }

    internal static TableLayoutPanel Stack(int rows)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = rows,
            Margin = Padding.Empty, BackColor = Color.Transparent
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < rows; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        return table;
    }

    internal static Label Text(string text, bool section = false, bool muted = false) => new()
    {
        Text = text, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.Transparent,
        ForeColor = muted ? Muted : Ink, Font = section ? SectionFont : BodyFont,
        Margin = new Padding(0, 0, 0, 8)
    };

    internal static Control Header(string subtitle, int imageSize = 48, Label? detail = null)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0, 0, 0, 18)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        using var icon = AppIcons.Load(size: 128);
        var picture = new PictureBox
        {
            Image = icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(imageSize, imageSize),
            Margin = new Padding(0, 0, 14, 0), AccessibleName = "MouseHoldFix"
        };
        picture.Disposed += (_, _) => picture.Image?.Dispose();
        var labels = Stack(detail is null ? 2 : 3);
        var name = Text("MouseHoldFix");
        name.Font = HeadingFont;
        name.Margin = Padding.Empty;
        labels.Controls.Add(name);
        var caption = Text(subtitle, muted: true);
        caption.Margin = Padding.Empty;
        labels.Controls.Add(caption);
        if (detail is not null)
        {
            detail.Margin = Padding.Empty;
            labels.Controls.Add(detail);
        }
        header.Controls.Add(picture, 0, 0);
        header.Controls.Add(labels, 1, 0);
        return header;
    }

    internal static SurfacePanel Card(Control content, int padding = 16)
    {
        var card = new SurfacePanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(padding), Margin = new Padding(0, 0, 0, 12)
        };
        content.Dock = DockStyle.Fill;
        card.Controls.Add(content);
        return card;
    }

    internal static void Style(Button button, bool primary = false)
    {
        button.Font = BodyFont;
        button.AutoSize = false;
        // MeasureText uses screen pixels; the layout is scaled from 96 DPI later.
        var captionWidth = (int)Math.Ceiling(TextRenderer.MeasureText(button.Text, BodyFont).Width * 96d / button.DeviceDpi);
        button.Size = new Size(Math.Max(96, captionWidth + 28), 34);
        button.MinimumSize = button.Size;
        button.Padding = Padding.Empty;
        button.Margin = new Padding(8, 0, 0, 0);
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = primary ? Accent : Surface;
        button.ForeColor = primary ? SystemColors.HighlightText : Ink;
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary
            ? (SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(6, 94, 102)) : Canvas;
        button.Cursor = Cursors.Hand;
    }

    internal static void Style(CheckBox checkBox)
    {
        checkBox.AutoSize = true;
        checkBox.ForeColor = Ink;
        checkBox.Margin = new Padding(0, 2, 0, 12);
    }

    internal sealed class SurfacePanel : Panel
    {
        internal SurfacePanel()
        {
            BackColor = Surface;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Transparent layout containers have no opaque color to clear with.
            var background = Parent;
            while (background is not null && background.BackColor.A < 255) background = background.Parent;
            e.Graphics.Clear(background?.BackColor ?? Canvas);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var radius = Math.Min(12f * DeviceDpi / 96, Math.Min(Width, Height) / 2f);
            if (radius <= 0) return;
            var diameter = radius * 2;
            using var path = new GraphicsPath();
            path.AddArc(0, 0, diameter, diameter, 180, 90);
            path.AddArc(Width - diameter - 1, 0, diameter, diameter, 270, 90);
            path.AddArc(Width - diameter - 1, Height - diameter - 1, diameter, diameter, 0, 90);
            path.AddArc(0, Height - diameter - 1, diameter, diameter, 90, 90);
            path.CloseFigure();
            using var brush = new SolidBrush(Surface);
            using var pen = new Pen(Border);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
            base.OnPaint(e);
        }
    }

    internal sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Border;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Surface;
        public override Color MenuItemSelected => SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(226, 243, 243);
        public override Color MenuItemBorder => Border;
        public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
        public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
        public override Color MenuItemPressedGradientBegin => MenuItemSelected;
        public override Color MenuItemPressedGradientMiddle => MenuItemSelected;
        public override Color MenuItemPressedGradientEnd => MenuItemSelected;
    }
}
