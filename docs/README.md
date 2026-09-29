# EchoPad overview

EchoPad is a Windows 4×4 audio pad sampler with 16 profiles, two rolling capture inputs, a main output and a separate monitor output. It uses local audio devices or VBAN network audio.

![Main pad grid](images/current/main-states.png)

## Pad behavior

Empty pads have no clip. Echo pads with no clip are armed and buffer their selected input. Triggering an armed Echo pad commits the last 15 seconds to a WAV file; triggering a loaded pad starts playback. Playing pads use their running color and optional playing artwork. Each pad keeps its audio, trim, name, gain, source settings, keyboard/MIDI trigger, MIDI LED feedback and graphics.

Run mode is for capture and playback. Edit mode provides trimming, pad setup and Ctrl-click copying. Private previews use the monitor output configured in Settings.

## This development release

The interface now has eight global settings tabs and three pad settings tabs. General adds Windows startup, open/close/minimize to tray and an Open/Settings/Exit tray menu. It also includes profile names/search, a Shift-click profile picker, improved modifier shortcuts and CC handling, stronger loaded/playing colors, global hue and intensity controls, stopped/playing PNG artwork with Fit/Crop and opacity, and a GitHub update checker with verified installer downloads.

![Pad graphics editor](images/current/pad-Graphics.png)

Read the [illustrated setup and menu guide](SETUP.md) for every tab and control, or the [release notes](RELEASE-NOTES.md) for validation details.

## Routing and storage

Each input/output route can use Local or VBAN, or be disabled. Local and VBAN cannot run simultaneously on the same route. Other routes can use different transports.

The app installs in `C:\Program Files\ElkaSoft\EchoPad`. Settings, profiles, managed PNG copies and captures are stored under `%LOCALAPPDATA%\ElkaSoft\EchoPad`. Older saves and owned media are copied automatically on first launch, keeping the originals. External audio and watched folders retain their paths. See [migration details](MIGRATION.md) and the [backup notes](SETUP.md#saved-data-and-backups) before moving data between machines.

## Development

This is the normal EchoPad application. The planned VST endpoint edition is separate future work. This release is unsigned and published as a prerelease.

See the [repository README](../README.md) for downloads, build commands and tests.
