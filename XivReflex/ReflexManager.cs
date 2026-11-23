using System;
using System.Threading.Tasks;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.Input;

namespace XivReflex;

public class ReflexManager : IAsyncDisposable
{
    private bool _reflexEnabled;
    private ulong _framesDrawn;
    private NvAPI_Status _lastStatus = NvAPI_Status.NVAPI_INVALID_CONFIGURATION;

    private Hook<TaskManager.Delegates.ExecuteAllTasks>? _runAllTasksHook;
    private Hook<ImmediateContext.Delegates.ProcessCommands>? _processCommandsHook;
    private Hook<SwapChain.Delegates.Present>? _presentHook;
    private Hook<MouseDevice.Delegates.ProcessMouseInputMessage>? _mouseMessageHandlerHook;

    public NvAPI_Status InitStatus { get; }

    public unsafe ReflexManager()
    {
        NvApiNative.Initialize();

        void* nvapiLibrary = null;
        InitStatus = NvApiNative.GetOrInitNvAPI(&nvapiLibrary);

        if (InitStatus != NvAPI_Status.NVAPI_OK)
        {
            Services.PluginLog.Error("NVAPI not initialized. Status code: {status}", InitStatus);
            return;
        }

        uint gpuCount = 0;
        var nvGPUHandles = stackalloc void*[NvApiNative.MAX_PHYSICAL_GPUS];
        NvApiNative.EnumPhysicalGPUs(nvGPUHandles, &gpuCount);
        if (gpuCount == 0)
        {
            Services.PluginLog.Error("Did not find any NVIDIA GPU");
            return;
        }

        Services.PluginLog.Information("NVIDIA GPU found, using NVAPI");

        _runAllTasksHook = Services.GameInteropProvider.HookFromAddress<TaskManager.Delegates.ExecuteAllTasks>(
            TaskManager.Addresses.ExecuteAllTasks.Value,
            RunAllTasksDetour);

        _processCommandsHook = Services.GameInteropProvider.HookFromAddress<ImmediateContext.Delegates.ProcessCommands>(
            ImmediateContext.Addresses.ProcessCommands.Value,
            ProcessCommandsDetour);

        _presentHook = Services.GameInteropProvider.HookFromAddress<SwapChain.Delegates.Present>(
            SwapChain.Addresses.Present.Value,
            PresentDetour);

        _mouseMessageHandlerHook = Services.GameInteropProvider.HookFromAddress<MouseDevice.Delegates.ProcessMouseInputMessage>(
            MouseDevice.Addresses.ProcessMouseInputMessage.Value,
            MouseMessageHandlerDetour);

        Services.Framework.Run(() =>
        {
            _runAllTasksHook.Enable();
            _processCommandsHook.Enable();
            _presentHook.Enable();
            _mouseMessageHandlerHook.Enable();

            Services.PluginLog.Information("Hooks enabled");

            PCLStats.Init();

            var config = Services.Config;
            SetSleepMode(
                config.LowLatencyMode,
                config.LowLatencyBoost,
                config.UseFPSLimit,
                config.FpsLimit,
                config.UseMarkersToOptimize);
        });
    }

    public ValueTask DisposeAsync()
    {
        return new ValueTask(Services.Framework.Run(() =>
        {
            SetSleepMode(false);

            _mouseMessageHandlerHook?.Dispose();
            _mouseMessageHandlerHook = null;
            _presentHook?.Dispose();
            _presentHook = null;
            _processCommandsHook?.Dispose();
            _processCommandsHook = null;
            _runAllTasksHook?.Dispose();
            _runAllTasksHook = null;

            PCLStats.Shutdown();
        }));
    }

    private unsafe void RunAllTasksDetour(TaskManager* thisPtr, float* dt)
    {
        var reflexEnabled = _reflexEnabled;
        if (reflexEnabled)
        {
            NvApiNative.D3D_Sleep(Services.PluginInterface.UiBuilder.DeviceHandle.ToPointer());
            SetLatencyMarker(NV_LATENCY_MARKER_TYPE.SIMULATION_START);
        }

        _runAllTasksHook!.OriginalDisposeSafe(thisPtr, dt);

        if (reflexEnabled)
        {
            SetLatencyMarker(NV_LATENCY_MARKER_TYPE.SIMULATION_END);
        }
    }

