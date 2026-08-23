using System;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace XivReflex;

public class Plugin(IDalamudPluginInterface pluginInterface) : IAsyncDalamudPlugin
{
    private PluginWindowSystem? _windowSystem;
    private ConfigWindow? _configWindow;

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        Services.Initialize(pluginInterface);

        _windowSystem = new PluginWindowSystem();
        _configWindow = new ConfigWindow();
        _windowSystem.AddWindow(_configWindow);

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

        return Services.ReflexManager.DisposeAsync();
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
