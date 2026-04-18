using MNTBloxAudio.Core.Models;
using NAudio.CoreAudioApi;

namespace MNTBloxAudio.Core.Services;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var defaultDeviceId = SafeGetDefaultRenderDeviceId(enumerator);

        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active | DeviceState.Disabled | DeviceState.Unplugged)
            .Select(device => new AudioDeviceInfo
            {
                Id = device.ID,
                Name = device.FriendlyName,
                Flow = device.DataFlow.ToString(),
                State = device.State.ToString(),
                IsDefault = string.Equals(device.ID, defaultDeviceId, StringComparison.OrdinalIgnoreCase),
            })
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? SafeGetDefaultRenderDeviceId(MMDeviceEnumerator enumerator)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
        }
        catch
        {
            return null;
        }
    }
}
