using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using static XivReflex.Localization;

namespace XivReflex;

public class Plugin(IDalamudPluginInterface pluginInterface) : IAsyncDalamudPlugin
{
    private PluginWindowSystem? _windowSystem;
    private ConfigWindow? _configWindow;
    private CommandInfo? _commandInfo;

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        Services.Initialize(pluginInterface);

        _windowSystem = new PluginWindowSystem();
        _configWindow = new ConfigWindow();
        _windowSystem.AddWindow(_configWindow);
        
        _commandInfo = new(OnCommand)
        {
            HelpMessage = t("CommandHandlerHelpMessage"),
        };
        Services.CommandManager.AddHandler("/xivreflex", _commandInfo);

        Services.PluginInterface.LanguageChanged += OnLanguageChanged;

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_configWindow != null)
        {
            _windowSystem?.RemoveWindow(_configWindow);
            _configWindow.Dispose();
            _configWindow = null;
        }

        _windowSystem?.Dispose();
        _windowSystem = null;

        Services.CommandManager.RemoveHandler("/xivreflex");

        Services.PluginInterface.LanguageChanged -= OnLanguageChanged;

        return Services.ReflexManager.DisposeAsync();
    }

    private void OnCommand(string command, string arguments)
    {
        _configWindow?.Toggle();
    }

    private void OnLanguageChanged(string langCode)
    {
        _commandInfo?.HelpMessage = t("CommandHandlerHelpMessage");
    }

    public class PluginWindowSystem : WindowSystem, IDisposable
    {
        public PluginWindowSystem() : base("XivReflex")
        {
            Services.PluginInterface.UiBuilder.Draw += Draw;
        }

        public void Dispose()
        {
            Services.PluginInterface.UiBuilder.Draw -= Draw;
            RemoveAllWindows();
        }
    }
}
