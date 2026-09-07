# MNTBloxAudio 1.3.2

- Fixed startup crashes caused by empty, truncated, or invalid settings and cache recovery JSON.
- Save flushed, atomic primary files and backup copies; recover the saved library, device ID, and replacement ownership from a valid backup when possible.
- Preserve damaged files with a `.corrupt-...` suffix. If no settings backup exists, start with defaults and a recovery notice.
- If cache ownership is unrecoverable and original audio backups exist, open the app with cache changes paused rather than risking incorrect restoration.

# MNTBloxAudio 1.3.1

- Fixed disabled sounds remaining stuck on restoration while unrelated Roblox audio was playing. Restore now depends only on exclusive access to the affected cache file.
- Distinguish real file locks from missing/corrupt original backups and permission errors, with automatic retries after recovery.
- Cache restoration continues even if querying Roblox audio sessions fails.

# MNTBloxAudio 1.3.0

- Replaced the rule editor workflow with Search and Stored tabs, rounded search, inline actions, previews, keyboard search, and reduced-motion-aware transitions.
- Added automatic preparation, cache replacement, unlimited locked-file retries, and durable restoration after disabling or removing sounds.
- Kept enabled sounds active across cache eviction; replacement ownership and verified originals prevent cross-restoring unrelated audio.
- Added verified GitHub Release downloads and restart-to-install updates, plus a tag-based Windows release workflow.
- Updated MNTBloxIndex search, API query support, dependencies, and production deployment.
- Validated with cache/update regression checks and rendered WPF layouts.

In 1.3.0, restoration conservatively waited for overall Roblox audio silence and exclusive file access (fixed in 1.3.1). Per-asset playback cannot be identified through Windows audio sessions.

# MNTBloxAudio 1.2.2

Mockup-matching UI refresh with stronger cache auto-replacement behavior.

## UI Refresh

- Reworks the main app layout to match the darker mockup-style interface more closely
- Updates My Audio into a card-based browser with a right-side edit panel
- Carries the same visual language into the Uploading and Song Index tabs
- Uses visual rule states instead of direct status text on audio cards

## Audio State Colors

- Blue outlines now represent audio that is enabled and actively replaced in Roblox's cache
- Adds a separate ready color for rules that match the original Roblox cache and are prepared to replace it
- Keeps inactive or original rules in a muted gray state
- Adds runtime rule state tracking so the UI reflects whether a rule is currently original, ready, or active

## Automatic Cache Replacement

- Enabling a rule now also turns on automatic cache re-checks for new Roblox sound files
- Newly detected cache entries are checked against recent asset detections and enabled prepared rules
- If Roblox is still using a cache file, the app now waits and retries for a short window instead of immediately giving up
- Disabling a rule restores matching replaced cache entries back to the original Roblox audio when possible

## Notes

- Automatic replacement still depends on Roblox having downloaded the target sound into `%TEMP%\Roblox\sounds`
- If Roblox keeps a cache file locked for too long, the app will eventually stop retrying and log the skip in Activity

# MNTBloxAudio 1.2.1

Song index targeting update with linked Roblox sound IDs and improved preview controls.

## Linked Roblox IDs

- Song index entries can now store a linked Roblox sound ID alongside the replacement source
- Using a 6-letter song code in the app now auto-fills the selected rule's target Roblox sound ID when that entry has one
- Song search and upload views now show the linked Roblox sound ID for each entry when available

## Upload And Index Updates

- Uploads now include a required linked Roblox sound ID that tells the app which Roblox sound the entry is meant to replace
- Roblox source sound IDs can still be used to autofill replacement metadata and a direct `assetdelivery.roblox.com` audio URL
- Updates the public index schema to include linked Roblox sound IDs for new entries

## Preview And Workflow

- Adds a dedicated `Stop Rule Preview` control for rule playback
- Keeps the regular preview stop controls for typed URLs, uploaded songs, and indexed song previews
- Improves the "Use Code In Rule" workflow so song codes can act more like a reusable linked replacement entry

## Notes

- Older song index entries may not have a linked Roblox sound ID yet
- Entries without a linked Roblox sound ID still resolve as replacement sources, but they will not auto-fill a rule target

# MNTBloxAudio 1.2.0

Local Roblox audio replacement through Roblox's sound cache, now with song index support.

## Song Index

- Supports 6-letter song codes as replacement sources
- Resolves song codes through the public MNTBloxIndex API
- Adds a native in-app Songs tab that searches the index through API requests
- Lets you preview indexed songs, copy their codes, and apply a code directly to the selected rule
- Supports a configurable song index base URL in the app

## Uploads And Preview

- Adds an Upload tab for submitting direct audio links to the public song index
- Stores uploads by device ID so the same device can delete its own submissions later
- Adds preview controls for typed audio URLs and selected uploaded songs
- Adds stop-preview controls in the app
- Cleans up invalid saved upload entries from older builds automatically

## App Updates

- Adds a dark mode toggle in the Advanced tab
- Improves upload response handling so saved submissions keep their code, song name, and artist correctly
- Keeps the song index workflow link-only; audio files are not mirrored or uploaded by MNTBloxAudio

## Notes

- Song index entries store direct audio links, not hosted audio copies
- If a remote audio URL stops working, its song code will no longer resolve to a playable source

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
