# MNTBloxAudio

Windows desktop app for local Roblox audio overrides.

## What it does

- Detects active Roblox audio sessions from Windows render devices using WASAPI-compatible APIs.
- Plays local replacement audio files through a chosen output device.
- Can temporarily mute Roblox during replacement playback.
- Uses an experimental proxy path to detect `assetdelivery.roblox.com` asset IDs so local files can replace matched Roblox songs.
- Shows live proxy request telemetry in the app so you can tell whether Roblox traffic is reaching the proxy.

## Why the design looks like this

The audio-device layer is still the most reliable place to control what the player hears, but specific song replacement needs asset IDs. This app therefore uses the proxy path to detect Roblox asset requests, then uses local session control and local playback to replace matched songs on the client.

## Run

```powershell
dotnet build .\MNTBloxAudio.slnx
dotnet run --project .\MNTBloxAudio.App
```

## Notes

- Asset-pattern rules are meant to trigger from proxy-detected asset IDs.
- Blank-pattern rules can still be used as a fallback for generic Roblox-audio-start replacement.
- If you enable the proxy fallback, Windows may need to trust a local root certificate and temporarily use `127.0.0.1:<port>` as the system proxy.
- For the cleanest primary setup, route Roblox into a dedicated endpoint or virtual cable and let this app own local replacement playback.
