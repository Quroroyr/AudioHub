using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using AudioHub.Audio;
using AudioHub.ViewModels;
using Microsoft.Win32;

namespace AudioHub;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AudioService? _audio;
    private MainViewModel? _viewModel;
    private PopupWindow? _popup;
    private TrayIcon? _tray;

    private static string DumpPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioHub", "dump.txt");

    private static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioHub", "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Диагностика работает и при запущенной копии, поэтому до проверки единственного экземпляра.
        if (e.Args.Contains("--dump") || e.Args.Contains("--set-default"))
        {
            RunDiagnostics(e.Args);
            Shutdown();
            return;
        }

        _singleInstance = new Mutex(true, @"Local\AudioHub-single-instance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        Theme.Apply(Resources);

        try
        {
            _audio = new AudioService();
        }
        catch (Exception ex) when (ex is COMException or InvalidComObjectException)
        {
            // Без службы звука работать не с чем — сообщаем и выходим, а не висим без значка в трее.
            Log(ex);
            MessageBox.Show("Не удалось подключиться к звуковой службе Windows.\n" + ex.Message, "AudioHub",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        _viewModel = new MainViewModel(_audio, CreateRouting(), Dispatcher);
        _popup = new PopupWindow(_viewModel);
        _tray = new TrayIcon(() => _popup.Toggle(), ExitApp);

        _viewModel.DefaultOutputChanged += (_, _) => _tray.Update(_audio.DefaultOutput);
        _tray.Update(_audio.DefaultOutput);

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        // --show: сразу открыть панель (удобно для проверки, не ища значок в трее).
        if (e.Args.Contains("--show"))
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => _popup.ShowPanel());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _tray?.Dispose();
        _viewModel?.Dispose();
        _audio?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static IAppRouting CreateRouting()
    {
        try
        {
            return new AppRouting();
        }
        catch (Exception ex)
        {
            Log(ex);
            return new NullAppRouting();
        }
    }

    private void ExitApp()
    {
        _popup?.Close();
        Shutdown();
    }

    /// <summary>Сменили тему или акцентный цвет Windows — перекрашиваемся.</summary>
    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)) return;
        Dispatcher.BeginInvoke(() =>
        {
            Theme.Apply(Resources);
            _tray?.Update(_audio?.DefaultOutput, force: true);
        });
    }

    /// <summary>
    /// Звуковое устройство может пропасть посреди любого вызова — это не повод падать.
    /// Такие ошибки пишем в лог и продолжаем; остальное — тоже в лог, но приложение закрывается.
    /// </summary>
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        if (e.Exception is COMException or InvalidComObjectException)
            e.Handled = true;
    }

    /// <summary>--dump: что приложение видит в звуке Windows — в dump.txt рядом с логом ошибок.</summary>
    private static void Dump(AudioService audio, string fileName = "dump.txt")
    {
        var text = new System.Text.StringBuilder();
        foreach (var device in audio.Outputs.Concat(audio.Inputs))
        {
            var mark = device.Id == audio.DefaultOutputId || device.Id == audio.DefaultInputId ? " [default]" : "";
            text.AppendLine($"{device.Flow} {device}{mark}: {device.Volume * 100:0}%, mute={device.IsMuted}");
        }

        var sessions = audio.GetOutputSessions();
        text.AppendLine($"Sessions: {sessions.Count}");
        foreach (var session in sessions)
        {
            var exe = ProcessInfo.GetExePath(session.ProcessId);
            text.AppendLine($"  pid={session.ProcessId} system={session.IsSystemSounds} device={session.Device.Name} " +
                            $"vol={session.Volume * 100:0}% name={session.DisplayName ?? ProcessInfo.GetDisplayName(session.ProcessId, exe)}");
            session.Dispose();
        }

        if (AudioSession.LastError is { } error) text.AppendLine($"Last session error: {error}");

        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(LogPath)!, fileName), text.ToString(), System.Text.Encoding.UTF8);
    }

    /// <summary>--dump: состояние звука в dump.txt; --set-default <id>: сделать устройство основным и записать итог.</summary>
    private static void RunDiagnostics(string[] args)
    {
        using var audio = new AudioService();
        var index = Array.IndexOf(args, "--set-default");
        if (index >= 0 && index + 1 < args.Length)
        {
            var target = audio.Outputs.Concat(audio.Inputs).FirstOrDefault(d => d.Id == args[index + 1]);
            try
            {
                if (target is null) throw new ArgumentException($"Нет устройства {args[index + 1]}");
                audio.SetDefault(target);
                File.AppendAllText(DumpPath, $"set-default {target.Name}: ok" + Environment.NewLine);
            }
            catch (Exception ex)
            {
                File.AppendAllText(DumpPath, $"set-default failed: {ex}" + Environment.NewLine);
            }

            return;
        }

        Dump(audio);
    }

    internal static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
