using System.Windows.Input;
using System.Windows.Media;
using AudioHub.Audio;

namespace AudioHub.ViewModels;

/// <summary>Одна программа в микшере. У программы может быть несколько потоков — управляем всеми сразу.</summary>
public sealed class AppViewModel : ObservableObject, IDisposable
{
    private readonly IAppRouting _routing;
    private readonly Func<IReadOnlyList<AudioDevice>> _outputs;
    private List<AudioSession> _sessions;
    private double _volume;
    private bool _isMuted;
    private double _peak;
    private string? _routedDeviceId;

    public AppViewModel(int processId, List<AudioSession> sessions, IAppRouting routing, Func<IReadOnlyList<AudioDevice>> outputs)
    {
        ProcessId = processId;
        _sessions = sessions;
        _routing = routing;
        _outputs = outputs;
        IsSystemSounds = sessions[0].IsSystemSounds;

        var exePath = IsSystemSounds ? null : ProcessInfo.GetExePath(processId);
        Name = IsSystemSounds
            ? "Системные звуки"
            : sessions.Select(s => s.DisplayName).FirstOrDefault(n => n is not null) ?? ProcessInfo.GetDisplayName(processId, exePath);
        Icon = exePath is null ? null : IconCache.Get(exePath);

        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        _volume = sessions[0].Volume * 100;
        _isMuted = sessions[0].IsMuted;
        _routedDeviceId = CanRoute ? _routing.GetDevice(processId, DataFlow.Render) : null;
    }

    public int ProcessId { get; }

    public bool IsSystemSounds { get; }

    public string Name { get; }

    /// <summary>Иконка exe; null — показываем значок динамика.</summary>
    public ImageSource? Icon { get; }

    public ICommand ToggleMuteCommand { get; }

    /// <summary>Системные звуки идут туда же, куда и Windows, — их не перенаправить.</summary>
    public bool CanRoute => _routing.IsSupported && !IsSystemSounds;

    public double Volume
    {
        get => _volume;
        set
        {
            if (!Set(ref _volume, value)) return;
            foreach (var session in _sessions) session.Volume = (float)(value / 100);
            if (IsMuted && value > 0) IsMuted = false;
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (!Set(ref _isMuted, value)) return;
            foreach (var session in _sessions) session.IsMuted = value;
        }
    }

    public double Peak
    {
        get => _peak;
        private set => Set(ref _peak, value);
    }

    /// <summary>Подпись на кнопке выбора устройства.</summary>
    public string DeviceLabel =>
        _routedDeviceId is null
            ? "По умолчанию"
            : _outputs().FirstOrDefault(d => d.Id == _routedDeviceId)?.Name ?? "Устройство отключено";

    public bool IsRouted => _routedDeviceId is not null;

    /// <summary>Варианты для меню: «По умолчанию» + все активные устройства вывода.</summary>
    public IReadOnlyList<DeviceOption> GetDeviceOptions()
    {
        var options = new List<DeviceOption> { new(null, "По умолчанию", "", _routedDeviceId is null) };
        options.AddRange(_outputs().Select(d => new DeviceOption(d.Id, d.Name, d.Description, d.Id == _routedDeviceId)));
        return options;
    }

    public void RouteTo(string? deviceId)
    {
        if (!CanRoute) return;
        _routing.SetDevice(ProcessId, DataFlow.Render, deviceId);
        _routedDeviceId = deviceId;
        OnPropertyChanged(nameof(DeviceLabel));
        OnPropertyChanged(nameof(IsRouted));
    }

    /// <summary>Заменить набор потоков (после повторного перечисления), старые освободить.</summary>
    public void ReplaceSessions(List<AudioSession> sessions)
    {
        foreach (var old in _sessions) old.Dispose();
        _sessions = sessions;
    }

    public void Sync(bool withPeak, bool withRouting)
    {
        var first = _sessions[0];
        var volume = first.Volume * 100;
        if (Math.Abs(volume - _volume) > 0.5) Set(ref _volume, volume, nameof(Volume));
        Set(ref _isMuted, first.IsMuted, nameof(IsMuted));
        if (withPeak) Peak = _isMuted ? 0 : _sessions.Max(s => s.Peak);

        if (withRouting && CanRoute)
        {
            var routed = _routing.GetDevice(ProcessId, DataFlow.Render);
            if (routed != _routedDeviceId)
            {
                _routedDeviceId = routed;
                OnPropertyChanged(nameof(DeviceLabel));
                OnPropertyChanged(nameof(IsRouted));
            }
        }
    }

    public void Dispose()
    {
        foreach (var session in _sessions) session.Dispose();
        _sessions = [];
    }
}

public sealed record DeviceOption(string? DeviceId, string Name, string Description, bool IsSelected);
