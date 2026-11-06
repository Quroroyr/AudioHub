using Microsoft.Win32;

namespace AudioHub;

/// <summary>
/// Запуск вместе с Windows через HKCU\...\Run (без прав администратора).
/// Windows 11 запускает запись, только если в StartupApproved\Run у неё отметка «разрешено» — ту же ставит
/// переключатель в диспетчере задач. Без неё запись лежит в реестре, но при входе в систему не выполняется.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "AudioHub";

    // 12 байт: первый — состояние (02 — включено, 03 — выключено пользователем), дальше — время выключения.
    private static readonly byte[] Enabled = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return key?.GetValue(ValueName) is string command
                   && command.Contains(Environment.ProcessPath ?? "", StringComparison.OrdinalIgnoreCase)
                   && approved?.GetValue(ValueName) is byte[] { Length: > 0 } state && state[0] == 0x02;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey);
            if (value)
            {
                key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
                approved.SetValue(ValueName, Enabled, RegistryValueKind.Binary);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                approved.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
    }
}
