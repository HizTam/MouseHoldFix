using System.Globalization;

namespace MouseHoldFix.Configuration;

public static class UiLanguage
{
    public static CultureInfo Select(CultureInfo systemUiCulture) =>
        CultureInfo.GetCultureInfo(systemUiCulture.TwoLetterISOLanguageName == "ja" ? "ja-JP" : "en-US");
}
