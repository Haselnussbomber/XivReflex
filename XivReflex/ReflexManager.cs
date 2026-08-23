using System;
using System.Diagnostics;
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

    private Hook<RunAllTasksDelegate>? _runAllTasksHook;
    private Hook<ImmediateContext.Delegates.ProcessCommands>? _processCommandsHook;
    private Hook<PresentDelegate>? _presentHook;
    private Hook<MouseMessageHandlerDelegate>? _mouseMessageHandlerHook;
    private Hook<InputUpdateDelegate>? _inputUpdateHook;

    private unsafe delegate void PrePostTickDelegate(Device* thisPtr);
    private unsafe delegate void RunAllTasksDelegate(TaskManager* thisPtr, void* userData);
    private unsafe delegate void PresentDelegate(SwapChain* thisPtr);
    private unsafe delegate void MouseMessageHandlerDelegate(void* hwnd, int uMsg, int wParam);
    private unsafe delegate void InputUpdateDelegate(InputDeviceManager* thisPtr, float frameDeltaTime, GamepadInputData* outGamepadInputs, CursorInputData* outCursorInputs, KeyboardInputData* outKeyboardInputs);

    public NvAPI_Status InitStatus { get; private set; }
    public TimeSpan SleepDuration { get; private set; } = TimeSpan.Zero;

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
            _inputUpdateHook = Services.GameInteropProvider.HookFromSignature<InputUpdateDelegate>("E8 ?? ?? ?? ?? 83 7B ?? 00 75 ?? 48 8B CF", InputUpdateDetour);
        }

        Services.Framework.Run(() =>
        {
            _runAllTasksHook.Enable();
            _processCommandsHook.Enable();
            _presentHook.Enable();
            _mouseMessageHandlerHook.Enable();
            _inputUpdateHook.Enable();

            // ReflexEtwProvider.Log.PCLStatsInit();

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
            _inputUpdateHook?.Dispose();
            _inputUpdateHook = null;
            // ReflexEtwProvider.Log.PCLStatsShutdown();
            // ReflexEtwProvider.Log.Dispose();
        }));
    }

    private unsafe void RunAllTasksDetour(TaskManager* thisPtr, void* userData)
    {
        if (_reflexEnabled)
        {
            var sw = Stopwatch.StartNew();
            NvApiNative.D3D_Sleep(Services.PluginInterface.UiBuilder.DeviceHandle.ToPointer());
            sw.Stop();
            SleepDuration = sw.Elapsed;
        }

        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.SIMULATION_START);
        _runAllTasksHook!.OriginalDisposeSafe(thisPtr, userData);
        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.SIMULATION_END);
    }

    private unsafe void ProcessCommandsDetour(ImmediateContext* thisPtr, RenderCommandBufferGroup* renderCommands, uint renderCommandCount)
    {
        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.RENDERSUBMIT_START);
        _processCommandsHook!.OriginalDisposeSafe(thisPtr, renderCommands, renderCommandCount);
    }

    private unsafe void PresentDetour(SwapChain* thisPtr)
    {
        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.RENDERSUBMIT_END);
        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.PRESENT_START);
        _presentHook!.OriginalDisposeSafe(thisPtr);
        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.PRESENT_END);
    }

    private unsafe void MouseMessageHandlerDetour(void* hwnd, int uMsg, int wParam)
    {
        _mouseMessageHandlerHook!.OriginalDisposeSafe(hwnd, uMsg, wParam);

        if (uMsg == 0x201) // WM_LBUTTONDOWN
        {
            SetLatencyMarker(NV_LATENCY_MARKER_TYPE.TRIGGER_FLASH);
        }

        /*
        if (uMsg == PclStatsWindowMessage)
        {
            SetLatencyMarker(NV_LATENCY_MARKER_TYPE.PC_LATENCY_PING);
        }
        */
    }

    private unsafe void InputUpdateDetour(InputDeviceManager* thisPtr, float frameDeltaTime, GamepadInputData* outGamepadInputs, CursorInputData* outCursorInputs, KeyboardInputData* outKeyboardInputs)
    {
        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.INPUT_SAMPLE);
        SetLatencyMarker(NV_LATENCY_MARKER_TYPE.PC_LATENCY_PING); // here?
        _inputUpdateHook!.OriginalDisposeSafe(thisPtr, frameDeltaTime, outGamepadInputs, outCursorInputs, outKeyboardInputs);
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
        sleepModeParams.Rsvd.Fill(0);

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
        if (InitStatus != NvAPI_Status.NVAPI_OK)
            return false;

        var markerParams = new NV_LATENCY_MARKER_PARAMS_V1
        {
            Version = NV_LATENCY_MARKER_PARAMS_V1.StructVersion,
            MarkerType = marker,
            FrameID = _framesDrawn
        };
        markerParams.Rsvd.Fill(0);

        var ret = NvApiNative.D3D_SetLatencyMarker(Services.PluginInterface.UiBuilder.DeviceHandle.ToPointer(), &markerParams);

        // TODO: ReflexEtwProvider.Log.PCLStatsEvent((uint)marker, _framesDrawn);

        if (marker == NV_LATENCY_MARKER_TYPE.PRESENT_END)
            _framesDrawn++;

        return ret == NvAPI_Status.NVAPI_OK;
    }

}
