using System.Runtime.InteropServices;
using AudioHub.Audio.Interop;

namespace AudioHub.Audio;

/// <summary>Устройство вывода или ввода (колонки, наушники, микрофон).</summary>
public sealed class AudioDevice : IDisposable
{
    private static readonly Guid EndpointVolumeIid = typeof(IAudioEndpointVolume).GUID;
    private static readonly Guid MeterIid = typeof(IAudioMeterInformation).GUID;
    private static readonly Guid SessionManagerIid = typeof(IAudioSessionManager2).GUID;

    private readonly IMMDevice _device;
    private readonly IAudioEndpointVolume? _volume;
    private readonly IAudioMeterInformation? _meter;
    private IAudioSessionManager2? _sessionManager;
    private VolumeCallback? _callback;

    internal AudioDevice(IMMDevice device, DataFlow flow)
    {
        _device = device;
        Flow = flow;
        _device.GetId(out var id);
        Id = id;

        _device.OpenPropertyStore(0 /* STGM_READ */, out var store);
        try
        {
            Name = ReadString(store, PropertyKeys.DeviceDescription) ?? ReadString(store, PropertyKeys.FriendlyName) ?? id;
            Description = ReadString(store, PropertyKeys.InterfaceFriendlyName) ?? "";
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }

        _volume = Activate<IAudioEndpointVolume>(EndpointVolumeIid);
        _meter = Activate<IAudioMeterInformation>(MeterIid);
    }

    public string Id { get; }

    public DataFlow Flow { get; }

    /// <summary>"Динамики", "Наушники", "Микрофон".</summary>
    public string Name { get; }

    /// <summary>Имя железа/драйвера: "Realtek(R) Audio", "HyperX Cloud".</summary>
    public string Description { get; }

    /// <summary>Громкость устройства или его mute изменились (в том числе извне). Приходит не в UI-потоке.</summary>
    public event EventHandler? VolumeChanged;

    public float Volume
    {
        get
        {
            if (_volume is null) return 0;
            _volume.GetMasterVolumeLevelScalar(out var level);
            return level;
        }
        set
        {
            if (_volume is null) return;
            var context = Guid.Empty;
            _volume.SetMasterVolumeLevelScalar(Math.Clamp(value, 0f, 1f), ref context);
        }
    }

    public bool IsMuted
    {
        get
        {
            if (_volume is null) return false;
            _volume.GetMute(out var muted);
            return muted;
        }
        set
        {
            if (_volume is null) return;
            var context = Guid.Empty;
            _volume.SetMute(value, ref context);
        }
    }

    /// <summary>Текущий уровень сигнала 0..1.</summary>
    public float Peak => _meter is not null && _meter.GetPeakValue(out var peak) == 0 ? peak : 0;

    /// <summary>Подписаться на изменения громкости (нужно только для устройства по умолчанию — для иконки в трее).</summary>
    public void WatchVolume()
    {
        if (_volume is null || _callback is not null) return;
        _callback = new VolumeCallback(this);
        _volume.RegisterControlChangeNotify(_callback);
    }

    internal IReadOnlyList<AudioSession> GetSessions()
    {
        _sessionManager ??= Activate<IAudioSessionManager2>(SessionManagerIid);
        if (_sessionManager is null) return [];

        _sessionManager.GetSessionEnumerator(out var enumerator);
        try
        {
            enumerator.GetCount(out var count);
            var sessions = new List<AudioSession>(count);
            try
            {
                for (var i = 0; i < count; i++)
                {
                    enumerator.GetSession(i, out var control);
                    var session = AudioSession.TryCreate(control, this);
                    if (session is not null)
                        sessions.Add(session);
                    else
                        Marshal.ReleaseComObject(control);
                }
            }
            catch
            {
                foreach (var session in sessions) session.Dispose();
                throw;
            }

            return sessions;
        }
        finally
        {
            Marshal.ReleaseComObject(enumerator);
        }
    }

    public void Dispose()
    {
        if (_callback is not null && _volume is not null)
        {
            try { _volume.UnregisterControlChangeNotify(_callback); } catch (COMException) { }
            _callback = null;
        }

        Release(_sessionManager);
        Release(_meter);
        Release(_volume);
        Release(_device);
    }

    private T? Activate<T>(Guid iid) where T : class
    {
        try
        {
            _device.Activate(ref iid, Native.ClsCtxAll, IntPtr.Zero, out var instance);
            return (T)instance;
        }
        catch (COMException)
        {
            // Устройство пропало или отключено между перечислением и активацией.
            return null;
        }
    }

    private static string? ReadString(IPropertyStore store, PropertyKey key)
    {
        try
        {
            store.GetValue(ref key, out var value);
            try
            {
                var text = value.AsString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            finally
            {
                Native.PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
            Marshal.ReleaseComObject(comObject);
    }

    public override string ToString() => $"{Name} ({Description})";

    [ComVisible(true)]
    private sealed class VolumeCallback(AudioDevice owner) : IAudioEndpointVolumeCallback
    {
        public void OnNotify(IntPtr notifyData) => owner.VolumeChanged?.Invoke(owner, EventArgs.Empty);
    }
}
