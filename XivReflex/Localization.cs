using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace XivReflex;

public static class Localization
{
    private static readonly FrozenDictionary<string, Dictionary<string, string>> Localizations = new Dictionary<string, Dictionary<string, string>>()
    {
        ["ConfigWindow.WindowName"] = new() {
            { "en", "XivReflex Configuration" },
            { "de", "XivReflex Konfiguration" }
        },
        ["ConfigWindow.GitHubLink.Tooltip"] = new() {
            { "en", "Visit the XivReflex GitHub Repository" },
            { "de", "Zum XivReflex GitHub Repository" }
        },
        ["ConfigWindow.SponsorLink.Tooltip"] = new() {
            { "en", "Support me on GitHub Sponsors" },
            { "de", "Unterstütze mich auf GitHub Sponsors" }
        },
    }.ToFrozenDictionary();

    public static string t(string key)
        => TryGetTranslation(key, out var text) ? text : key;

    public static string Translate(string key)
        => TryGetTranslation(key, out var text) ? text : key;

    public static bool TryGetTranslation(string key, [MaybeNullWhen(returnValue: false)] out string text)
    {
        text = string.Empty;
        return Localizations.TryGetValue(key, out var languages)
            && (languages.TryGetValue(Services.PluginInterface.UiLanguage, out text)
            || languages.TryGetValue("en", out text));
    }
}
