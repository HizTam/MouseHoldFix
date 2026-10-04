using System.Text;
using System.Text.RegularExpressions;

namespace MouseHoldFix.UI;

internal static class HelpDocument
{
    internal static readonly string[] Documents = ["README.md", "README.ja.md", "CHANGELOG.md", "LICENSE.txt", "THIRD-PARTY-NOTICES.md"];
    internal static readonly string[] Images = ["assets/screenshots/settings.en.png", "assets/screenshots/tray-menu.en.png", "assets/screenshots/diagnostics.en.png"];

    internal static string Read(string name)
    {
        if (!Documents.Contains(name, StringComparer.Ordinal)) throw new InvalidOperationException($"Unknown help document: {name}");
        if (name == "LICENSE.txt") return AppInfo.LicenseText;
        using var stream = Open(name);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    internal static Bitmap LoadImage(string name)
    {
        if (!Images.Contains(name, StringComparer.Ordinal)) throw new InvalidOperationException($"Unknown help image: {name}");
        using var stream = Open(name);
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static Stream Open(string name) => typeof(HelpDocument).Assembly.GetManifestResourceStream($"MouseHoldFix.Help.{name}")
        ?? throw new InvalidOperationException($"Missing help resource: {name}");

    // Render the inline Markdown used in the bundled documents without loading remote images or HTML.
    internal static HelpText Format(string source)
    {
        source = Regex.Replace(source, @"\[!\[([^\]]+)\]\([^)]+\)\]\(([^)]+)\)", "[$1]($2)");
        var text = new StringBuilder();
        var links = new List<HelpLink>();
        var position = 0;
        foreach (Match match in Regex.Matches(source, @"`[^`]*`|\[([^\]]+)\]\(([^)]+)\)|\*\*"))
        {
            text.Append(source[position..match.Index]);
            if (match.Value.StartsWith('`')) text.Append(match.Value[1..^1]);
            else if (match.Groups[1].Success)
            {
                var label = match.Groups[1].Value.Replace("**", "", StringComparison.Ordinal);
                links.Add(new HelpLink(text.Length, label.Length, match.Groups[2].Value));
                text.Append(label);
            }
            position = match.Index + match.Length;
        }
        text.Append(source[position..]);
        return new HelpText(text.ToString(), links);
    }

    internal static bool IsExternalLink(string target) => Uri.TryCreate(target, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http" && !string.IsNullOrEmpty(uri.Host);
}

internal sealed record HelpLink(int Start, int Length, string Target);
internal sealed record HelpText(string Text, IReadOnlyList<HelpLink> Links);
