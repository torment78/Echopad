# Unsigned Dev Release

Version: **1.1.0-dev.20260929.3** · Windows x64 · Prerelease

## Changes

- New **General** settings tab with independent Start with Windows, Open to tray, Close to tray and Minimize to tray switches. All default off.
- Tray menu with Open EchoPad, Settings and Exit EchoPad; double-click restores the window. Repeated manual launches open the existing instance.
- Hidden startup initializes audio, MIDI and rolling capture. Hiding the window no longer tears down those services. Actual exit waits for pending capture saves, without waiting for playing clips to finish. Updates and Windows shutdown bypass close-to-tray.
- Startup is registered for the current Windows user, with a quoted executable path that refreshes after relocation. Turning the option off removes the entry; uninstall removes a matching entry for the uninstalling user.
- Dark installer follows Throttle, VBAN Stream and VBAN Plug: larger modern wizard, EchoPad portrait waveform artwork, ElkaSoft logo and updated welcome/finish text. Existing application appearance controls remain unchanged.
- Updated settings illustrations include the General tab. Tray construction preserves the host's async context.

Also included from the preceding development builds:

- Installer now defaults to `C:\Program Files\ElkaSoft\EchoPad`, including upgrades from the old installation folder.
- Writable data now lives in `%LOCALAPPDATA%\ElkaSoft\EchoPad`. First launch copies older settings, profiles, pad images and captures, rewrites imported media references, and keeps originals. Existing destination settings win; conflicting media are retained under distinct names. Failed migrations report an error and can be retried.
- Matching portrait/landscape waveform graphics retain the pad-icon design, with a 373,410-byte horizontal preview. Artwork and prompts ship in the repository and application packages.
- The installer preserves old custom install locations for data discovery. The application migrates as the Windows user who launches it.
- Settings can now download the matching installer, show progress/cancellation, verify its SHA-256 and size, save settings and wait for pending captures, launch the installer and close EchoPad. Failed downloads, canceled Windows prompts and failed launches keep the app open. Older builds need one manual installation to gain this workflow.

The following interface changes from the previous development build are also included:

- Eight settings tabs: General, Audio routing, MIDI & colors, Shortcuts, Audio folders, Profiles, Appearance and Updates.
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

The Windows Release regression harness passes **141 checks**, including:

- Startup registration and relocation, defaults and persistence, profile independence, failed registration preserving saved settings, single-instance activation, native icon loading, tray restore/minimize/close/explicit exit, capture-save waits and update shutdown bypass.
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

UI examples use generated pad artwork and simulated devices/meter levels; the trim illustration is a supplied screenshot from a running EchoPad session. The native tray resource loads in a smoke check, and window lifecycle tests use an injected tray backend. Startup tests use a fake registry store. A real Windows sign-in and live tray interaction are not part of the automated checks. Physical audio interfaces, real MIDI controllers and a remote VBAN receiver still require a user audition.

Both the installer and a non-installing branding preview compile. Live wizard visual inspection was blocked by the desktop tool's Windows sandbox startup error; the shipped portrait artwork and rendered settings pages were visually reviewed.

## Downloads and signing

The self-contained Windows x64 ZIP includes the runtime, illustrated guide and promotional graphics. Extract the whole ZIP before launching `Echopad.App.exe`. The installer installs the same application payload. SHA-256 checksums accompany the assets. The installer is compiled, migration uses isolated fixtures, and launcher success/cancellation is simulated; a real installation over personal saved data is not part of the automated checks.

This is an **unsigned development build**. Code signing will be added to a future release. It shares the normal EchoPad settings/profile directory; see [backup and data locations](SETUP.md#saved-data-and-backups).
