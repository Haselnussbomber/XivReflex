using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Dalamud.Configuration;
using Dalamud.Utility;

namespace XivReflex;

public partial class PluginConfig : IPluginConfiguration
{
    [JsonIgnore]
    public const int CURRENT_CONFIG_VERSION = 1;

    [JsonIgnore]
    public int LastSavedConfigHash { get; set; }

    [JsonIgnore]
    public static JsonSerializerOptions? SerializerOptions { get; private set; }

    public event Action<string>? ConfigOptionChanged;

    public static PluginConfig Load()
    {
        SerializerOptions = new JsonSerializerOptions()
        {
            IncludeFields = true,
            WriteIndented = true,
        };

        var fileInfo = Services.PluginInterface.ConfigFile;
        if (!fileInfo.Exists || fileInfo.Length < 2)
            return new();

        var json = File.ReadAllText(fileInfo.FullName);
        if (JsonNode.Parse(json) is not JsonObject config)
            return new();

        return config.Deserialize<PluginConfig>(SerializerOptions) ?? new();
    }

    public void Save()
    {
        try
        {
            var serialized = JsonSerializer.Serialize(this, SerializerOptions);
            var hash = StringComparer.Ordinal.GetHashCode(serialized);

            if (LastSavedConfigHash != hash)
            {
                FilesystemUtil.WriteAllTextSafe(Services.PluginInterface.ConfigFile.FullName, serialized);
                LastSavedConfigHash = hash;
                Services.PluginLog.Information("Configuration saved.");
            }
        }
        catch (Exception e)
        {
            Services.PluginLog.Error(e, "Error saving config");
        }
    }

    public void RaiseConfigOptionChanged(string fieldName)
    {
        ConfigOptionChanged?.Invoke(fieldName);
    }
}

public partial class PluginConfig
{
    public int Version { get; set; } = CURRENT_CONFIG_VERSION;

    public bool LowLatencyMode = true;
    public bool LowLatencyBoost;
    public bool UseFPSLimit;
    public float FpsLimit = 60;
    public bool UseMarkersToOptimize;
}
