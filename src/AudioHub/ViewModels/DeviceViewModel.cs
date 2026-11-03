using System.Windows.Input;
using AudioHub.Audio;

namespace AudioHub.ViewModels;

public sealed class DeviceViewModel : ObservableObject
{
    private double _volume;
    private bool _isMuted;
    private double _peak;

    public DeviceViewModel(AudioDevice device, bool isDefault, Action<DeviceViewModel> makeDefault)
    {
        Device = device;
        IsDefault = isDefault;
        MakeDefaultCommand = new RelayCommand(() => makeDefault(this));
        ToggleMuteCommand = new RelayCommand(() => IsMuted = !IsMuted);
        _volume = device.Volume * 100;
        _isMuted = device.IsMuted;
    }

    public AudioDevice Device { get; }

    public string Name => Device.Name;

    public string Description => Device.Description;

    public bool IsInput => Device.Flow == DataFlow.Capture;

    public bool IsDefault { get; }

    public ICommand MakeDefaultCommand { get; }

    public ICommand ToggleMuteCommand { get; }

    /// <summary>0..100.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            if (!Set(ref _volume, value)) return;
            Device.Volume = (float)(value / 100);
            // Как в Windows: подвинул громкость — mute снимается.
            if (IsMuted && value > 0) IsMuted = false;
        }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (Set(ref _isMuted, value)) Device.IsMuted = value;
        }
    }

    /// <summary>Уровень сигнала 0..1 для полоски под ползунком.</summary>
    public double Peak
    {
        get => _peak;
        private set => Set(ref _peak, value);
    }

    /// <summary>Подтянуть значения, изменённые извне (клавиши громкости, другие программы).</summary>
    public void Sync(bool withPeak)
    {
        var volume = Device.Volume * 100;
        if (Math.Abs(volume - _volume) > 0.5) Set(ref _volume, volume, nameof(Volume));
        Set(ref _isMuted, Device.IsMuted, nameof(IsMuted));
        if (withPeak) Peak = _isMuted ? 0 : Device.Peak;
    }
}
