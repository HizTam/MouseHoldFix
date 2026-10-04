using System.Reflection;
using System.Globalization;

namespace MouseHoldFix;

internal static class AppInfo
{
    internal static string Author { get; } = typeof(AppInfo).Assembly
        .GetCustomAttribute<AssemblyCompanyAttribute>()!.Company;
    internal const string License = "MIT";
    internal static string LicenseText
    {
        get
        {
            using var stream = typeof(AppInfo).Assembly.GetManifestResourceStream("MouseHoldFix.License.txt")
                ?? throw new InvalidOperationException("The embedded license text is missing.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
    internal static string Copyright => CopyrightForYear(DateTime.Now.Year);
    internal static string CopyrightForYear(int year) => string.Create(CultureInfo.InvariantCulture, $"Copyright (c) {year} {Author}");
    internal static string Version { get; } = typeof(AppInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
}
