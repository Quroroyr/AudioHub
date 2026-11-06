using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AudioHub.Audio;
using AudioHub.Audio.Interop;
using Forms = System.Windows.Forms;

namespace AudioHub;

/// <summary>Значок в трее: глиф громкости основного устройства, подсказка, меню по правому клику.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _autostartItem;
    private IntPtr _iconHandle;
    private string? _lastGlyphKey;

    public TrayIcon(Action onLeftClick, Action onExit)
    {
        _autostartItem = new Forms.ToolStripMenuItem("Запускать вместе с Windows") { Checked = Autostart.IsEnabled };
        _autostartItem.Click += (_, _) =>
        {
            Autostart.IsEnabled = !Autostart.IsEnabled;
            _autostartItem.Checked = Autostart.IsEnabled;
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть", null, (_, _) => onLeftClick());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Параметры звука Windows", null, (_, _) => Launch("ms-settings:sound"));
        menu.Items.Add("Громкость приложений Windows", null, (_, _) => Launch("ms-settings:apps-volume"));
        menu.Items.Add("Классическая панель звука", null, (_, _) => Launch("mmsys.cpl"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_autostartItem);
        menu.Items.Add("Выход", null, (_, _) => onExit());

        _icon = new Forms.NotifyIcon { ContextMenuStrip = menu, Text = "AudioHub" };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) onLeftClick();
        };
        // Значок показываем в первом Update, когда устройство уже известно: Windows запоминает подсказку,
        // с которой значок впервые появился, и иначе держала бы в трее «нет устройств вывода».
    }

    /// <summary>Перерисовать под текущую громкость основного устройства.</summary>
    public void Update(AudioDevice? device, bool force = false)
    {
        var volume = device is null ? 0 : Math.Round(device.Volume * 100);
        var muted = device?.IsMuted ?? true;

        var tooltip = device is null ? "AudioHub — нет устройств вывода" : $"{device.Name}: {(muted ? "звук выключен" : $"{volume}%")}";
        // NotifyIcon.Text ограничен 127 символами.
        _icon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        var glyph = device is null ? Glyphs.Mute : Glyphs.ForVolume(volume, muted);
        var light = Theme.TaskbarUsesLight;
        var key = glyph + light;
        if (!force && key == _lastGlyphKey) return;
        _lastGlyphKey = key;

        var previous = _iconHandle;
        _icon.Icon = RenderGlyph(glyph, light ? Color.Black : Color.White, out _iconHandle);
        if (previous != IntPtr.Zero) Native.DestroyIcon(previous);
        _icon.Visible = true;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        if (_iconHandle != IntPtr.Zero) Native.DestroyIcon(_iconHandle);
    }

    private static Icon RenderGlyph(string glyph, Color color, out IntPtr handle)
    {
        var size = Forms.SystemInformation.SmallIconSize.Width;
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            using var font = CreateIconFont(size * 0.78f);
            using var brush = new SolidBrush(color);
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            graphics.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), format);
        }

        handle = bitmap.GetHicon();
        return Icon.FromHandle(handle);
    }

    private static Font CreateIconFont(float pixels)
    {
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            var font = new Font(name, pixels, GraphicsUnit.Pixel);
            if (font.Name == name) return font;
            font.Dispose();
        }

        return new Font(FontFamily.GenericSansSerif, pixels, GraphicsUnit.Pixel);
    }

    private static void Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
