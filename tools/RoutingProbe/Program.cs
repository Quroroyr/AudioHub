using System.Runtime.InteropServices;
using System.Text;
using AudioHub.Audio;
using AudioHub.Audio.Interop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length == 1 && args[0] == "list")
            {
                ListDevices();
                return 0;
            }

            if (args.Length is < 2 or > 3 || !int.TryParse(args[1], out int processId) || processId <= 0 ||
                !((args[0] is "get" or "reset") && args.Length == 2 || args[0] == "set" && args.Length == 3))
            {
                Console.Error.WriteLine("Usage: RoutingProbe list | get <pid> | set <pid> <deviceId> | reset <pid>");
                return 2;
            }

            var routing = new AppRouting();
            Console.WriteLine($"Windows build: {Environment.OSVersion.Version.Build}; IsSupported: {routing.IsSupported}");
            if (!routing.IsSupported)
                return 3;

            switch (args[0])
            {
                case "set":
                    routing.SetDevice(processId, DataFlow.Render, args[2]);
                    Console.WriteLine($"Set Render for PID {processId}: {args[2]}");
                    break;
                case "reset":
                    // Always attempt both directions, even if the first reset fails.
                    var resetErrors = new List<string>();
                    foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
                    {
                        try
                        {
                            routing.SetDevice(processId, flow, null);
                        }
                        catch (InvalidOperationException exception)
                        {
                            resetErrors.Add(exception.Message);
                        }
                    }
                    if (resetErrors.Count > 0)
                        throw new InvalidOperationException(string.Join(Environment.NewLine, resetErrors));
                    Console.WriteLine($"Reset Render and Capture for PID {processId}");
                    break;
            }

            Console.WriteLine($"Render: {routing.GetDevice(processId, DataFlow.Render) ?? "null"}");
            Console.WriteLine($"Capture: {routing.GetDevice(processId, DataFlow.Capture) ?? "null"}");
            if (args[0] == "get")
                PrintNativePreferences(processId);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    // IAppRouting intentionally maps native Get failures to null. The diagnostic
    // probe also shows HRESULTs so that a failed read cannot look like a reset.
    private static void PrintNativePreferences(int processId)
    {
        using var policy = AudioPolicyConfig.TryCreate();
        if (policy is null)
            return;
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            foreach (var role in new[] { AudioPolicyRole.Console, AudioPolicyRole.Multimedia })
            {
                int hr = policy.Get((uint)processId, flow, role, out var endpoint);
                Console.WriteLine($"  {flow}/e{role}: HRESULT=0x{hr:X8}; endpoint={endpoint ?? "null"}");
            }
        }
    }

    private static void ListDevices()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        try
        {
            foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
            {
                var defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int role = 0; role <= 1; role++)
                {
                    int hr = enumerator.GetDefaultAudioEndpoint(flow, role, out var defaultDevice);
                    try
                    {
                        if (hr >= 0 && defaultDevice is not null)
                        {
                            Marshal.ThrowExceptionForHR(defaultDevice.GetId(out var defaultId));
                            defaults.Add(defaultId);
                            Console.WriteLine($"{flow} default {(role == 0 ? "eConsole" : "eMultimedia")}: {defaultId}");
                        }
                    }
                    finally
                    {
                        Release(defaultDevice);
                    }
                }

                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, 1, out var collection));
                try
                {
                    Marshal.ThrowExceptionForHR(collection.GetCount(out uint count));
                    Console.WriteLine($"{flow}: {count} active device(s)");
                    for (uint index = 0; index < count; index++)
                    {
                        Marshal.ThrowExceptionForHR(collection.Item(index, out var device));
                        try
                        {
                            Marshal.ThrowExceptionForHR(device.GetId(out var id));
                            Console.WriteLine($"  {id}{(defaults.Contains(id) ? " [default]" : "")}");
                            Console.WriteLine($"    {GetName(device)}");
                        }
                        finally
                        {
                            Release(device);
                        }
                    }
                }
                finally
                {
                    Release(collection);
                }
            }
        }
        finally
        {
            Release(enumerator);
        }
    }

    private static string GetName(IMMDevice device)
    {
        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out var store));
        try
        {
            var key = new PropertyKey { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), PropertyId = 14 };
            var value = default(PropVariant);
            try
            {
                Marshal.ThrowExceptionForHR(store.GetValue(in key, out value));
                return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "(unnamed)" : "(unnamed)";
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Release(store);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null)
            Marshal.ReleaseComObject(value);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public nint Pointer;
    }

    [ComImport, Guid("bcde0395-e52f-467c-8e3d-c4579291692e")]
    private class MMDeviceEnumerator { }

    [ComImport, Guid("a95664d2-9614-4f35-a746-de8db63617e6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(DataFlow flow, uint stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(DataFlow flow, int role, out IMMDevice? device);
    }

    [ComImport, Guid("0bd7a1be-7a1a-44db-8397-cc5392387b5e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("d666063f-1587-4e43-81f1-b948e807363f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(in Guid iid, uint context, nint activationParams, out nint instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(in PropertyKey key, out PropVariant value);
    }
}
