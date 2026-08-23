/*
 * Copyright (c) 2020, NVIDIA CORPORATION.  All rights reserved.
 *
 * NVIDIA CORPORATION and its licensors retain all intellectual property
 * and proprietary rights in and to this software, related documentation
 * and any modifications thereto.  Any use, reproduction, disclosure or
 * distribution of this software and related documentation without an express
 * license agreement from NVIDIA CORPORATION is strictly prohibited.
 */

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using InteropGenerator.Runtime.Attributes;

namespace XivReflex;

public static unsafe class NvApiNative
{
    // https://github.com/NVIDIA/nvapi/blob/cd6918f/nvapi_lite_common.h#L203
    public const int MAX_PHYSICAL_GPUS = 64;

    private static delegate* unmanaged<void*, void*, NvAPI_Status> CachedSetLatencyMarker;
    private static delegate* unmanaged<void*, NvAPI_Status> CachedSleep;
    private static delegate* unmanaged<void*, NV_SET_SLEEP_MODE_PARAMS_V1*, NvAPI_Status> CachedSetSleepMode;
    private static delegate* unmanaged<void**, uint*, NvAPI_Status> CachedEnumPhysicalGPUs;

    public static class Addresses
    {
        public static nint GetOrInitNvAPI;
        public static nint GetQueryInterfaceAddress;
    }

    public static class MemberFunctionPointers
    {
        public static delegate* unmanaged<void**, NvAPI_Status> GetOrInitNvAPI => (delegate* unmanaged<void**, NvAPI_Status>)Addresses.GetOrInitNvAPI;
    }

    public static void Initialize()
    {
        Addresses.GetOrInitNvAPI = Services.SigScanner.ScanText("E8 ?? ?? ?? ?? 89 44 24 ?? 83 7C 24 ?? ?? 75 ??");
        Services.PluginLog.Debug($"GetOrInitNvAPI: {Addresses.GetOrInitNvAPI:X}");
        Addresses.GetQueryInterfaceAddress = Services.SigScanner.GetStaticAddressFromSig("FF 15 ?? ?? ?? ?? 48 89 05 ?? ?? ?? ?? B9 ?? ??");
        Services.PluginLog.Debug($"GetQueryInterfaceAddress: {Addresses.GetQueryInterfaceAddress:X}");
    }

    public static NvAPI_Status GetOrInitNvAPI(void** ptr)
    {
        if (MemberFunctionPointers.GetOrInitNvAPI is null)
            throw new InvalidOperationException("Address for GetOrInitNvAPI is null.");
        return MemberFunctionPointers.GetOrInitNvAPI(ptr);
    }

    public static void* QueryInterface(NvAPI_InterfaceFunction funcId)
    {
        if (Addresses.GetQueryInterfaceAddress == 0)
            return null;

        var fn = *(delegate* unmanaged<uint, void*>*)Addresses.GetQueryInterfaceAddress;
        if (fn == null)
            return null;

        return fn((uint)funcId);
    }

    public static NvAPI_Status D3D_SetLatencyMarker(void* pDev, void* pGetLatencyParams)
    {
        var fn = CachedSetLatencyMarker;
        if (fn == null)
            fn = CachedSetLatencyMarker = (delegate* unmanaged<void*, void*, NvAPI_Status>)QueryInterface(NvAPI_InterfaceFunction.D3D_SetLatencyMarker);

        if (fn == null)
            return NvAPI_Status.NVAPI_INVALID_POINTER;

        return fn(pDev, pGetLatencyParams);
    }

    public static NvAPI_Status D3D_Sleep(void* pDev)
    {
        var fn = CachedSleep;

        if (fn == null)
            fn = CachedSleep = (delegate* unmanaged<void*, NvAPI_Status>)QueryInterface(NvAPI_InterfaceFunction.D3D_Sleep);

        if (fn == null)
            return NvAPI_Status.NVAPI_INVALID_POINTER;

        return fn(pDev);
    }

    public static NvAPI_Status D3D_SetSleepMode(void* pDev, NV_SET_SLEEP_MODE_PARAMS_V1* pSetSleepModeParams)
    {
        var fn = CachedSetSleepMode;

        if (fn == null)
            fn = CachedSetSleepMode = (delegate* unmanaged<void*, NV_SET_SLEEP_MODE_PARAMS_V1*, NvAPI_Status>)QueryInterface(NvAPI_InterfaceFunction.D3D_SetSleepMode);

        if (fn == null)
            return NvAPI_Status.NVAPI_INVALID_POINTER;

        return fn(pDev, pSetSleepModeParams);
    }

