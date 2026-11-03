namespace AudioHub.Audio;

/// <summary>Заглушка, если API закрепления устройств за программами недоступен.</summary>
public sealed class NullAppRouting : IAppRouting
{
    public bool IsSupported => false;

    public string? GetDevice(int processId, DataFlow flow) => null;

    public void SetDevice(int processId, DataFlow flow, string? deviceId)
    {
    }
}
