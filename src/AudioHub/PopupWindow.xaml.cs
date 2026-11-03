using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AudioHub.ViewModels;
using Forms = System.Windows.Forms;

namespace AudioHub;

/// <summary>Всплывающая панель над треем. Закрывается, как только теряет фокус.</summary>
public partial class PopupWindow : Window
{
    private const double WheelStep = 2;

    private readonly MainViewModel _viewModel;
    private DateTime _hiddenAt;

    public PopupWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        Deactivated += (_, _) => HidePanel();
        SizeChanged += (_, _) => { if (IsVisible) PlaceNearTray(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) HidePanel(); };
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    /// <summary>Клик по значку в трее: показать или спрятать.</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            HidePanel();
            return;
        }

        // Клик по трею сначала уводит фокус с панели (она прячется), а потом приходит сюда — не открываем её заново.
        if (DateTime.UtcNow - _hiddenAt < TimeSpan.FromMilliseconds(300)) return;

        ShowPanel();
    }

    public void ShowPanel()
    {
        _viewModel.Activate();
        Opacity = 0;
        Show();
        UpdateLayout();
        PlaceNearTray();
        Opacity = 1;
        Activate();
        // Без этого Windows иногда не отдаёт фокус окну, запущенному из трея, и Deactivated не срабатывает.
        SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    private void HidePanel()
    {
        if (!IsVisible) return;
        _hiddenAt = DateTime.UtcNow;
        Hide();
        _viewModel.Deactivate();
    }

    /// <summary>В угол рабочей области у панели задач, на том мониторе, где курсор.</summary>
    private void PlaceNearTray()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        var dpi = VisualTreeHelper.GetDpi(this);

        // Не выше рабочей области: лишнее уходит в прокрутку внутри панели.
        var maxHeight = work.Height / dpi.DpiScaleY;
        if (Math.Abs(MaxHeight - maxHeight) > 0.5)
        {
            MaxHeight = maxHeight;
            UpdateLayout();
        }

        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);

        // Окно уже содержит поле под тень, поэтому прижимаем вплотную к рабочей области.
        var x = work.Left > bounds.Left ? work.Left : work.Right - width;
        var y = work.Top > bounds.Top ? work.Top : work.Bottom - height;

        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    /// <summary>Колесо над ползунком — громкость по 2%, в остальном месте — прокрутка списка.</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var slider = FindParent<Slider>(e.OriginalSource as DependencyObject);
        if (slider is null) return;

        slider.Value = Math.Clamp(slider.Value + Math.Sign(e.Delta) * WheelStep, slider.Minimum, slider.Maximum);
        e.Handled = true;
    }

    private void OnAppDeviceClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AppViewModel app } button) return;

        var menu = new ContextMenu
        {
            PlacementTarget = button,
            Placement = PlacementMode.Bottom,
        };

        foreach (var option in app.GetDeviceOptions())
        {
            var header = new StackPanel();
            header.Children.Add(new TextBlock { Text = option.Name });
            if (option.Description.Length > 0)
                header.Children.Add(new TextBlock { Text = option.Description, Style = (Style)FindResource("Secondary") });

            var item = new MenuItem { Header = header, IsChecked = option.IsSelected };
            item.Click += (_, _) =>
            {
                try
                {
                    app.RouteTo(option.DeviceId);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(this, ex.Message, "AudioHub", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void OnSoundSettingsClick(object sender, RoutedEventArgs e) => Launch("ms-settings:sound");

    private void OnClassicPanelClick(object sender, RoutedEventArgs e) => Launch("mmsys.cpl");

    private void Launch(string target)
    {
        HidePanel();
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private static T? FindParent<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match) return match;
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return null;
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
