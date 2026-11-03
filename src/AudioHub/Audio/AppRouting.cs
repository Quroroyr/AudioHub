using System;
using AudioHub.Audio.Interop;

namespace AudioHub.Audio;

/// <summary>Windows' persisted per-application playback and recording preferences.</summary>
public sealed class AppRouting : IAppRouting
{
    private const string DevicePrefix = @"\\?\SWD#MMDEVAPI#";
    private const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    private const string CaptureSuffix = "#{2eef81be-33fa-4800-9670-1cd474972c3f}";

    public AppRouting()
    {
        using var policy = AudioPolicyConfig.TryCreate();
        IsSupported = policy is not null;
    }

    public bool IsSupported { get; }

    public string? GetDevice(int processId, DataFlow flow)
    {
        if (!IsSupported || processId <= 0 || !IsValidFlow(flow))
            return null;

        using var policy = AudioPolicyConfig.TryCreate();
        if (policy is null)
            return null;

        int hr = policy.Get((uint)processId, flow, AudioPolicyRole.Console, out var endpoint);
        if (hr < 0)
            return null;
        if (string.IsNullOrEmpty(endpoint))
        {
            hr = policy.Get((uint)processId, flow, AudioPolicyRole.Multimedia, out endpoint);
            if (hr < 0)
                return null;
        }

        if (string.IsNullOrEmpty(endpoint))
            return null;

        string suffix = flow == DataFlow.Render ? RenderSuffix : CaptureSuffix;
        if (!endpoint.StartsWith(DevicePrefix, StringComparison.OrdinalIgnoreCase) ||
            !endpoint.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return null;

        int length = endpoint.Length - DevicePrefix.Length - suffix.Length;
        return length > 0 ? endpoint.Substring(DevicePrefix.Length, length) : null;
    }

    public void SetDevice(int processId, DataFlow flow, string? deviceId)
    {
        if (!IsSupported)
            return;
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId), "Process ID must be positive.");
        if (!IsValidFlow(flow))
            throw new ArgumentOutOfRangeException(nameof(flow));

        using var policy = AudioPolicyConfig.TryCreate();
        if (policy is null)
            return;

        string? endpoint = string.IsNullOrEmpty(deviceId) ? null :
            DevicePrefix + deviceId + (flow == DataFlow.Render ? RenderSuffix : CaptureSuffix);

        // Attempt both roles, including when the first call fails (especially on reset).
        int consoleHr = policy.Set((uint)processId, flow, AudioPolicyRole.Console, endpoint);
        int multimediaHr = policy.Set((uint)processId, flow, AudioPolicyRole.Multimedia, endpoint);
        if (consoleHr < 0 || multimediaHr < 0)
        {
            throw new InvalidOperationException(
                $"Не удалось {(endpoint is null ? "сбросить" : "назначить")} устройство {flow} " +
                $"для процесса {processId}. Windows AudioPolicyConfig: " +
                $"eConsole HRESULT=0x{consoleHr:X8}, eMultimedia HRESULT=0x{multimediaHr:X8}.");
        }
    }

    private static bool IsValidFlow(DataFlow flow) => flow is DataFlow.Render or DataFlow.Capture;
}
