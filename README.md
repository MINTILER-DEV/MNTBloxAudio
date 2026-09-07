# MNTBloxAudio

A simpler Windows app for local Roblox audio replacement.

## Use it

1. Open **Search**. Enter a song, artist, Roblox sound ID, or six-letter song code and press Enter or Search.
2. Select a result. **Store for later** saves it without changing Roblox. **Enable replacement** stores and enables it in one step.
3. Open **Stored** to enable, disable, edit, or remove your sounds. **Local audio** lets you choose a file and enter the Roblox sound ID it should replace.

Enabling prepares the source in the background and watches `%TEMP%\Roblox\sounds`. Matching originals are replaced automatically, including when Roblox downloads them again. Only one replacement is enabled per Roblox sound ID. Stored files, direct HTTP/HTTPS audio links, and song codes are supported; readable formats beyond MP3/WAV/OGG are converted automatically.

Disabling or removing a sound queues restoration of its original. The queue survives application restarts and retries without a time limit while the app is open. Keep MNTBloxAudio open for automatic replacement and restoration.

Windows exposes overall Roblox audio activity, not playback of individual asset IDs. Restoration checks exclusive access to the specific cache file on every monitor pass; other Roblox sounds do not block it. Missing or damaged backups and permission errors are reported separately from files that are still open. Sounds already decoded into Roblox memory cannot be changed retroactively. The new cached bytes take effect when Roblox reads the file again.

## Share audio and device ID

**Share audio** opens the upload page with your saved app device ID filled automatically. New installations create and save an ID on startup. The browser remembers that ID for later direct visits, and **Copy device ID** in the app or **Copy ID** on the upload page lets you reuse it elsewhere. You can also paste a copied ID into the upload page.

The app passes the ID in the URL fragment; the page saves it locally and removes it from the address bar. A first browser-only visit creates an ID automatically. If browser storage or clipboard access is unavailable, the page keeps the current ID usable and offers manual copying.

## Updates

The published `MNTBloxAudio.exe` automatically checks stable GitHub Releases from `MINTILER-DEV/MNTBloxAudio` on startup, downloads newer releases, and verifies the SHA-256 digest and size provided by GitHub. Choose **Restart to update** to install. The helper waits for the application to save and exit, atomically replaces the executable, and keeps a `.previous` backup. Settings and recovery data stay in AppData.

Offline checks leave the app usable; click the update status to retry. Development builds cannot install over themselves. Update installation requires a writable application folder. Failed installs are logged under `%LocalAppData%\MNTBloxAudio\updates\<version>\install.log`.

To publish a future update, bump the project version and push the matching `vX.Y.Z` tag. `.github/workflows/release.yml` tests and publishes the executable to GitHub Releases. A repository commit alone is not an executable update.

## Build and test

Requires Windows and the .NET 10 SDK.

```powershell
dotnet build MNTBloxAudio.slnx
dotnet run --project MNTBloxAudio.Tests
.\buildproj.bat
```

The test harness covers cache ownership, busy-file retries, restoration after restart/removal, legacy backup migration, source integrity, and version parsing. It also renders the real WPF Search and Stored templates to `artifacts/ui` without starting Roblox monitoring or changing the saved library.

The single-file build is written to `publish/MNTBloxAudio.exe`.

## Data and recovery

Settings live in `%AppData%\MNTBloxAudio\settings.json`, with atomic saves. Original audio and the replacement ownership manifest live in `sound-cache-backups` beside it. Do not delete that folder while replacements are active. Backups from earlier releases are imported when their original and replacement hashes match saved rules.

MNTBloxIndex is maintained in the separate nested repository and deployed at https://mntbloxindex.vercel.app. **Share audio** opens its submission page.

### Startup recovery

Starting with 1.3.2, settings and cache recovery records have a `.backup` copy. Empty or invalid JSON no longer prevents startup: the app recovers a valid backup, or starts with default settings if none exists. Damaged files are preserved with a `.corrupt-...` suffix, and a status message explains the recovery.

If cache ownership records are unrecoverable while original audio backups exist, automatic cache changes are paused. The app keeps those original audio files intact; it cannot safely infer which original belongs to an already replaced file. Keep the recovery folder for diagnosis instead of deleting its backups.
