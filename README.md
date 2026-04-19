# MNTBloxAudio

Windows desktop app for local Roblox audio replacement.

The app watches Roblox's local sound cache in `%TEMP%\Roblox\sounds`, matches cached `RBX...` files against prepared Roblox asset IDs, and swaps the cached file contents with your own local audio.

## How it works

1. Create a rule with an exact Roblox asset ID.
2. Pick a local replacement file or paste a direct `http/https` URL.
3. Save and apply the rule.
4. The app downloads the original Roblox asset for that ID, resolves your replacement source to a local file, auto-converts unsupported replacement formats to a cached MP3 when needed, and remembers the replacement hash.
5. When Roblox has the matching sound cached locally, the app replaces that cached `RBX...` file with your local file.

Important behavior:

- If a rule is enabled, the app will try to replace matching cached audio.
- If a rule is disabled, Roblox keeps the original audio.
- If the matching cache file disappears later, the app disables that rule and clears its prepared state.
- The background proxy watcher is only used to notice Roblox asset re-fetches and trigger a cache re-check. It is not the main replacement path.
- `MP3`, `WAV`, and `OGG` are used directly. Other readable formats such as `M4A` are converted to a cached `MP3` automatically.
- If a source format needs `ffmpeg` and it is not already installed, the app can download a verified local copy into `%AppData%\MNTBloxAudio\tools\ffmpeg`.

## UI

The app is intentionally split into two tabs:

- `Rules`
  - Main workflow
  - Add a rule, set the Roblox asset ID, choose a replacement file or paste a URL, enable or disable the rule, then use `Save + Apply Rule`
  - `Apply All` prepares and applies every enabled rule
- `Advanced`
  - Cached sound list
  - Activity log
  - Auto-apply toggle for Roblox re-fetch detection

## Main workflow

For one rule:

1. Open `Rules`
2. Click `Add`
3. Enter the exact Roblox asset ID
4. Click `Browse File` or paste a URL into the replacement source box
5. Enable the rule
6. Click `Save + Apply Rule`

For all enabled rules:

1. Open `Rules`
2. Make sure the rules you want are enabled
3. Click `Apply All`

## Notes about replacement

- The app replaces the contents of the cached `RBX...` file and keeps the same filename.
- A backup of the original cached file is stored in `%AppData%\MNTBloxAudio\sound-cache-backups`.
- If Roblox is actively using a cache file, the app now skips that file instead of crashing.
- If a cache file is already replaced, the app will not overwrite it again unnecessarily.

## Requirements

- Windows
- .NET SDK 10

The app shell is WPF and the audio/session inspection is Windows-specific.

## Run from source

```powershell
dotnet build .\MNTBloxAudio.slnx
dotnet run --project .\MNTBloxAudio.App
```

## Build / publish

### Debug build

```powershell
dotnet build .\MNTBloxAudio.slnx
```

## Troubleshooting

### The app says no match was found

- Make sure the rule uses the exact Roblox asset ID
- Make sure the rule is enabled
- Make sure the replacement file still exists, or that the replacement URL is still reachable
- Make sure Roblox has already downloaded that sound into `%TEMP%\Roblox\sounds`
- Check the `Advanced` tab and compare the cached sound state against your rule

### The app skips a file while audio is playing

That usually means Roblox currently has the cached file open. The app will skip instead of forcing the write. Try again after the sound stops or after Roblox re-fetches the asset.

### The rule becomes disabled by itself

That happens when the app had seen a matching cache file for the rule earlier, but Roblox later removed that cached file. The rule is disabled and reset to avoid leaving stale prepared state behind.

## Project structure

- [MNTBloxAudio.App](/D:/GitHub/Repositories/MNTBloxAudio/MNTBloxAudio.App) - WPF UI and view model
- [MNTBloxAudio.Core](/D:/GitHub/Repositories/MNTBloxAudio/MNTBloxAudio.Core) - services, models, cache handling, Roblox asset preparation
