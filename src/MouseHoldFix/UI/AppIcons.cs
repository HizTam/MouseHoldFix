using System.Reflection;
using MouseHoldFix.Localization;

namespace MouseHoldFix.UI;

internal static class AppIcons
{
    internal static Icon Load(bool enabled = true, int size = 32)
    {
        var name = enabled ? "MouseHoldFix.ico" : "MouseHoldFix-disabled.ico";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"MouseHoldFix.Assets.{name}")
            ?? throw new InvalidOperationException(Texts.Get("IconMissing"));
        using var original = new Icon(stream, new Size(size, size));
        return (Icon)original.Clone();
    }
}
