namespace MNTBloxAudio.Core.Models;

public sealed class ProxyStatusInfo
{
    public bool IsRunning { get; init; }

    public int Port { get; init; }

    public string StatusText { get; init; } = "Proxy is disabled.";
}
