using System.Globalization;
using MouseHoldFix.Configuration;
using MouseHoldFix.Localization;
using MouseHoldFix.UI;

namespace MouseHoldFix;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var language = UiLanguage.Select(CultureInfo.CurrentUICulture);
        CultureInfo.CurrentUICulture = language;
        CultureInfo.DefaultThreadCurrentUICulture = language;
        var smoke = args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        if (smoke)
        {
            var languageIndex = Array.IndexOf(args, "--test-language");
            if (languageIndex >= 0 && languageIndex + 1 < args.Length)
            {
                language = UiLanguage.Select(CultureInfo.GetCultureInfo(args[languageIndex + 1]));
                CultureInfo.CurrentUICulture = language;
                CultureInfo.DefaultThreadCurrentUICulture = language;
            }
        }
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseHoldFix");
        if (smoke)
        {
            var index = Array.IndexOf(args, "--test-data");
            if (index < 0 || index + 1 >= args.Length) return 2;
            dataDirectory = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(dataDirectory);
        }

        using var mutex = new Mutex(true, smoke ? @"Local\MouseHoldFix.Smoke" : @"Local\MouseHoldFix", out var created);
        if (!created) return 0;
        TrayApplicationContext? context = null;
        EventHandler exitHandler = (_, _) => context?.EmergencyStop();
        UnhandledExceptionEventHandler exceptionHandler = (_, _) => context?.EmergencyStop();
        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            AppDomain.CurrentDomain.ProcessExit += exitHandler;
            AppDomain.CurrentDomain.UnhandledException += exceptionHandler;
            context = new TrayApplicationContext(dataDirectory,
                args.Contains("--startup", StringComparer.OrdinalIgnoreCase), smoke);
            Application.Run(context);
            return 0;
        }
        catch (Exception ex)
        {
            context?.EmergencyStop();
            if (smoke) File.WriteAllText(Path.Combine(dataDirectory, "smoke-test.txt"), $"FAIL: {ex}");
            else MessageBox.Show(ex.Message, Texts.Get("StartError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            AppDomain.CurrentDomain.ProcessExit -= exitHandler;
            AppDomain.CurrentDomain.UnhandledException -= exceptionHandler;
            context?.Dispose();
            mutex.ReleaseMutex();
        }
    }
}