    public static NvAPI_Status EnumPhysicalGPUs(void** nvGPUHandle, uint* pGpuCount)
    {
        var fn = CachedEnumPhysicalGPUs;

        if (fn == null)
            fn = CachedEnumPhysicalGPUs = (delegate* unmanaged<void**, uint*, NvAPI_Status>)QueryInterface(NvAPI_InterfaceFunction.EnumPhysicalGPUs);

        if (fn == null)
            return NvAPI_Status.NVAPI_INVALID_POINTER;

        return fn(nvGPUHandle, pGpuCount);
    }

    public static uint MakeVersion(int structsize, ushort ver)
    {
        return (uint)structsize | (uint)(ver << 16);
    }
}

// https://github.com/NVIDIA/nvapi/blob/cd6918f/nvapi_interface.h#L31
public enum NvAPI_InterfaceFunction : uint
{
    EnumPhysicalGPUs = 0xe5ac921f,
    D3D_SetSleepMode = 0xac1ca9e0,
    D3D_Sleep = 0x852cd1d2,
    D3D_SetLatencyMarker = 0xd9984c05,
}

// https://github.com/NVIDIA/nvapi/blob/cd6918f/nvapi_lite_common.h#L264
public enum NvAPI_Status
{
    /// <summary> Success. Request is completed. </summary>
    NVAPI_OK = 0,

    /// <summary> An invalid pointer, usually NULL, was passed as a parameter </summary>
    NVAPI_INVALID_POINTER = -14,

    /// <summary> The requested action cannot be performed in the current state. </summary>
    NVAPI_INVALID_CONFIGURATION = -180,
}

// https://github.com/NVIDIA/nvapi/blob/cd6918f/nvapi.h#L18493
public enum NV_LATENCY_MARKER_TYPE
{
    SIMULATION_START = 0,
    SIMULATION_END = 1,
    RENDERSUBMIT_START = 2,
    RENDERSUBMIT_END = 3,
    PRESENT_START = 4,
    PRESENT_END = 5,
    // INPUT_SAMPLE = 6, // (Deprecated)
    TRIGGER_FLASH = 7,
    PC_LATENCY_PING = 8,
}

// https://github.com/NVIDIA/nvapi/blob/cd6918f/nvapi.h#L18514
[StructLayout(LayoutKind.Explicit, Size = 0x58)]
public struct NV_LATENCY_MARKER_PARAMS_V1
{
    public const int StructSize = 0x58;

    public static uint StructVersion => NvApiNative.MakeVersion(StructSize, 1);

    [UnscopedRef] public Span<byte> Rsvd => _rsvd;

    [FieldOffset(0x00)] public uint Version;
    [FieldOffset(0x08)] public ulong FrameID;
    [FieldOffset(0x10)] public NV_LATENCY_MARKER_TYPE MarkerType;
    [FieldOffset(0x18), FixedSizeArray] internal FixedSizeArray64<byte> _rsvd;
}

// https://github.com/NVIDIA/nvapi/blob/cd6918f/nvapi.h#L18298
[StructLayout(LayoutKind.Explicit, Size = 0x2C)]
public struct NV_SET_SLEEP_MODE_PARAMS_V1
{
    public const int StructSize = 0x2C;

    public static uint StructVersion => NvApiNative.MakeVersion(StructSize, 1);

    [UnscopedRef] public Span<byte> Rsvd => _rsvd;

    [FieldOffset(0x00)] public uint Version;
    [FieldOffset(0x04)] public bool LowLatencyMode;
    [FieldOffset(0x05)] public bool LowLatencyBoost;
    [FieldOffset(0x08)] public uint MinimumIntervalUs;
    [FieldOffset(0x0C)] public bool UseMarkersToOptimize;
    [FieldOffset(0x0D), FixedSizeArray] internal FixedSizeArray31<byte> _rsvd;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
[InlineArray(31)]
internal struct FixedSizeArray31<T> where T : unmanaged
{
    private T _element0;
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
[InlineArray(64)]
internal struct FixedSizeArray64<T> where T : unmanaged
{
    private T _element0;
}
