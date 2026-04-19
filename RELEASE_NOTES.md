# MNTBloxAudio 1.0.0

Local Roblox audio replacement through Roblox's sound cache.

## Highlights

- Replaces matching Roblox cached audio in `%TEMP%\Roblox\sounds`
- Rule-based workflow with exact Roblox asset IDs
- Minimal tabbed UI with a simplified main Rules tab
- `Save + Apply Rule` for one-click prepare + apply
- `Apply All` for all enabled rules
- `Reset + Refresh` restores originals, re-prepares enabled rules, and reapplies from a clean state
- Automatic backup/restore of original Roblox cache files
- Busy-file handling so active Roblox audio does not crash the app

## Cache Matching

- Prepares rules by downloading the original Roblox asset and hashing it
- Matches prepared rules against the local Roblox cache by SHA-256
- Keeps the original `RBX...` cache filename and only replaces file contents
- Stores original backups in `%AppData%\MNTBloxAudio\sound-cache-backups`
- Disables a rule automatically if its matched cache file disappears later

## UI / Workflow

- Cleaner minimal layout with Rules and Advanced tabs
- Cache list with replacement state visibility
- Scrollable activity output
- Better rule state display, including auto-converted source hints
- File browser support for replacement source selection

## Notes

- Replacement only works after Roblox has downloaded the target sound into its local cache
- If Roblox is actively using a cache file, the app skips the write instead of forcing it
- The proxy watcher is only used for background asset re-fetch detection and cache re-checks, not as the main replacement path

## Known Limitations

- If a target sound is already playing, Roblox may lock the cache file and prevent immediate replacement

# MNTBloxAudio 1.1.0

Local Roblox audio replacement through Roblox's sound cache.

## Replacement Sources

- Supports local replacement files
- Supports direct `http/https` replacement URLs
- Uses `MP3`, `WAV`, and `OGG` directly
- Auto-converts other formats such as `M4A` to cached `MP3` files when needed
- Can use `ffmpeg` for broader conversion support
- If `ffmpeg` is not already available, the app can download a verified local copy into `%AppData%\MNTBloxAudio\tools\ffmpeg`

## Known Limitations

- Some replacement formats may still depend on local decoder support or `ffmpeg`