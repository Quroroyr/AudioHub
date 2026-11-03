using System.Runtime.InteropServices;
using AudioHub.Audio.Interop;

namespace AudioHub.Audio;

/// <summary>
/// Точка входа в звук Windows: списки устройств, устройство по умолчанию, потоки программ.
/// Все методы вызывать из UI-потока; события приходят из потока Core Audio.
/// </summary>
public sealed class AudioService : IDisposable
{
    private readonly IMMDeviceEnumerator _enumerator;
    private readonly NotificationClient _client;
    private List<AudioDevice> _outputs = [];
    private List<AudioDevice> _inputs = [];

    public AudioService()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        _client = new NotificationClient(this);
        _enumerator.RegisterEndpointNotificationCallback(_client);
        RefreshDevices();
    }

    /// <summary>Устройство подключили/отключили или сменилось устройство по умолчанию.</summary>
    public event EventHandler? DevicesChanged;

    public IReadOnlyList<AudioDevice> Outputs => _outputs;

    public IReadOnlyList<AudioDevice> Inputs => _inputs;

    public string? DefaultOutputId { get; private set; }

    public string? DefaultInputId { get; private set; }

    public AudioDevice? DefaultOutput => _outputs.Find(d => d.Id == DefaultOutputId);

    public AudioDevice? DefaultInput => _inputs.Find(d => d.Id == DefaultInputId);

    public void RefreshDevices()
    {
        // Старые освобождаем в любом случае, новые принимаем, только если всё прочиталось целиком.
        foreach (var device in _outputs.Concat(_inputs))
            device.Dispose();
        _outputs = [];
        _inputs = [];

        var outputs = Enumerate(EDataFlow.Render, DataFlow.Render);
        List<AudioDevice> inputs = [];
        try
        {
            inputs = Enumerate(EDataFlow.Capture, DataFlow.Capture);
            DefaultOutputId = GetDefaultId(EDataFlow.Render);
            DefaultInputId = GetDefaultId(EDataFlow.Capture);
        }
        catch
        {
            foreach (var device in outputs.Concat(inputs))
                device.Dispose();
            throw;
        }

        _outputs = outputs;
        _inputs = inputs;
    }

    /// <summary>Сделать устройство основным для всех ролей: звук, мультимедиа и связь.</summary>
    public void SetDefault(AudioDevice device)
    {
        var policy = (IPolicyConfig)new PolicyConfigClientComObject();
        try
        {
            foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            {
                var hr = policy.SetDefaultEndpoint(device.Id, role);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    /// <summary>Потоки программ на всех активных устройствах вывода. Вызывающий отвечает за Dispose.</summary>
    public List<AudioSession> GetOutputSessions()
    {
        var sessions = new List<AudioSession>();
        foreach (var device in _outputs)
        {
            try
            {
                sessions.AddRange(device.GetSessions());
            }
            catch (COMException)
            {
                // Устройство отвалилось прямо сейчас — DevicesChanged придёт следом.
            }
        }

        return sessions;
    }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_client); } catch (COMException) { }
        foreach (var device in _outputs.Concat(_inputs))
            device.Dispose();
        _outputs = [];
        _inputs = [];
        Marshal.ReleaseComObject(_enumerator);
    }

    private List<AudioDevice> Enumerate(EDataFlow flow, DataFlow publicFlow)
    {
        _enumerator.EnumAudioEndpoints(flow, DeviceState.Active, out var collection);
        try
        {
            collection.GetCount(out var count);
            var devices = new List<AudioDevice>((int)count);
            try
            {
                for (uint i = 0; i < count; i++)
                {
                    collection.Item(i, out var device);
                    try
                    {
                        devices.Add(new AudioDevice(device, publicFlow));
                    }
                    catch (COMException)
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            catch
            {
                foreach (var device in devices) device.Dispose();
                throw;
            }

            return devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }
    }

    private string? GetDefaultId(EDataFlow flow)
    {
        if (_enumerator.GetDefaultAudioEndpoint(flow, ERole.Console, out var device) < 0) return null;
        try
        {
            device.GetId(out var id);
            return id;
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    [ComVisible(true)]
    private sealed class NotificationClient(AudioService owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Raise();

        public void OnDeviceAdded(string deviceId) => Raise();

        public void OnDeviceRemoved(string deviceId) => Raise();

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId)
        {
            // Приходит по разу на каждую роль; UI интересует только «основная».
            if (role == ERole.Console) Raise();
        }

        public void OnPropertyValueChanged(string deviceId, PropertyKey key)
        {
            // Сыплется постоянно (форматы, уровни и т.п.) — игнорируем.
        }

        private void Raise() => owner.DevicesChanged?.Invoke(owner, EventArgs.Empty);
    }
}
