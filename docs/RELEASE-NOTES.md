# Unsigned Dev Release

Version: **1.1.0-dev.20260929.1** · Windows x64 · Prerelease

## Changes

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

The Windows Release regression harness passes **70 checks**, including:

- 15-second buffer ordering and PCM WAV export.
- Real asynchronous waveform loading in an offscreen WPF host.
- Ctrl-copy lifecycle and target-routing preservation.
- Modifier chords, MIDI CC edge/release behavior and profile linking/persistence.
- VBAN monitor preview framing and pause/stop through localhost UDP.
- PNG import validation, managed-copy persistence, profile storage, Fit/Crop, opacity and rounded clipping.
- Update version ordering, development/stable channels, malformed/offline/rate-limited feeds and available-update UI.
- Rendering every settings/pad tab, compact layout checks and no WPF binding errors.

The public GitHub release endpoint was also checked successfully. UI examples use generated artwork and simulated devices/meter levels. Physical audio interfaces, real MIDI controllers and a remote VBAN receiver still require a user audition; the automated tests do not claim that hardware coverage.

## Downloads and signing

The self-contained Windows x64 ZIP includes the runtime and illustrated guide. Extract the whole ZIP before launching `Echopad.App.exe`. The installer installs the same application payload. SHA-256 checksums accompany the assets.

This is an **unsigned development build**. Code signing will be added to a future release. It shares the normal EchoPad settings/profile directory; see [backup and data locations](SETUP.md#saved-data-and-backups).
