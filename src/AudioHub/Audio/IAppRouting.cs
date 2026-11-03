namespace AudioHub.Audio;

public enum DataFlow
{
    Render = 0,
    Capture = 1,
}

/// <summary>
/// Своё устройство для отдельной программы (то же, что «Параметры устройств и громкости приложений» в Windows).
/// Идентификатор устройства — обычный MMDevice id вида "{0.0.0.00000000}.{guid}".
/// </summary>
public interface IAppRouting
{
    /// <summary>false, если API недоступен на этой сборке Windows — тогда UI прячет выбор устройства.</summary>
    bool IsSupported { get; }

    /// <summary>Устройство, закреплённое за процессом, или null, если программа идёт в устройство по умолчанию.</summary>
    string? GetDevice(int processId, DataFlow flow);

    /// <summary>Закрепить устройство за процессом; deviceId = null — вернуть на устройство по умолчанию.</summary>
    void SetDevice(int processId, DataFlow flow, string? deviceId);
}