    private unsafe void ProcessCommandsDetour(ImmediateContext* thisPtr, RenderCommandBufferGroup* renderCommands, uint renderCommandCount)
    {
        var reflexEnabled = _reflexEnabled;
        if (reflexEnabled)
        {
            SetLatencyMarker(NV_LATENCY_MARKER_TYPE.RENDERSUBMIT_START);
        }

        _processCommandsHook!.OriginalDisposeSafe(thisPtr, renderCommands, renderCommandCount);

        if (reflexEnabled)
        {
            SetLatencyMarker(NV_LATENCY_MARKER_TYPE.RENDERSUBMIT_END);
        }
    }

    private unsafe void PresentDetour(SwapChain* thisPtr)
    {
        var reflexEnabled = _reflexEnabled;
        if (reflexEnabled)
        {
            SetLatencyMarker(NV_LATENCY_MARKER_TYPE.PRESENT_START);
        }

        _presentHook!.OriginalDisposeSafe(thisPtr);

        if (reflexEnabled)
        {
            SetLatencyMarker(NV_LATENCY_MARKER_TYPE.PRESENT_END);
        }
    }

    private void MouseMessageHandlerDetour(nint hwnd, uint uMsg, nint wParam)
    {
        _mouseMessageHandlerHook!.OriginalDisposeSafe(hwnd, uMsg, wParam);

        var reflexEnabled = _reflexEnabled;
        if (!reflexEnabled)
            return;

        if (uMsg == 0x201) // WM_LBUTTONDOWN
        {
            SetLatencyMarker(NV_LATENCY_MARKER_TYPE.TRIGGER_FLASH);
        }

        if (PCLStats.IsPingMsgId(uMsg))
        {
            SetLatencyMarker(NV_LATENCY_MARKER_TYPE.PC_LATENCY_PING);
        }
    }

    public unsafe void SetSleepMode(bool lowLatencyMode, bool lowLatencyBoost = false, bool useFpsLimit = false, float fpsLimit = 0f, bool useMarkersToOptimize = false)
    {
        var minimumIntervalUs = useFpsLimit ? (uint)(1000.0f / fpsLimit * 1000.0f) : 0;

        var sleepModeParams = new NV_SET_SLEEP_MODE_PARAMS_V1()
        {
            Version = NV_SET_SLEEP_MODE_PARAMS_V1.StructVersion,
            LowLatencyMode = lowLatencyMode,
            LowLatencyBoost = lowLatencyBoost,
            MinimumIntervalUs = minimumIntervalUs,
            UseMarkersToOptimize = false, // TODO: "Only works with bLowLatencyBoost enabled. Enable bUseMarkersToOptimize if using latency markers and true is found beneficial to latency"
        };
        sleepModeParams.Rsvd.Clear();

        var status = NvApiNative.D3D_SetSleepMode(Services.PluginInterface.UiBuilder.DeviceHandle.ToPointer(), &sleepModeParams);
        _reflexEnabled = status == NvAPI_Status.NVAPI_OK;

        if (status != _lastStatus)
        {
            if (_reflexEnabled)
            {
                Services.PluginLog.Information("Reflex enabled, returned status code {status}", status);
            }
            else
            {
                Services.PluginLog.Warning("Reflex not enabled, returned status code {status}", status);
            }

            _lastStatus = status;
        }
    }

    private unsafe bool SetLatencyMarker(NV_LATENCY_MARKER_TYPE marker)
    {
        PCLStats.Marker((PclStatsLatencyMarkerType)marker, _framesDrawn);

        if (InitStatus != NvAPI_Status.NVAPI_OK)
            return false;

        if (marker == NV_LATENCY_MARKER_TYPE.PRESENT_END)
            _framesDrawn++;

        var markerParams = new NV_LATENCY_MARKER_PARAMS_V1
        {
            Version = NV_LATENCY_MARKER_PARAMS_V1.StructVersion,
            MarkerType = marker,
            FrameID = _framesDrawn
        };
        markerParams.Rsvd.Clear();

        var ret = NvApiNative.D3D_SetLatencyMarker(Services.PluginInterface.UiBuilder.DeviceHandle.ToPointer(), &markerParams);
        return ret == NvAPI_Status.NVAPI_OK;
    }
}
