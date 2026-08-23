using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace XivReflex;

public static class Services
{
    public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    public static IPluginLog PluginLog { get; private set; } = null!;
    public static IFramework Framework { get; private set; } = null!;
    public static ISigScanner SigScanner { get; private set; } = null!;
    public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    public static ICommandManager CommandManager { get; private set; } = null!;

    public static PluginConfig Config { get; private set; } = null!;
    public static ReflexManager ReflexManager { get; private set; } = null!;

    public static void Initialize(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
        PluginLog = pluginInterface.GetService<IPluginLog>();
        Framework = pluginInterface.GetService<IFramework>();
        SigScanner = pluginInterface.GetService<ISigScanner>();
        GameInteropProvider = pluginInterface.GetService<IGameInteropProvider>();
        CommandManager = pluginInterface.GetService<ICommandManager>();

        Config = PluginConfig.Load();
        NvApiNative.Initialize();
        ReflexManager = new ReflexManager();
    }

    private static T GetService<T>(this IServiceProvider serviceProvider)
    {
        return (T)serviceProvider.GetService(typeof(T))!;
    }
}
