using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AudioHub.Audio.Interop;

namespace AudioHub.Audio;

/// <summary>Звуковой поток одной программы на одном устройстве.</summary>
public sealed class AudioSession : IDisposable
{
    private readonly IAudioSessionControl2 _control;
    private readonly ISimpleAudioVolume _volume;
    private readonly IAudioMeterInformation? _meter;

    private AudioSession(IAudioSessionControl2 control, AudioDevice device, int processId, bool isSystemSounds, string instanceId)
    {
        _control = control;
        _volume = (ISimpleAudioVolume)control;
        _meter = control as IAudioMeterInformation;
        Device = device;
        ProcessId = processId;
        IsSystemSounds = isSystemSounds;
        InstanceId = instanceId;
    }

    public AudioDevice Device { get; }

    public int ProcessId { get; }

    public bool IsSystemSounds { get; }

    /// <summary>Уникален для потока, стабилен между перечислениями.</summary>
    public string InstanceId { get; }

    public float Volume
    {
        get
        {
            _volume.GetMasterVolume(out var level);
            return level;
        }
        set
        {
            var context = Guid.Empty;
            _volume.SetMasterVolume(Math.Clamp(value, 0f, 1f), ref context);
        }
    }

    public bool IsMuted
    {
        get
        {
            _volume.GetMute(out var muted);
            return muted;
        }
        set
        {
            var context = Guid.Empty;
            _volume.SetMute(value, ref context);
        }
    }

    public float Peak => _meter is not null && _meter.GetPeakValue(out var peak) == 0 ? peak : 0;

    public bool IsExpired
    {
        get
        {
            try
            {
                _control.GetState(out var state);
                return state == AudioSessionState.Expired;
            }
            catch (COMException)
            {
                return true;
            }
        }
    }

    public string? DisplayName
    {
        get
        {
            try
            {
                _control.GetDisplayName(out var name);
                // Встречаются ресурсные ссылки вида "@%SystemRoot%\System32\AudioSrv.Dll,-202" — их не показываем.
                return string.IsNullOrWhiteSpace(name) || name.StartsWith('@') ? null : name;
            }
            catch (COMException)
            {
                return null;
            }
        }
    }

    /// <summary>Последняя ошибка разбора потока — для диагностики (--dump).</summary>
    internal static Exception? LastError { get; private set; }

    internal static AudioSession? TryCreate(IAudioSessionControl2 control, AudioDevice device)
    {
        try
        {
            control.GetState(out var state);
            if (state == AudioSessionState.Expired) return null;

            control.GetProcessId(out var pid);
            control.GetSessionInstanceIdentifier(out var instanceId);
            var isSystem = control.IsSystemSoundsSession() == 0;
            if (!isSystem && !ProcessInfo.IsAlive((int)pid)) return null;

            return new AudioSession(control, device, (int)pid, isSystem, instanceId);
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            LastError = e;
            return null;
        }
    }

    public void Dispose()
    {
        // _volume и _meter — тот же RCW, что и _control (приведение типа = QueryInterface), освобождаем один раз.
        Marshal.ReleaseComObject(_control);
    }
}

/// <summary>Сведения о процессе по pid: путь к exe и человеческое имя.</summary>
public static class ProcessInfo
{
    public static bool IsAlive(int processId)
    {
        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            // Нет доступа (например, процесс запущен от администратора) — значит, процесс всё же существует.
            return Marshal.GetLastWin32Error() == 5 /* ERROR_ACCESS_DENIED */;
        Native.CloseHandle(handle);
        return true;
    }

    public static string? GetExePath(int processId)
    {
        var handle = Native.OpenProcess(Native.ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return Native.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    /// <summary>"Google Chrome" из описания exe, иначе имя файла без расширения.</summary>
    public static string GetDisplayName(int processId, string? exePath)
    {
        if (exePath is not null)
        {
            try
            {
                var description = FileVersionInfo.GetVersionInfo(exePath).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            }
            catch (FileNotFoundException)
            {
            }

            return Path.GetFileNameWithoutExtension(exePath);
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return $"Процесс {processId}";
        }
    }
}
