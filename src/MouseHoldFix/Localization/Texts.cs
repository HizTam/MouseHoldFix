using System.Globalization;
using System.Resources;
using MouseHoldFix.Input;

namespace MouseHoldFix.Localization;

internal static class Texts
{
    private static readonly ResourceManager Resources = new("MouseHoldFix.Localization.Strings", typeof(Texts).Assembly);

    internal static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new MissingManifestResourceException($"Missing UI resource: {key}");

    internal static string Format(string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), values);

    internal static string State(RepairState state) => Get($"State{state}");
}
