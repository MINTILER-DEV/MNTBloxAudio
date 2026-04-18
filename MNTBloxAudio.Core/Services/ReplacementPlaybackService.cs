using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MNTBloxAudio.Core.Services;

public sealed class ReplacementPlaybackService : IDisposable
{
    private readonly object syncRoot = new();
    private IWavePlayer? output;
    private AudioFileReader? reader;

    public void ValidatePlayback(string filePath, string? outputDeviceId)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Replacement audio file was not found.", filePath);
        }

        using var enumerator = new MMDeviceEnumerator();
        var device = ResolveDevice(enumerator, outputDeviceId);
        using var validationReader = CreateReader(filePath);
        using var validationOutput = new WasapiOut(device, AudioClientShareMode.Shared, true, 200);

        validationOutput.Init(validationReader);
    }

    public async Task PlayAsync(string filePath, string? outputDeviceId, float volume)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Replacement audio file was not found.", filePath);
        }

        Stop();

        IWavePlayer nextOutput;
        AudioFileReader nextReader;

        using (var enumerator = new MMDeviceEnumerator())
        {
            var device = ResolveDevice(enumerator, outputDeviceId);
            nextReader = CreateReader(filePath);
            nextReader.Volume = Math.Clamp(volume, 0f, 1f);

            nextOutput = new WasapiOut(device, AudioClientShareMode.Shared, true, 200);
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        nextOutput.PlaybackStopped += (_, _) => completion.TrySetResult();
        nextOutput.Init(nextReader);

        lock (syncRoot)
        {
            output = nextOutput;
            reader = nextReader;
        }

        nextOutput.Play();
        await completion.Task.ConfigureAwait(false);
        Stop();
    }

    public void Stop()
    {
        lock (syncRoot)
        {
            try
            {
                output?.Stop();
            }
            catch
            {
                // Ignore teardown errors.
            }

            output?.Dispose();
            reader?.Dispose();
            output = null;
            reader = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private static MMDevice ResolveDevice(MMDeviceEnumerator enumerator, string? outputDeviceId)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(outputDeviceId)
                ? enumerator.GetDevice(outputDeviceId)
                : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
    }

    private static AudioFileReader CreateReader(string filePath)
    {
        try
        {
            return new AudioFileReader(filePath);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "The replacement audio file could not be opened. Use a local WAV or MP3 file, or pick a different output device.",
                exception);
        }
    }
}
