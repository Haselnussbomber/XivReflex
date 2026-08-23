using System;
using System.Threading.Tasks;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.System.Framework;

namespace XivReflex;

public class ReflexManager : IAsyncDisposable
{
    private bool _reflexEnabled;
    private ulong _framesDrawn;
    private NvAPI_Status _lastStatus = NvAPI_Status.NVAPI_INVALID_CONFIGURATION;

    private Hook<RunAllTasksDelegate>? _runAllTasksHook;
    private Hook<ImmediateContext.Delegates.ProcessCommands>? _processCommandsHook;
    private Hook<PresentDelegate>? _presentHook;
    private Hook<MouseMessageHandlerDelegate>? _mouseMessageHandlerHook;

    private unsafe delegate void PrePostTickDelegate(Device* thisPtr);
    private unsafe delegate void RunAllTasksDelegate(TaskManager* thisPtr, void* userData);
    private unsafe delegate void PresentDelegate(SwapChain* thisPtr);
    private unsafe delegate void MouseMessageHandlerDelegate(void* hwnd, uint uMsg, uint wParam);

    public NvAPI_Status InitStatus { get; }

    public ReflexManager()
    {
        unsafe
        {
            void* nvapiLibrary = null;
            InitStatus = NvApiNative.GetOrInitNvAPI(&nvapiLibrary);
        }

        if (InitStatus != NvAPI_Status.NVAPI_OK)
        {
            Services.PluginLog.Error("NVAPI not initialized. Status code: {status}", InitStatus);
            return;
        }

        uint gpuCount = 0;
        unsafe
        {
            var nvGPUHandles = stackalloc void*[NvApiNative.MAX_PHYSICAL_GPUS];
            NvApiNative.EnumPhysicalGPUs(nvGPUHandles, &gpuCount);
        }
        if (gpuCount == 0)
        {
            Services.PluginLog.Error("Did not find any NVIDIA GPU");
            return;
        }

        Services.PluginLog.Information("Found {gpuCount} NVIDIA GPUs, using NVAPI", gpuCount);
        // TODO: check for reflex support?

        unsafe
        {
            _runAllTasksHook = Services.GameInteropProvider.HookFromSignature<RunAllTasksDelegate>("E8 ?? ?? ?? ?? 48 8B 8B ?? ?? ?? ?? 48 85 C9 74 ?? F3 0F 10 8B", RunAllTasksDetour);
            _processCommandsHook = Services.GameInteropProvider.HookFromAddress<ImmediateContext.Delegates.ProcessCommands>(ImmediateContext.Addresses.ProcessCommands.Value, ProcessCommandsDetour);
            _mouseMessageHandlerHook = Services.GameInteropProvider.HookFromSignature<MouseMessageHandlerDelegate>("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC ?? 49 8B D8 C6 05", MouseMessageHandlerDetour);
            _presentHook = Services.GameInteropProvider.HookFromSignature<PresentDelegate>("E8 ?? ?? ?? ?? C6 46 ?? 00 48 8B 8E", PresentDetour);
        }

        Services.Framework.Run(() =>
        {
            _runAllTasksHook.Enable();
            _processCommandsHook.Enable();
            _presentHook.Enable();
            _mouseMessageHandlerHook.Enable();

            PCLStats.Init();

            Services.PluginLog.Information("Hooks enabled");

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

            _runAllTasksHook?.Dispose();
            _runAllTasksHook = null;
            _processCommandsHook?.Dispose();
            _processCommandsHook = null;
            _presentHook?.Dispose();
            _presentHook = null;
            _mouseMessageHandlerHook?.Dispose();
            _mouseMessageHandlerHook = null;
            PCLStats.Shutdown();
        }));
    }

    private unsafe void RunAllTasksDetour(TaskManager* thisPtr, void* userData)
    {
        if (_reflexEnabled)
            NvApiNative.D3D_Sleep(Services.PluginInterface.UiBuilder.DeviceHandle.ToPointer());

        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.SIMULATION_START);
        _runAllTasksHook!.OriginalDisposeSafe(thisPtr, userData);
        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.SIMULATION_END);
    }

    private unsafe void ProcessCommandsDetour(ImmediateContext* thisPtr, RenderCommandBufferGroup* renderCommands, uint renderCommandCount)
    {
        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.RENDERSUBMIT_START);
        _processCommandsHook!.OriginalDisposeSafe(thisPtr, renderCommands, renderCommandCount);
        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.RENDERSUBMIT_END);
    }

    private unsafe void PresentDetour(SwapChain* thisPtr)
    {
        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.PRESENT_START);
        _presentHook!.OriginalDisposeSafe(thisPtr);
        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.PRESENT_END);
    }

    private unsafe void MouseMessageHandlerDetour(void* hwnd, uint uMsg, uint wParam)
    {
        _mouseMessageHandlerHook!.OriginalDisposeSafe(hwnd, uMsg, wParam);

        if (uMsg == 0x201) // WM_LBUTTONDOWN
        {
            SetLatencyMarker(NV_LATENCY_MARKER_TYPE.TRIGGER_FLASH);
        }

        if (PCLStats.IsPingMsgId(uMsg))
        {
            SetLatencyMarker(NV_LATENCY_MARKER_TYPE.PC_LATENCY_PING);
        }
    }

    public void SetSleepMode(bool lowLatencyMode, bool lowLatencyBoost = false, bool useFpsLimit = false, float fpsLimit = 0f, bool useMarkersToOptimize = false)
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

        NvAPI_Status status;

        unsafe
        {
            status = NvApiNative.D3D_SetSleepMode(Services.PluginInterface.UiBuilder.DeviceHandle.ToPointer(), &sleepModeParams);
        }

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
