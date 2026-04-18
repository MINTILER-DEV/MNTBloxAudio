using System.Diagnostics;
using MNTBloxAudio.Core.Models;
using NAudio.CoreAudioApi;

namespace MNTBloxAudio.Core.Services;

public sealed class RobloxAudioSessionService
{
    public IReadOnlyList<RobloxAudioSessionInfo> GetRobloxSessions()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var sessions = new List<RobloxAudioSessionInfo>();
        var discoveredProcessIds = new HashSet<int>();

        foreach (var device in devices)
        {
            device.AudioSessionManager.RefreshSessions();
            var sessionCollection = device.AudioSessionManager.Sessions;

            for (var index = 0; index < sessionCollection.Count; index++)
            {
                using var session = sessionCollection[index];

                var processId = (int)session.GetProcessID;
                if (processId <= 0)
                {
                    continue;
                }

                Process? process;
                try
                {
                    process = Process.GetProcessById(processId);
                }
                catch
                {
                    continue;
                }

                if (!IsRobloxAudioOwner(process))
                {
                    continue;
                }

                discoveredProcessIds.Add(processId);

                var processName = SafeGetProcessName(process) ?? "Roblox";
                var peakMeter = SafeGetPeakMeter(session);
                sessions.Add(new RobloxAudioSessionInfo
                {
                    ProcessId = processId,
                    ProcessName = processName,
                    DisplayName = string.IsNullOrWhiteSpace(session.DisplayName) ? processName : session.DisplayName,
                    DeviceName = device.FriendlyName,
                    DeviceId = device.ID,
                    Volume = session.SimpleAudioVolume.Volume,
                    PeakMeter = peakMeter,
                    IsMuted = session.SimpleAudioVolume.Mute,
                    HasAudibleActivity = !session.SimpleAudioVolume.Mute && peakMeter >= 0.01f,
                    State = session.State.ToString(),
                    SessionIdentifier = session.GetSessionIdentifier,
                });
            }
        }

        foreach (var process in GetCandidateRobloxProcesses())
        {
            if (!discoveredProcessIds.Add(process.Id))
            {
                continue;
            }

            var processName = SafeGetProcessName(process) ?? "RobloxPlayerBeta";
            sessions.Add(new RobloxAudioSessionInfo
            {
                ProcessId = process.Id,
                ProcessName = processName,
                DisplayName = string.IsNullOrWhiteSpace(process.MainWindowTitle) ? processName : process.MainWindowTitle,
                DeviceName = "No active audio session yet",
                DeviceId = string.Empty,
                Volume = 0f,
                PeakMeter = 0f,
                IsMuted = false,
                HasAudibleActivity = false,
                State = "Process running",
                SessionIdentifier = $"process:{process.Id}",
            });
        }

        return sessions
            .OrderBy(session => session.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void SetMute(bool mute)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        foreach (var device in devices)
        {
            device.AudioSessionManager.RefreshSessions();
            var sessionCollection = device.AudioSessionManager.Sessions;

            for (var index = 0; index < sessionCollection.Count; index++)
            {
                using var session = sessionCollection[index];
                if (IsRobloxSession(session))
                {
                    session.SimpleAudioVolume.Mute = mute;
                }
            }
        }
    }

    private static bool IsRobloxSession(AudioSessionControl session)
    {
        var processId = (int)session.GetProcessID;
        if (processId <= 0)
        {
            return false;
        }

        try
        {
            return IsRobloxAudioOwner(Process.GetProcessById(processId));
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<Process> GetCandidateRobloxProcesses()
    {
        foreach (var process in Process.GetProcesses())
        {
            if (ShouldReportFallbackProcess(process))
            {
                yield return process;
            }
            else
            {
                process.Dispose();
            }
        }
    }

    private static bool IsRobloxAudioOwner(Process process)
    {
        var processName = SafeGetProcessName(process);
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        if (IsRobloxPlayerProcessName(processName))
        {
            return true;
        }

        try
        {
            var fileName = Path.GetFileNameWithoutExtension(process.MainModule?.FileName ?? string.Empty);
            if (IsRobloxPlayerProcessName(fileName))
            {
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldReportFallbackProcess(Process process)
    {
        if (!IsRobloxAudioOwner(process))
        {
            return false;
        }

        try
        {
            return process.MainWindowHandle != IntPtr.Zero
                || !string.IsNullOrWhiteSpace(process.MainWindowTitle);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRobloxPlayerProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return string.Equals(processName, "RobloxPlayerBeta", StringComparison.OrdinalIgnoreCase)
            || string.Equals(processName, "RobloxPlayer", StringComparison.OrdinalIgnoreCase);
    }

    private static string? SafeGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static float SafeGetPeakMeter(AudioSessionControl session)
    {
        try
        {
            return session.AudioMeterInformation.MasterPeakValue;
        }
        catch
        {
            return 0f;
        }
    }
}
