# Unsigned Dev Release

Version: **1.1.0-dev.20260929.2** · Windows x64 · Prerelease

## Changes

- Installer now defaults to `C:\Program Files\ElkaSoft\EchoPad`, including upgrades from the old installation folder.
- Writable data now lives in `%LOCALAPPDATA%\ElkaSoft\EchoPad`. First launch copies older settings, profiles, pad images and captures, rewrites imported media references, and keeps originals. Existing destination settings win; conflicting media are retained under distinct names. Failed migrations report an error and can be retried.
- Matching portrait/landscape waveform graphics retain the pad-icon design, with a 373,410-byte horizontal preview. Artwork and prompts ship in the repository and application packages.
- The installer preserves old custom install locations for data discovery. The application migrates as the Windows user who launches it.
- Settings can now download the matching installer, show progress/cancellation, verify its SHA-256 and size, save settings and wait for pending captures, launch the installer and close EchoPad. Failed downloads, canceled Windows prompts and failed launches keep the app open. Older builds need one manual installation to gain this workflow.

The following interface changes from the previous development build are also included:

- Seven settings tabs: Audio routing, MIDI & colors, Shortcuts, Audio folders, Profiles, Appearance and Updates.
- Local/VBAN cards with on/off controls and separate vertical input meters.
- Clearer whole-pad loaded and playing colors, brighter rings and centered names.
- Rounded global sliders for loaded fill, playing fill and outline strength, plus hue wheels and palette pickers.
- Profile names/search, dark Shift-click selection, and profile configuration inside Settings.
- Readable Ctrl/Shift/Alt hotkeys, corrected CC threshold/release behavior and restored trim bindings.
- Ctrl-click copy to multiple pads, with selection cleared when Ctrl is released or the gesture is canceled.
- Pad editor tabs for routing/triggers, MIDI LED feedback and graphics; waveform/trim/gain remain above them.
- Separate stopped/playing PNGs, opacity, Fit and centered Crop. Imported PNGs are retained in the EchoPad data folder.
- Manual GitHub update checks with an orange available-update button, release name/version and a browser link.
- Waveform loading now reads WPF dimensions on the UI thread and ignores obsolete asynchronous results.
- Updated illustrated documentation with actual WPF menu renders.

The normal 15-second capture and playback workflow remains in place. VST endpoint support is not included.

## Validation

The Windows Release regression harness passes **111 checks**, including:

- 17 migration checks: legacy layouts, source priority, named/active profiles, recordings, both PNG states, Unicode, trim/routing/bindings, preservation of originals and newer destination data, external-path preservation, collision handling, one-time behavior, and failure/retry safety.
- 15-second buffer ordering and PCM WAV export.
- Real asynchronous waveform loading in an offscreen WPF host.
- Ctrl-copy lifecycle and target-routing preservation.
- Modifier chords, MIDI CC edge/release behavior and profile linking/persistence.
- VBAN monitor preview framing and pause/stop through localhost UDP.
- PNG import validation, managed-copy persistence, profile storage, Fit/Crop, opacity and rounded clipping.
- Update version ordering, development/stable channels, malformed/offline/rate-limited feeds and available-update UI.
- Installer asset selection, URL/size/hash validation, progress, successful handoff ordering, corrupted/truncated/oversized downloads, cancellation, foreign redirects, HTTP errors, save failure and canceled/failed installer launches.
- Rendering every settings/pad tab, compact layout checks and no WPF binding errors.

The public GitHub release endpoint was also checked successfully. UI examples use generated artwork and simulated devices/meter levels; the trim illustration is a supplied screenshot from a running EchoPad session. Physical audio interfaces, real MIDI controllers and a remote VBAN receiver still require a user audition; the automated tests do not claim that hardware coverage.

## Downloads and signing

The self-contained Windows x64 ZIP includes the runtime, illustrated guide and promotional graphics. Extract the whole ZIP before launching `Echopad.App.exe`. The installer installs the same application payload. SHA-256 checksums accompany the assets. The installer is compiled, migration uses isolated fixtures, and launcher success/cancellation is simulated; a real installation over personal saved data is not part of the automated checks.

This is an **unsigned development build**. Code signing will be added to a future release. It shares the normal EchoPad settings/profile directory; see [backup and data locations](SETUP.md#saved-data-and-backups).
