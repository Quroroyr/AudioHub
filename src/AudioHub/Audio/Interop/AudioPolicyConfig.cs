using System;
using System.Runtime.InteropServices;

namespace AudioHub.Audio.Interop;

internal enum AudioPolicyRole
{
    Console = 0,
    Multimedia = 1,
}

// This object is deliberately acquired and disposed on the calling thread for each
// operation: it owns both a COM reference and a balanced RoInitialize call.
internal sealed unsafe class AudioPolicyConfig : IDisposable
{
    private static readonly Guid ModernIid = new("ab3d4648-e242-459f-b02f-541c70306324");
    private static readonly Guid DownlevelIid = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");
    private const int RpcEChangedMode = unchecked((int)0x80010106);
    private nint _factory;
    private readonly bool _uninitialize;

    // ABI verified against both EarTrumpet variants:
    // https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IAudioPolicyConfigFactoryVariantFor21H2.cs
    // https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Interop/MMDeviceAPI/IAudioPolicyConfigFactoryVariantForDownlevel.cs
    // Slots 0..2 = IUnknown, 3..5 = IInspectable, 6..24 = 19 incomplete methods:
    // add_CtxVolumeChange, remove_CtxVolumeChanged, add_RingerVibrateStateChanged,
    // remove_RingerVibrateStateChange, SetVolumeGroupGainForId, GetVolumeGroupGainForId,
    // GetActiveVolumeGroupForEndpointId, GetVolumeGroupsForEndpoint, GetCurrentVolumeContext,
    // SetVolumeGroupMuteForId, GetVolumeGroupMuteForId, SetRingerVibrateState,
    // GetRingerVibrateState, SetPreferredChatApplication, ResetPreferredChatApplication,
    // GetPreferredChatApplication, GetCurrentChatApplications, add_ChatContextChanged,
    // remove_ChatContextChanged. Do not remove these slots from the offset calculation.
    private const int SetSlot = 25;
    private const int GetSlot = 26;
    private const int ClearSlot = 27;

    private AudioPolicyConfig(nint factory, bool uninitialize)
    {
        _factory = factory;
        _uninitialize = uninitialize;
    }

    internal static AudioPolicyConfig? TryCreate()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        int hr = RoInitialize(1); // RO_INIT_MULTITHREADED; an existing STA is also fine.
        if (hr < 0 && hr != RpcEChangedMode)
            return null;

        bool uninitialize = hr >= 0;
        nint className = 0;
        nint factory = 0;
        bool transferred = false;
        try
        {
            const string runtimeClass = "Windows.Media.Internal.AudioPolicyConfig";
            if (WindowsCreateString(runtimeClass, (uint)runtimeClass.Length, out className) < 0)
                return null;

            bool modern = Environment.OSVersion.Version.Build >= 21390;
            Guid first = modern ? ModernIid : DownlevelIid;
            Guid second = modern ? DownlevelIid : ModernIid;
            hr = RoGetActivationFactory(className, in first, out factory);
            if (hr < 0 || factory == 0)
            {
                if (factory != 0)
                    Marshal.Release(factory);
                factory = 0;
                hr = RoGetActivationFactory(className, in second, out factory);
            }

            if (hr < 0 || factory == 0)
                return null;

            var result = new AudioPolicyConfig(factory, uninitialize);
            transferred = true;
            return result;
        }
        finally
        {
            WindowsDeleteString(className);
            if (!transferred)
            {
                if (factory != 0)
                    Marshal.Release(factory);
                if (uninitialize)
                    RoUninitialize();
            }
        }
    }

    internal int Set(uint processId, DataFlow flow, AudioPolicyRole role, string? endpoint)
    {
        nint device = 0;
        try
        {
            if (!string.IsNullOrEmpty(endpoint))
            {
                int hr = WindowsCreateString(endpoint, (uint)endpoint.Length, out device);
                if (hr < 0)
                    return hr;
            }

            var method = (delegate* unmanaged[Stdcall]<nint, uint, DataFlow, AudioPolicyRole, nint, int>)Vtable[SetSlot];
            return method(_factory, processId, flow, role, device);
        }
        finally
        {
            WindowsDeleteString(device);
        }
    }

    internal int Get(uint processId, DataFlow flow, AudioPolicyRole role, out string? endpoint)
    {
        endpoint = null;
        nint device = 0;
        try
        {
            var method = (delegate* unmanaged[Stdcall]<nint, uint, DataFlow, AudioPolicyRole, nint*, int>)Vtable[GetSlot];
            int hr = method(_factory, processId, flow, role, &device);
            if (hr >= 0 && device != 0)
            {
                char* buffer = WindowsGetStringRawBuffer(device, out uint length);
                if (buffer != null && length > 0)
                    endpoint = new string(buffer, 0, checked((int)length));
            }
            return hr;
        }
        finally
        {
            WindowsDeleteString(device);
        }
    }

    // Kept for the complete relevant ABI; per-process reset must use Set(null),
    // never this global operation, which would clear other applications' choices.
    internal int ClearAllPersistedApplicationDefaultEndpoints()
    {
        var method = (delegate* unmanaged[Stdcall]<nint, int>)Vtable[ClearSlot];
        return method(_factory);
    }

    private nint* Vtable => _factory != 0 ? *(nint**)_factory :
        throw new ObjectDisposedException(nameof(AudioPolicyConfig));

    public void Dispose()
    {
        if (_factory == 0)
            return;
        Marshal.Release(_factory);
        _factory = 0;
        if (_uninitialize)
            RoUninitialize();
    }

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoInitialize(uint initType);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern void RoUninitialize();

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint classId, in Guid iid, out nint factory);

    [DllImport("combase.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, uint length, out nint value);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint value);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern char* WindowsGetStringRawBuffer(nint value, out uint length);
}
