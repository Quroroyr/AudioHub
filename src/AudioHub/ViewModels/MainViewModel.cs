using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AudioHub.Audio;

namespace AudioHub.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int SessionRefreshEveryTicks = 20; // 20 × 50 мс = раз в секунду

    private readonly AudioService _audio;
    private readonly IAppRouting _routing;
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _devicesDebounce;
    private DeviceViewModel? _defaultOutput;
    private DeviceViewModel? _defaultInput;
    private bool _showAllOutputs;
    private bool _showAllInputs;
    private bool _hasApps;
    private int _tickCount;

    public MainViewModel(AudioService audio, IAppRouting routing, Dispatcher dispatcher)
    {
        _audio = audio;
        _routing = routing;

        _tick = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => OnTick(), dispatcher);
        _tick.Stop();

        // Windows шлёт пачку уведомлений на одно действие — собираем их в одно обновление.
        _devicesDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) =>
        {
            _devicesDebounce!.Stop();
            ReloadDevices();
        }, dispatcher);
        _devicesDebounce.Stop();

        _audio.DevicesChanged += (_, _) => dispatcher.BeginInvoke(() =>
        {
            _devicesDebounce.Stop();
            _devicesDebounce.Start();
        });

        ToggleAllOutputsCommand = new RelayCommand(() => ShowAllOutputs = !ShowAllOutputs);
        ToggleAllInputsCommand = new RelayCommand(() => ShowAllInputs = !ShowAllInputs);
        ReloadDevices();
    }

    /// <summary>Сменилось устройство по умолчанию или его громкость — для иконки в трее.</summary>
    public event EventHandler? DefaultOutputChanged;

    public ObservableCollection<DeviceViewModel> Outputs { get; } = [];

    public ObservableCollection<DeviceViewModel> Inputs { get; } = [];

    public ObservableCollection<AppViewModel> Apps { get; } = [];

    public DeviceViewModel? DefaultOutput
    {
        get => _defaultOutput;
        private set => Set(ref _defaultOutput, value);
    }

    public DeviceViewModel? DefaultInput
    {
        get => _defaultInput;
        private set => Set(ref _defaultInput, value);
    }

    public bool ShowAllOutputs
    {
        get => _showAllOutputs;
        set => Set(ref _showAllOutputs, value);
    }

    public bool ShowAllInputs
    {
        get => _showAllInputs;
        set => Set(ref _showAllInputs, value);
    }

    public bool HasApps
    {
        get => _hasApps;
        private set => Set(ref _hasApps, value);
    }

    public bool CanRouteApps => _routing.IsSupported;

    public RelayCommand ToggleAllOutputsCommand { get; }

    public RelayCommand ToggleAllInputsCommand { get; }

    /// <summary>Панель открыли: всё перечитать и запустить живые обновления (уровни, громкость).</summary>
    public void Activate()
    {
        ShowAllOutputs = false;
        ShowAllInputs = false;
        foreach (var device in Outputs.Concat(Inputs)) device.Sync(withPeak: false);
        ReloadApps();
        _tickCount = 0;
        _tick.Start();
    }

    /// <summary>Панель закрыли: в трее ничего не опрашиваем.</summary>
    public void Deactivate()
    {
        _tick.Stop();
        foreach (var device in Outputs.Concat(Inputs)) device.Sync(withPeak: false);
    }

    public void Dispose()
    {
        _tick.Stop();
        _devicesDebounce.Stop();
        ClearApps();
    }

    private void ReloadDevices()
    {
        try
        {
            ReloadDevicesCore();
            _devicesDebounce.Interval = TimeSpan.FromMilliseconds(150);
        }
        catch (Exception e) when (e is COMException or InvalidComObjectException)
        {
            // Устройство пропало посреди перечисления. Ничего полуготового не показываем и пробуем снова чуть позже.
            ClearDevices();
            _devicesDebounce.Interval = TimeSpan.FromSeconds(2);
            _devicesDebounce.Start();
        }
    }

    private void ReloadDevicesCore()
    {
        // Сначала отпускаем модели: RefreshDevices освободит объекты устройств, на которые они ссылаются.
        ClearDevices();
        _audio.RefreshDevices();

        var outputs = _audio.Outputs.Select(d => new DeviceViewModel(d, d.Id == _audio.DefaultOutputId, MakeDefault)).ToList();
        var inputs = _audio.Inputs.Select(d => new DeviceViewModel(d, d.Id == _audio.DefaultInputId, MakeDefault)).ToList();
        foreach (var device in outputs) Outputs.Add(device);
        foreach (var device in inputs) Inputs.Add(device);

        DefaultOutput = Outputs.FirstOrDefault(d => d.IsDefault);
        DefaultInput = Inputs.FirstOrDefault(d => d.IsDefault);

        // Иконке в трее нужна живая громкость основного устройства даже при закрытой панели.
        _audio.DefaultOutput?.WatchVolume();
        if (_audio.DefaultOutput is { } output)
            output.VolumeChanged += (_, _) => _tick.Dispatcher.BeginInvoke(() =>
            {
                DefaultOutput?.Sync(withPeak: false);
                DefaultOutputChanged?.Invoke(this, EventArgs.Empty);
            });

        if (_tick.IsEnabled) ReloadApps();

        DefaultOutputChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MakeDefault(DeviceViewModel device)
    {
        if (device.IsDefault) return;
        try
        {
            _audio.SetDefault(device.Device);
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            // Устройство пропало — список обновится по уведомлению. Причину пишем в лог, чтобы не гадать.
            App.Log(e);
        }
    }

    /// <summary>
    /// Ошибки ловим здесь, а не в глобальном обработчике: DispatcherTimer после исключения в Tick больше не тикает.
    /// </summary>
    private void OnTick()
    {
        _tickCount++;
        var refreshSessions = _tickCount % SessionRefreshEveryTicks == 0;
        if (refreshSessions) Safe(ReloadApps);

        if (DefaultOutput is { } output) Safe(() => output.Sync(withPeak: true));
        if (DefaultInput is { } input) Safe(() => input.Sync(withPeak: true));
        foreach (var app in Apps)
            // Поток мог закрыться между обновлениями — уберётся при следующем перечислении.
            Safe(() => app.Sync(withPeak: true, withRouting: refreshSessions));
    }

    private static void Safe(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e) when (e is COMException or InvalidComObjectException)
        {
        }
    }

    /// <summary>Перечислить потоки и сверить с текущим списком, не пересоздавая строки без нужды.</summary>
    private void ReloadApps()
    {
        // Системные звуки есть на каждом устройстве; как и Windows, показываем только поток основного.
        var all = _audio.GetOutputSessions();
        foreach (var foreign in all.Where(s => s.IsSystemSounds && s.Device.Id != _audio.DefaultOutputId).ToList())
        {
            foreign.Dispose();
            all.Remove(foreign);
        }

        var byProcess = all
            .GroupBy(s => s.IsSystemSounds ? -1 : s.ProcessId)
            .ToDictionary(g => g.Key, g => g.ToList());

        for (var i = Apps.Count - 1; i >= 0; i--)
        {
            var app = Apps[i];
            var key = app.IsSystemSounds ? -1 : app.ProcessId;
            if (byProcess.Remove(key, out var sessions))
            {
                app.ReplaceSessions(sessions);
            }
            else
            {
                app.Dispose();
                Apps.RemoveAt(i);
            }
        }

        foreach (var (_, sessions) in byProcess)
        {
            try
            {
                Insert(new AppViewModel(sessions[0].ProcessId, sessions, _routing, () => _audio.Outputs));
            }
            catch (Exception e) when (e is COMException or InvalidComObjectException)
            {
                foreach (var session in sessions) session.Dispose();
            }
        }

        HasApps = Apps.Count > 0;
    }

    /// <summary>По алфавиту, системные звуки — в самом конце.</summary>
    private void Insert(AppViewModel app)
    {
        var index = 0;
        while (index < Apps.Count && Compare(Apps[index], app) <= 0) index++;
        Apps.Insert(index, app);

        static int Compare(AppViewModel a, AppViewModel b) =>
            a.IsSystemSounds != b.IsSystemSounds
                ? a.IsSystemSounds ? 1 : -1
                : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    }

    private void ClearDevices()
    {
        ClearApps();
        DefaultOutput = null;
        DefaultInput = null;
        Outputs.Clear();
        Inputs.Clear();
    }

    private void ClearApps()
    {
        foreach (var app in Apps) app.Dispose();
        Apps.Clear();
        HasApps = false;
    }
}
