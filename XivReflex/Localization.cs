using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace XivReflex;

public static class Localization
{
    private static readonly FrozenDictionary<string, Dictionary<string, string>> Localizations = new Dictionary<string, Dictionary<string, string>>()
    {
        ["CommandHandlerHelpMessage"] = new() {
            { "en", "Opens the XivReflex configuration window" },
            { "de", "Öffnet das XivReflex-Konfigurationsfenster" },
        },
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
        ["ConfigWindow.ReleaseNotesLink.Tooltip"] = new() {
            { "en", "Visit Release Notes" },
            { "de", "Zu den Release Notes" }
        },
        ["ConfigWindow.ReflexNotAvailable"] = new() {
            { "en", "NVIDIA Reflex is not available." },
            { "de", "NVIDIA Reflex ist nicht verfügbar." }
        },
        ["Config.NVIDIAReflexLowLatency.Label"] = new() {
            { "en", "NVIDIA Reflex Low Latency" }
        },
        ["Config.NVIDIAReflexLowLatency.Option.Off"] = new() {
            { "en", "Off" }
        },
        ["Config.NVIDIAReflexLowLatency.Option.On"] = new() {
            { "en", "On" }
        },
        ["Config.NVIDIAReflexLowLatency.Option.OnBoost"] = new() {
            { "en", "On + Boost" }
        },
        ["Config.NVIDIAReflexLowLatency.HelpMessage"] = new() {
            { "en", """
Off:
     Standard rendering pipeline. May provide slightly
     higher peak FPS, but higher system latency.
     
On:
     Optimizes system pipeline latency to align the CPU
     and GPU, significantly reducing input lag.
     Recommended for most players.
     
On + Boost:
     Further reduces latency by overriding GPU power-
     saving features to force maximum clock speeds.
     Increases GPU power draw and heat.
""" },
            { "de", """
Off:
     Standard-Rendering-Pipeline. Kann leicht höhere
     maximale FPS bieten, führt jedoch zu höherer Systemlatenz.

On:
     Optimiert die System-Pipeline-Latenz, um CPU und GPU
     zu synchronisieren, was die Eingabeverzögerung deutlich
     reduziert. Für die meisten Spieler empfohlen.

On + Boost:
     Reduziert die Latenz weiter, indem Energiesparfunktionen
     der GPU außer Kraft gesetzt werden, um maximale Taktraten
     zu erzwingen.
     Erhöht Stromverbrauch und Wärmeentwicklung der GPU.
""" }
        },
        ["Config.FrameRateLimiter.Label"] = new() {
            { "en", "Frame Rate Limiter" }
        },
        ["Config.FrameRateLimit.Label"] = new() {
            { "en", "Frame Rate Limit" }
        }
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
