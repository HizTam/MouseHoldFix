using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Globalization;
using Microsoft.Win32;
using MouseHoldFix.Configuration;
using MouseHoldFix.Diagnostics;
using MouseHoldFix.Input;
using MouseHoldFix.Startup;
using MouseHoldFix.Localization;

namespace MouseHoldFix.UI;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsService _store;
    private readonly DiagnosticLogger _logger;
    private readonly MouseHook _hook;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly Icon _enabledIcon = AppIcons.Load(), _disabledIcon = AppIcons.Load(false);
    private readonly ToolStripMenuItem _enabled = new(Texts.Get("EnableRepair")), _startup = new(Texts.Get("StartupMenu"));
    private readonly ToolStripMenuItem _openLogsMenu = new(Texts.Get("OpenLogs"));
    private readonly ToolStripMenuItem _graceMenu = new();
    private readonly ToolStripMenuItem _customGrace = new() { Enabled = false };
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 500 };
    private AppSettings _settings;
    private SettingsForm? _settingsForm;
    private DiagnosticsForm? _diagnosticsForm;
    private AboutForm? _aboutForm;
    private HelpForm? _helpForm;
    private string? _smokeHelpTarget;
    private string? _smokeRecycledLogPath;
    private bool _disposed, _updating;
    private Task _presetTask = Task.CompletedTask;
    private string? _shownError;
    private readonly bool _smoke;

    internal TrayApplicationContext(string directory, bool startup, bool smoke)
    {
        _smoke = smoke;
        _store = new SettingsService(directory);
        _settings = _store.Load();
        _logger = new DiagnosticLogger(directory, smoke ? logPath =>
        {
            var target = Path.Combine(directory, "RecycledLogs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Directory.Move(logPath, target);
            _smokeRecycledLogPath = target;
        } : null);
        if (_store.LoadError is { } loadError) _logger.Info($"ERROR Settings: {loadError}; defaults loaded.");
        if (smoke) _settings = _settings with { Enabled = false, StartWithWindows = false, DiagnosticLogging = false };
        else
        {
            try { _settings = _settings with { StartWithWindows = StartupManager.IsEnabled() }; }
            catch (Exception ex) { _logger.Info($"ERROR Startup read: {ex.Message}"); }
        }
        _logger.DiagnosticEnabled = _settings.DiagnosticLogging;
        _logger.Info($"START MouseHoldFix {AppInfo.Version}");
        try { _hook = new MouseHook(_settings, _logger); }
        catch { _logger.Dispose(); throw; }

        _menu = new ContextMenuStrip
        {
            Font = UiTheme.BodyFont, ForeColor = UiTheme.Ink,
            Renderer = new ToolStripProfessionalRenderer(new UiTheme.MenuColors())
        };
        _menu.Items.Add(new ToolStripMenuItem("MouseHoldFix") { Enabled = false });
        _menu.Items.Add(_enabled);
        foreach (var grace in GracePeriodPresets.Values)
        {
            var item = new ToolStripMenuItem($"{grace} ms") { Tag = grace };
            item.Click += (_, _) => _presetTask = ApplyFromMenu(_settings with { ReleaseGraceMs = grace });
            _graceMenu.DropDownItems.Add(item);
        }
        _graceMenu.DropDownItems.Add(_customGrace);
        _menu.Items.Add(_graceMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Texts.Get("SettingsMenu"), null, (_, _) => OpenSettings());
        _menu.Items.Add(Texts.Get("DiagnosticsMenu"), null, (_, _) => OpenDiagnostics());
        _openLogsMenu.Click += (_, _) => OpenLogs();
        _menu.Items.Add(_openLogsMenu);
        _menu.Opening += (_, _) => _openLogsMenu.Enabled = Directory.Exists(_logger.DirectoryPath);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_startup);
        _menu.Items.Add(Texts.Get("HelpMenu"), null, (_, _) => OpenHelp());
        _menu.Items.Add(Texts.Get("About"), null, (_, _) => OpenAbout());
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(Texts.Get("Exit"), null, (_, _) => ExitThread());
        _enabled.Click += async (_, _) => await ApplyFromMenu(_settings with { Enabled = !_settings.Enabled });
        _startup.Click += async (_, _) => await ApplyFromMenu(_settings with { StartWithWindows = !_settings.StartWithWindows });
        _tray = new NotifyIcon { ContextMenuStrip = _menu, Visible = true, Icon = _enabledIcon };
        _tray.DoubleClick += (_, _) => OpenSettings();
        RefreshTray();
        _statusTimer.Tick += (_, _) => PollStatus();
        _statusTimer.Start();
        SystemEvents.SessionEnding += SessionEnding;
        if (!startup && !smoke)
            _tray.ShowBalloonTip(2500, "MouseHoldFix", Texts.Get("StartupNotification"), ToolTipIcon.Info);
        if (smoke)
        {
            if (Marshal.SizeOf<NativeMethods.Input>() != 40 || Marshal.SizeOf<NativeMethods.MouseHookData>() != 32)
                throw new InvalidOperationException("Win32 input structure sizes do not match x64.");
            OpenSettings();
            OpenDiagnostics();
            OpenAbout();
            var ticks = 0;
            _statusTimer.Tick += async (_, _) =>
            {
                if (++ticks >= 3)
                {
                    _statusTimer.Stop();
                    try
                    {
                        await VerifyPresetSelection();
                        VerifySettingsCancellation();
                        await VerifyHelp(directory);
                        SavePreview(_helpForm!, Path.Combine(directory, "help.png"));
                        SavePreview(_settingsForm!, Path.Combine(directory, "settings.png"));
                        SavePreview(_diagnosticsForm!, Path.Combine(directory, "diagnostics.png"));
                        SavePreview(_aboutForm!, Path.Combine(directory, "about.png"));
                        var licenseText = (TextBox)_aboutForm!.Controls.Find("LicenseText", searchAllChildren: true).Single();
                        if (!licenseText.ReadOnly || !licenseText.Visible ||
                            licenseText.Text.Replace("\r\n", "\n") != AppInfo.LicenseText.Replace("\r\n", "\n"))
                            throw new InvalidOperationException("About does not show the complete license as read-only text.");
                        if (licenseText.SelectionLength != 0 || _aboutForm.ActiveControl?.Name != "CloseAbout")
                            throw new InvalidOperationException("About selected the license or did not initially focus Close.");
                        File.WriteAllText(Path.Combine(directory, "about-license.txt"), licenseText.Text);
                        SaveMenuPreview(_menu, Path.Combine(directory, "tray-menu.png"));
                        SaveMenuPreview(_graceMenu.DropDown, Path.Combine(directory, "grace-presets.png"));
                        await VerifyLogDeletion();
                        File.WriteAllText(Path.Combine(directory, "about.txt"),
                            _aboutForm!.Summary);
                        File.WriteAllText(Path.Combine(directory, "smoke-test.txt"),
                            $"PASS: version={AppInfo.Version}; author={AppInfo.Author}; license={AppInfo.License}; copyright={AppInfo.Copyright}; language={CultureInfo.CurrentUICulture.Name}; dpi={_settingsForm!.DeviceDpi}; presets=100,150,200,custom37; settings synchronized; typed draft preserved; cancel closed without saving; settings reopened; about icon and copyright visible; full read-only license verified; initial selection empty and Close focused; embedded help, images, local navigation and external link dispatch verified; log deletion, recording guard and missing-folder actions verified; hook installed; tray, settings and diagnostics created; state={_hook.Snapshot.State}; enabled={_hook.Snapshot.Enabled}");
                    }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "smoke-test.txt"), $"FAIL: {ex}"); }
                    finally { ExitThread(); }
                }
            };
        }
    }

    private void OpenSettings()
    {
        if (_settingsForm is null || _settingsForm.IsDisposed)
        {
            _settingsForm = new SettingsForm(_settings, Apply);
            if (_smoke) { _settingsForm.ShowInTaskbar = false; _settingsForm.Opacity = 0; }
            _settingsForm.Show();
        }
        else { _settingsForm.WindowState = FormWindowState.Normal; _settingsForm.Activate(); }
    }

    private void OpenDiagnostics()
    {
        if (_diagnosticsForm is null || _diagnosticsForm.IsDisposed)
        {
            _diagnosticsForm = new DiagnosticsForm(_hook, _logger, OpenLogs, () => _settings, Apply);
            if (_smoke) { _diagnosticsForm.ShowInTaskbar = false; _diagnosticsForm.Opacity = 0; }
            _diagnosticsForm.Show();
        }
        else { _diagnosticsForm.WindowState = FormWindowState.Normal; _diagnosticsForm.Activate(); }
    }

    private void OpenAbout()
    {
        if (_aboutForm is null || _aboutForm.IsDisposed)
        {
            _aboutForm = new AboutForm();
            if (_smoke) { _aboutForm.ShowInTaskbar = false; _aboutForm.Opacity = 0; }
            _aboutForm.Show();
        }
        else { _aboutForm.WindowState = FormWindowState.Normal; _aboutForm.Activate(); }
        _aboutForm.RefreshCopyright();
    }

    private void OpenHelp()
    {
        try
        {
            if (_helpForm is null || _helpForm.IsDisposed)
            {
                _helpForm = new HelpForm(_smoke ? target => _smokeHelpTarget = target : null);
                if (_smoke) { _helpForm.ShowInTaskbar = false; _helpForm.Opacity = 0; }
                _helpForm.Show();
            }
            else { _helpForm.WindowState = FormWindowState.Normal; _helpForm.Activate(); }
        }
        catch (Exception ex)
        {
            if (_smoke) throw;
            ShowError(ex.Message);
        }
    }

    private void OpenLogs()
    {
        try
        {
            if (!Directory.Exists(_logger.DirectoryPath)) { _openLogsMenu.Enabled = false; return; }
            Process.Start(new ProcessStartInfo(_logger.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async Task ApplyFromMenu(AppSettings settings)
    {
        try { await Apply(settings); }
        catch (Exception ex)
        {
            if (_smoke) throw;
            ShowError(ex.Message);
        }
    }

    private async Task VerifyLogDeletion()
    {
        var form = _diagnosticsForm!;
        var delete = (Button)form.Controls.Find("DeleteLogs", searchAllChildren: true).Single();
        var open = (Button)form.Controls.Find("OpenLogFolder", searchAllChildren: true).Single();
        _logger.DiagnosticEnabled = true;
        form.RefreshStatus();
        if (delete.Enabled) throw new InvalidOperationException("Log deletion is enabled while recording.");
        _logger.DiagnosticEnabled = false;
        form.RefreshStatus();
        if (!delete.Enabled || !open.Enabled) throw new InvalidOperationException("Existing log folder actions are disabled.");
        delete.PerformClick();
        await form.LogDeletionTask;
        RefreshTray();
        if (_smokeRecycledLogPath is null || !Directory.EnumerateFiles(_smokeRecycledLogPath, "*.log").Any() ||
            Directory.Exists(_logger.DirectoryPath) || _logger.RecentLines.Length != 0 ||
            delete.Enabled || open.Enabled || _openLogsMenu.Enabled)
            throw new InvalidOperationException("Log deletion did not close, move and clear the folder or disable its actions.");
        _logger.Info("SMOKE logging resumed after deletion");
        var deadline = Stopwatch.StartNew();
        while (!Directory.Exists(_logger.DirectoryPath) && deadline.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(10);
        form.RefreshStatus();
        RefreshTray();
        if (!delete.Enabled || !open.Enabled || !_openLogsMenu.Enabled)
            throw new InvalidOperationException("Log folder actions did not recover when new logs were written.");
    }

    private async Task VerifyPresetSelection()
    {
        var original = _settings;
        foreach (var grace in new[] { 100, 150, 200 })
        {
            var item = _graceMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(x => x.Tag is int value && value == grace);
            item.PerformClick();
            await _presetTask;
            if (_store.Load() != original with { ReleaseGraceMs = grace } || _hook.Snapshot.Enabled ||
                _settingsForm!.ReadSettings().ReleaseGraceMs != grace ||
                !item.Checked || _graceMenu.DropDownItems.OfType<ToolStripMenuItem>().Count(x => x.Checked) != 1)
                throw new InvalidOperationException($"Preset {grace} was not applied, saved and checked correctly.");
        }
        await Apply(original with { ReleaseGraceMs = 37 });
        if (!_customGrace.Available || _settingsForm!.ReadSettings().ReleaseGraceMs != 37 ||
            _graceMenu.DropDownItems.OfType<ToolStripMenuItem>().Any(x => x.Checked))
            throw new InvalidOperationException("Custom grace was not displayed correctly.");
        await Apply(original);
        var graceInput = (NumericUpDown)_settingsForm!.Controls.Find("ReleaseGraceMs", searchAllChildren: true).Single();
        graceInput.Text = "150";
        foreach (var grace in new[] { 100, 200 })
        {
            _graceMenu.DropDownItems.OfType<ToolStripMenuItem>().Single(x => x.Tag is int value && value == grace).PerformClick();
            await _presetTask;
            if (_store.Load().ReleaseGraceMs != grace || _settingsForm.ReadSettings() != original with { ReleaseGraceMs = 150 })
                throw new InvalidOperationException("Tray changes overwrote the settings draft.");
        }
        graceInput.Value = _settings.ReleaseGraceMs;
        await Apply(original);
        foreach (var forbidden in new[]
        {
            original with { Enabled = true }, original with { DiagnosticLogging = true },
            original with { StartWithWindows = true }, original with { DragOnly = !original.DragOnly }
        })
        {
            var rejected = false;
            try { await Apply(forbidden); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected || _settings != original || _store.Load() != original || _hook.Snapshot.Enabled)
                throw new InvalidOperationException("Smoke test changed settings other than grace.");
        }
    }

    private void VerifySettingsCancellation()
    {
        var original = _settings;
        var form = _settingsForm!;
        ((NumericUpDown)form.Controls.Find("ReleaseGraceMs", searchAllChildren: true).Single()).Text = "150";
        ((Button)form.Controls.Find("CancelSettings", searchAllChildren: true).Single()).PerformClick();
        if (!form.IsDisposed || _settings != original || _store.Load() != original)
            throw new InvalidOperationException("Cancel did not close settings without saving.");
        OpenSettings();
        if (_settingsForm!.ReadSettings() != original)
            throw new InvalidOperationException("Reopened settings retained cancelled edits.");
    }

    private async Task VerifyHelp(string directory)
    {
        var steps = Path.Combine(directory, "help-steps.txt");
        File.WriteAllText(steps, "");
        void Trace(string stage) => File.AppendAllText(steps, $"{DateTimeOffset.Now:O} {stage}{Environment.NewLine}");
        Trace("opening");
        var expected = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ja" ? "README.ja.md" : "README.md";
        var item = _menu.Items.OfType<ToolStripMenuItem>().Single(x => x.Text == Texts.Get("HelpMenu"));
        item.PerformClick();
        var form = _helpForm!;
        await Task.Yield();
        Trace("opened");
        item.PerformClick();
        if (_helpForm != form || form.DocumentName != expected || form.ActiveControl?.Name != "CloseHelp")
            throw new InvalidOperationException($"Help did not open correctly: document={form.DocumentName}; expected={expected}; focus={form.ActiveControl?.Name}; reused={_helpForm == form}.");
        var view = (HelpContentView)form.Controls.Find("HelpContent", searchAllChildren: true).Single();
        if (view.Controls.OfType<PictureBox>().Count() != 3 || view.Sections.Count < 10 ||
            !view.Controls.OfType<LinkLabel>().Any(x => x.Text.Contains("Win + Shift + S", StringComparison.Ordinal)))
            throw new InvalidOperationException("Help did not render the README sections, symptoms and embedded images.");
        var contents = (ComboBox)form.Controls.Find("HelpSections", searchAllChildren: true).Single();
        if (contents.Items.Count != view.Sections.Count + 1 || contents.SelectedItem?.ToString() != Texts.Get("HelpContents"))
            throw new InvalidOperationException("Help did not show the localized contents list.");
        view.ScrollToSection(2);
        if (Math.Abs(view.Sections[2].Control.Top - view.Padding.Top) > 1)
            throw new InvalidOperationException("Help did not align a selected section with the top of the viewport.");
        foreach (var document in HelpDocument.Documents)
        {
            Trace($"document {document}");
            form.FollowLink(document);
            if (form.DocumentName != document || view.Controls.Count == 0)
                throw new InvalidOperationException($"Help did not navigate to the local document: {document}");
            if (document == "LICENSE.txt" && string.Join("\n\n", view.Controls.OfType<LinkLabel>().Select(x => x.Text))
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).SequenceEqual(AppInfo.LicenseText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) == false)
                throw new InvalidOperationException("Help changed the license text.");
        }
        form.FollowLink(expected);
        foreach (var invalid in new[] { "file:///C:/Windows/win.ini", "javascript:alert(1)", "../README.md", "C:/Windows/win.ini" })
        {
            var rejected = false;
            try { form.FollowLink(invalid); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected || form.DocumentName != expected || _smokeHelpTarget is not null)
                throw new InvalidOperationException("Help accepted an unsupported file or executable link.");
        }
        form.FollowLink("https://github.com/sponsors/hiztam");
        if (_smokeHelpTarget != "https://github.com/sponsors/hiztam" || form.DocumentName != expected)
            throw new InvalidOperationException("Help did not dispatch an explicitly clicked external link.");
        if (HelpDocument.Format("`[literal](LICENSE.txt)` **bold** [license](LICENSE.txt)") is not
            { Text: "[literal](LICENSE.txt) bold license", Links.Count: 1 })
            throw new InvalidOperationException("Help interpreted a link inside inline code.");
        var back = (Button)form.Controls.Find("BackHelp", searchAllChildren: true).Single();
        form.FollowLink("CHANGELOG.md");
        back.PerformClick();
        if (form.DocumentName != expected) throw new InvalidOperationException("Help Back did not restore the README.");
        var originalSize = form.Size;
        form.Size = new Size(480 * form.DeviceDpi / 96, 360 * form.DeviceDpi / 96);
        view.PerformLayout();
        for (var index = 0; index < view.Sections.Count; index++) view.ScrollToSection(index);
        if (view.HorizontalScroll.Visible) throw new InvalidOperationException("Help requires horizontal scrolling at minimum size.");
        Trace("minimum-size layout");
        SavePreview(form, Path.Combine(directory, "help-minimum.png"));
        form.Size = originalSize;
        view.ScrollControlIntoView(view.Controls.OfType<TableLayoutPanel>().Last());
        SavePreview(form, Path.Combine(directory, "help-settings.png"));
        view.AutoScrollPosition = Point.Empty;
        ((Button)form.Controls.Find("CloseHelp", searchAllChildren: true).Single()).PerformClick();
        if (!form.IsDisposed) throw new InvalidOperationException("Help Close did not close the window.");
        item.PerformClick();
        await Task.Yield();
        if (_helpForm!.DocumentName != expected) throw new InvalidOperationException("Reopened Help did not restore the default language.");
        Trace("complete");
    }

    private void ShowError(string message)
    {
        _logger.Info($"ERROR UI: {message}");
        MessageBox.Show(message, "MouseHoldFix", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static void SavePreview(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        ValidateControls(form, form);
    }

    private static void SaveMenuPreview(ToolStripDropDown menu, string path)
    {
        menu.CreateControl();
        var size = menu.GetPreferredSize(Size.Empty);
        menu.AutoSize = false;
        menu.Size = size;
        menu.PerformLayout();
        using var bitmap = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void ValidateControls(Form form, Control parent)
    {
        // Help content extends beyond the viewport and is intentionally scrollable.
        if (parent is HelpContentView) return;
        foreach (Control control in parent.Controls)
        {
            if (control.Visible && control is Button or CheckBox or NumericUpDown or Label or PictureBox or TextBoxBase)
            {
                var bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                if (!form.ClientRectangle.Contains(bounds))
                    throw new InvalidOperationException($"Control clipped at {form.DeviceDpi} DPI: {control.Text}; bounds={bounds}; client={form.ClientRectangle}");
                if (control is Label label && label.Text.Length > 0 && label.ClientSize.Height < label.Font.Height)
                    throw new InvalidOperationException($"Text clipped at {form.DeviceDpi} DPI: {label.Text}; size={label.ClientSize}");
                if (control is Button button)
                {
                    var captionSize = TextRenderer.MeasureText(button.Text, button.Font);
                    if (captionSize.Width > button.ClientSize.Width - 8 || captionSize.Height > button.ClientSize.Height - 8)
                        throw new InvalidOperationException($"Button caption clipped at {form.DeviceDpi} DPI: {button.Text}; size={button.ClientSize}");
                }
            }
            ValidateControls(form, control);
        }
    }

    private async Task Apply(AppSettings settings)
    {
        if (_updating) throw new InvalidOperationException(Texts.Get("SettingsBusy"));
        var previous = _settings;
        settings = settings.Normalize();
        // Reject non-grace smoke changes before any registry, file or rollback operations.
        if (_smoke && settings with { ReleaseGraceMs = previous.ReleaseGraceMs } != previous)
            throw new InvalidOperationException(Texts.Get("SmokeReadOnly"));
        // Keep deletion excluded until application and any rollback have both completed.
        using var logConfiguration = _logger.BeginConfiguration();
        _updating = true;
        _graceMenu.Enabled = false;
        try
        {
            if (settings.StartWithWindows != previous.StartWithWindows) StartupManager.SetEnabled(settings.StartWithWindows);
            _store.Save(settings);
            await _hook.ConfigureAsync(settings).WaitAsync(TimeSpan.FromSeconds(3));
            _settings = settings;
            RefreshTray();
        }
        catch (Exception failure)
        {
            _logger.Info($"ERROR Settings apply: {failure.Message}");
            try
            {
                if (settings.StartWithWindows != previous.StartWithWindows) StartupManager.SetEnabled(previous.StartWithWindows);
                _store.Save(previous);
                await _hook.ConfigureAsync(previous).WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex) { _logger.Info($"ERROR Settings rollback: {ex.Message}"); }
            throw;
        }
        finally { _updating = false; if (!_disposed) _graceMenu.Enabled = true; }
    }

    private void PollStatus()
    {
        _openLogsMenu.Enabled = Directory.Exists(_logger.DirectoryPath);
        _hook.RequestSnapshot();
        var snapshot = _hook.Snapshot;
        if (!_updating && snapshot.Enabled != _settings.Enabled)
        {
            _settings = _settings with { Enabled = snapshot.Enabled };
            RefreshTray();
        }
        if (snapshot.Error is { } error && _shownError != error)
        {
            _tray.ShowBalloonTip(6000, Texts.Get("StoppedTitle"), error, ToolTipIcon.Warning);
        }
        // Recovery allows the same error to notify again after a later failure.
        _shownError = snapshot.Error;
    }

    private void RefreshTray()
    {
        if (_settingsForm is { IsDisposed: false }) _settingsForm.Synchronize(_settings);
        if (_diagnosticsForm is { IsDisposed: false }) _diagnosticsForm.RefreshStatus();
        _openLogsMenu.Enabled = Directory.Exists(_logger.DirectoryPath);
        _enabled.Checked = _settings.Enabled;
        _startup.Checked = _settings.StartWithWindows;
        _graceMenu.Text = Texts.Format("GraceMenu", _settings.ReleaseGraceMs);
        foreach (var item in _graceMenu.DropDownItems.OfType<ToolStripMenuItem>())
            if (item.Tag is int grace) item.Checked = grace == _settings.ReleaseGraceMs;
        _customGrace.Visible = !GracePeriodPresets.Values.Contains(_settings.ReleaseGraceMs);
        _customGrace.Text = Texts.Format("CurrentGrace", _settings.ReleaseGraceMs);
        _tray.Icon = _settings.Enabled ? _enabledIcon : _disabledIcon;
        _tray.Text = Texts.Format("TrayText", AppInfo.Version, Texts.Get(_settings.Enabled ? "Enabled" : "Disabled"), _settings.ReleaseGraceMs);
    }

    private void SessionEnding(object sender, SessionEndingEventArgs args)
    {
        // Session ending can be cancelled by another app. Keep the hook recoverable
        // and merely stop suppression; ProcessExit handles the final disposal.
        try { _hook.ConfigureAsync(_settings with { Enabled = false }).Wait(TimeSpan.FromSeconds(2)); }
        catch (Exception ex) { _logger.Info($"ERROR Session ending: {ex.Message}"); _hook.Dispose(); }
    }

    internal void EmergencyStop() => _hook.Dispose();

    protected override void ExitThreadCore()
    {
        Dispose();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            SystemEvents.SessionEnding -= SessionEnding;
            _statusTimer.Dispose();
            _hook.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            _settingsForm?.Dispose();
            _diagnosticsForm?.Dispose();
            _aboutForm?.Dispose();
            _helpForm?.Dispose();
            _menu.Dispose();
            _enabledIcon.Dispose();
            _disabledIcon.Dispose();
            _logger.Info("STOP MouseHoldFix");
            _logger.Dispose();
        }
        base.Dispose(disposing);
    }
}
