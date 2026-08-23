using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using static XivReflex.Localization;

namespace XivReflex;

public class ConfigWindow : Window, IDisposable
{
    public ConfigWindow() : base("XivReflexConfig")
    {
        AllowClickthrough = false;
        AllowPinning = false;

        Flags |= ImGuiWindowFlags.NoScrollbar;

        Size = new Vector2(500, 500);
        SizeCondition = ImGuiCond.Appearing;

        WindowName = $"{t("ConfigWindow.WindowName")}##XivReflexConfig";

        Services.PluginInterface.LanguageChanged += OnLanguageChanged;
        Services.PluginInterface.UiBuilder.OpenConfigUi += Toggle;
    }

    public void Dispose()
    {
        Services.PluginInterface.LanguageChanged -= OnLanguageChanged;
        Services.PluginInterface.UiBuilder.OpenConfigUi -= Toggle;
    }

    private void OnLanguageChanged(string langCode)
    {
        WindowName = $"{t("ConfigWindow.WindowName")}##XivReflexConfig";
    }

    public override void Draw()
    {
        var config = Services.Config;

        var style = ImGui.GetStyle();

        var labels = new string[] {
            t("Config.NVIDIAReflexLowLatency.Label"),
            t("Config.FrameRateLimiter.Label"),
            t("Config.FrameRateLimit.Label"),
        };

        var labelColumnWidth = labels.Max(text => ImGui.CalcTextSize(text).X) + style.ItemSpacing.X * 2;
        var changed = false;
        var isInitialized = Services.ReflexManager.InitStatus == NvAPI_Status.NVAPI_OK;

        if (!isInitialized)
        {
            // yadda yadda
        }

        using (ImRaii.Disabled(!isInitialized))
        {
            // LowLatencyMode / LowLatencyBoost
            ImGui.Text(t("Config.NVIDIAReflexLowLatency.Label"));
            ImGui.SameLine(labelColumnWidth);
            var lowLatency = config.LowLatencyMode switch
            {
                true when config.LowLatencyBoost => 2,
                true => 1,
                _ => 0
            };
            ImGui.SetNextItemWidth(100);
            if (ImGui.Combo("##LowLatency"u8, ref lowLatency, [
                t("Config.NVIDIAReflexLowLatency.Option.Off"),
                t("Config.NVIDIAReflexLowLatency.Option.On"),
                t("Config.NVIDIAReflexLowLatency.Option.OnBoost")]))
            {
                switch (lowLatency)
                {
                    case 0:
                        config.LowLatencyMode = false;
                        config.LowLatencyBoost = false;
                        changed |= true;
                        break;

                    case 1:
                        config.LowLatencyMode = true;
                        config.LowLatencyBoost = false;
                        changed |= true;
                        break;

                    case 2:
                        config.LowLatencyMode = true;
                        config.LowLatencyBoost = true;
                        changed |= true;
                        break;
                }
            }

            ImGuiComponents.HelpMarker(t("Config.NVIDIAReflexLowLatency.HelpMessage"));

            // UseFPSLimit
            ImGui.Text(t("Config.FrameRateLimiter.Label"));
            ImGui.SameLine(labelColumnWidth);
            changed |= ImGui.Checkbox("##FrameRateLimiter"u8, ref config.UseFPSLimit);

            // FpsLimit
            using (ImRaii.Disabled(!config.UseFPSLimit))
            using (ImRaii.PushIndent())
            {
                ImGui.Text(t("Config.FrameRateLimit.Label"));
                ImGui.SameLine(labelColumnWidth);
                ImGui.SetNextItemWidth(100);
                changed |= ImGui.InputFloat("##FrameRateLimit"u8, ref config.FpsLimit);
            }

            // UseMarkersToOptimize?

            if (changed)
            {
                config.Save();
                Services.ReflexManager.SetSleepMode(
                    config.LowLatencyMode,
                    config.LowLatencyBoost,
                    config.UseFPSLimit,
                    config.FpsLimit,
                    config.UseMarkersToOptimize);
            }
        }

        if (isInitialized)
        {
            // TODO: FrameReport
            ImGui.Text($"SleepDuration: {Services.ReflexManager.SleepDuration.TotalMilliseconds.ToString(CultureInfo.InvariantCulture)}ms");
        }

        var contentAvail = ImGui.GetContentRegionAvail();
        var footerHeight = style.ItemSpacing.Y * 3 + ImGui.GetTextLineHeightWithSpacing();
        ImGui.Dummy(new Vector2(1, contentAvail.Y - footerHeight));

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var cursorPos = ImGui.GetCursorPos();

        DrawLink("GitHub", t("ConfigWindow.GitHubLink.Tooltip"), "https://github.com/Haselnussbomber/XivReflex");
        ImGui.SameLine();
        ImGui.Text("•");
        ImGui.SameLine();
        DrawLink("Sponsor", t("ConfigWindow.SponsorLink.Tooltip"), "https://github.com/sponsors/Haselnussbomber");

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            var versionString = "v" + version.ToString(3);
            ImGui.SetCursorPos(new Vector2(cursorPos.X + contentAvail.X - ImGui.CalcTextSize(versionString).X, cursorPos.Y));
            ImGui.TextDisabled(versionString);
        }
    }

    public static void DrawLink(string label, string title, string url)
    {
        ImGui.Text(label);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            using var tooltip = ImRaii.Tooltip();

            if (!string.IsNullOrEmpty(title))
                ImGui.TextColored(Vector4.One, title);

            ImGui.GetWindowDrawList().AddText(
                UiBuilder.IconFont, 12 * ImGuiHelpers.GlobalScale,
                ImGui.GetCursorScreenPos() + new Vector2(2 * ImGuiHelpers.GlobalScale),
                ColorText700,
                FontAwesomeIcon.ExternalLinkAlt.ToIconString());

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 20 * ImGuiHelpers.GlobalScale);

            ImGui.TextColored(ColorText700, url);
        }

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && ImGui.IsItemHovered())
            Task.Run(() => Util.OpenLink(url));
    }

    private static uint ColorText700 => ImGui.ColorConvertFloat4ToU32(ImGui.ColorConvertU32ToFloat4(ImGui.GetColorU32(ImGuiCol.Text)) with { W = 0.7f });
}
