/*
 * Copyright (c) 2021-2022, NVIDIA CORPORATION.  All rights reserved.
 *
 * NVIDIA CORPORATION and its licensors retain all intellectual property
 * and proprietary rights in and to this software, related documentation
 * and any modifications thereto.  Any use, reproduction, disclosure or
 * distribution of this software and related documentation without an express
 * license agreement from NVIDIA CORPORATION is strictly prohibited.
 */

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.Threading;
using Windows.Win32;

namespace XivReflex;

public static class PCLStats
{
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const ushort VK_F13 = 0x7C;
    private const ushort VK_F14 = 0x7D;
    private const ushort VK_F15 = 0x7E;

    private static ushort VirtualKey;
    private static uint IdThread;
    private static ManualResetEvent? QuitEvent;
    private static Thread? PingThread;

    public static uint WindowMessage { get; private set; }
    public static PclStatsFlags Flags { get; private set; }

    // PCLSTATS_INIT
    public static void Init(PclStatsFlags flags = PclStatsFlags.None)
    {
        Flags = flags;

        WindowMessage = PInvoke.RegisterWindowMessage("PC_Latency_Stats_Ping");
        QuitEvent = new ManualResetEvent(false);

        PCLStatsProvider.Log.PCLStatsInit();

        PingThread = new Thread(PingThreadProc) { IsBackground = true };
        PingThread.Start();
    }

    // PCLSTATS_MARKER
    public static void Marker(PclStatsLatencyMarkerType marker, ulong frameId)
    {
        PCLStatsProvider.Log.PCLStatsEvent((uint)marker, frameId);
    }

    // PCLSTATS_MARKER_V2
    public static void MarkerV2(PclStatsLatencyMarkerType marker, ulong frameId)
    {
        PCLStatsProvider.Log.PCLStatsEventV2((uint)marker, frameId, (uint)Flags);
    }

    // PCLSTATS_SHUTDOWN
    public static void Shutdown()
    {
        if (PingThread != null)
        {
            QuitEvent?.Set();
            PingThread.Join(1000);
            PingThread = null;
        }

        QuitEvent?.Dispose();
        QuitEvent = null;

        PCLStatsProvider.Log.PCLStatsShutdown();
        PCLStatsProvider.Log.Dispose();
    }

    // PCLSTATS_IS_PING_MSG_ID
    public static bool IsPingMsgId(uint msgId)
    {
        return msgId == WindowMessage;
    }

    // PCLSTATS_SET_ID_THREAD
    public static void SetIdThread(uint idThread)
    {
        IdThread = idThread;
    }

    // PCLSTATS_SET_VIRTUAL_KEY
    public static void SetVirtualKey(ushort vk)
    {
        VirtualKey = vk;
    }

    // The ping thread procedure logic translated to C#
    private static void PingThreadProc()
    {
        var rand = new Random();
        const int minPingInterval = 100;
        const int maxPingInterval = 300;

        while (QuitEvent?.WaitOne(minPingInterval + rand.Next(maxPingInterval - minPingInterval)) == false)
        {
            if (!PCLStatsProvider.Log.IsEnabled())
                continue;

            if (IdThread != 0)
            {
                PCLStatsProvider.Log.PCLStatsInputThread(IdThread);
                PInvoke.PostThreadMessage(IdThread, WindowMessage, 0, 0);
                continue;
            }

            var hWnd = PInvoke.GetForegroundWindow();
            if (hWnd != 0)
            {
                PInvoke.GetWindowThreadProcessId(hWnd, out var processId);

                if (Environment.ProcessId == processId)
                {
                    if (VirtualKey == VK_F13 || VirtualKey == VK_F14 || VirtualKey == VK_F15)
                    {
                        PCLStatsProvider.Log.PCLStatsInputKey(VirtualKey);
                        PInvoke.PostMessage(hWnd, WM_KEYDOWN, VirtualKey, 0x00000001);
                        PInvoke.PostMessage(hWnd, WM_KEYUP, VirtualKey, unchecked((nint)0xC0000001));
                    }
                    else if (WindowMessage != 0)
                    {
                        PCLStatsProvider.Log.PCLStatsInputMsg(WindowMessage);
                        PInvoke.PostMessage(hWnd, WindowMessage, 0, 0);
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }
    }
}

[EventSource(Name = "PCLStatsTraceLoggingProvider", Guid = "0d216f06-82a6-4d49-bc4f-8f38ae56efab")]
[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "It's required.")]
public sealed class PCLStatsProvider : EventSource
{
    public static readonly PCLStatsProvider Log = new();

    public PCLStatsProvider() : base(EventSourceSettings.EtwSelfDescribingEventFormat)
    {
    }

    [Event(1)]
    public void PCLStatsInit()
    {
        WriteEvent(1);
    }

    [Event(2)]
    public void PCLStatsEvent(uint Marker, ulong FrameID)
    {
        WriteEvent(2, Marker, FrameID);
    }

    [Event(4)]
    public void PCLStatsShutdown()
    {
        WriteEvent(4);
    }

    [Event(5)]
    public void PCLStatsFlags(uint Flags)
    {
        WriteEvent(5, Flags);
    }

    [Event(6)]
    public void PCLStatsEventV2(uint Marker, ulong FrameID, uint Flags)
    {
        WriteEvent(6, Marker, FrameID, Flags);
    }

    [NonEvent]
    public void PCLStatsInputThread(uint IdThread)
    {
        Write("PCLStatsInput", new { IdThread });
    }

    [NonEvent]
    public void PCLStatsInputKey(uint VirtualKey)
    {
        Write("PCLStatsInput", new { VirtualKey });
    }

    [NonEvent]
    public void PCLStatsInputMsg(uint MsgId)
    {
        Write("PCLStatsInput", new { MsgId });
    }

    protected override void OnEventCommand(EventCommandEventArgs command)
    {
        base.OnEventCommand(command);

        if (command.Command == EventCommand.SendManifest || command.Command == EventCommand.Update || command.Command == EventCommand.Enable)
        {
            PCLStatsFlags((uint)PCLStats.Flags);
        }
    }
}

public enum PclStatsLatencyMarkerType : uint
{
    SimulationStart = 0,
    SimulationEnd = 1,
    RenderSubmitStart = 2,
    RenderSubmitEnd = 3,
    PresentStart = 4,
    PresentEnd = 5,
    // InputSample = 6, (Deprecated)
    TriggerFlash = 7,
    PcLatencyPing = 8,
    OutOfBandRenderSubmitStart = 9,
    OutOfBandRenderSubmitEnd = 10,
    OutOfBandPresentStart = 11,
    OutOfBandPresentEnd = 12,
    ControllerInputSample = 13,
}

[Flags]
public enum PclStatsFlags : uint
{
    None = 0,
    NoPresentMarkers = 0x00000001,
}
