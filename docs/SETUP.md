# EchoPad setup and menu guide

This guide covers **1.1.0-dev.20260929.3 — Unsigned Dev Release**. Menu examples are renders of the real WPF controls using test settings; device names and meter levels are simulated. The trim section uses a screenshot supplied from a running EchoPad session. The orange update image demonstrates the available-update state.

## Windows startup and tray

![General startup and tray settings](images/current/settings-General.png)

Open **Settings → General**. These options are independent and default to off, including when upgrading an older installation:

| Option | Behavior |
| --- | --- |
| Start with Windows | Starts EchoPad when this Windows user signs in. |
| Open to tray | Opens EchoPad in the notification area without showing its main window. |
| Close to tray | The main window's X or Alt+F4 hides it and keeps the application running. |
| Minimize to tray | Minimizing hides the taskbar button and keeps the tray icon available. |

Audio, MIDI, rolling capture and the drop-folder watcher remain active while hidden. Keyboard pad shortcuts still require EchoPad to be active. Double-click its tray icon to show the window, or right-click for **Open EchoPad**, **Settings** and **Exit EchoPad**. Windows may place the icon under its notification-area arrow. Launching EchoPad again restores the existing instance; an automatic Windows startup launch does not force an already running window to open.

**Exit EchoPad** quits even with Close to tray enabled. A pending capture is allowed to finish saving; playing audio does not delay exit. The settings window's own **Close** button only saves and closes settings. Installer updates and Windows shutdown also exit the application.

Start with Windows registers the current executable for this user, so install or extract EchoPad into its intended permanent folder before enabling it. Launching after an upgrade refreshes that path. Disable the option to remove EchoPad's startup entry. Open to tray applies to both manual and automatic first launches; `Echopad.App.exe --show` overrides it for that launch.

The installer uses the dark ElkaSoft layout with EchoPad's portrait waveform image. The application retains its existing Appearance controls; there is no new application theme switch.

## Start with audio routing

Open **Settings → Audio routing**. Choose **Input 1**, **Input 2**, **Main output** or **Monitor output** in the left column.

![Input routing and separate meters](images/current/settings-Audio.png)

Each route has a Local card and a VBAN card. Turn on the one you want; enabling either switches the other off. Both may be off without losing their configuration. Input 1 and Input 2 each maintain a 15-second rolling buffer. The vertical meters show the active source level and hold recent peaks.

For Local input, select a microphone, interface or loopback device. For VBAN, set the remote address, UDP port and stream name to match the sender. The stream name must contain 1–16 ASCII characters. VBAN sends audio over your network; it requires another application or device that supports VBAN.

![Main output routing](images/current/settings-Audio-outputs.png)

**Main output** carries normal pad playback. **Monitor output** is the destination for private previews in pad setup and for pads configured to use the monitor in Edit mode. Select headphones or a separate VBAN stream for the monitor. Confirm its routing in your mixer so previews are not sent to your audience.

Local and VBAN can be mixed between routes. Each route uses one transport at a time.

## MIDI and input colors

![MIDI device and palette settings](images/current/settings-Midi.png)

Choose the MIDI input for triggers and the MIDI output for controller LEDs. Input-specific armed colors distinguish pads buffering Input 1 from those buffering Input 2. Click **Choose color** for the palette; hex fields remain available for exact values. MIDI LED values and on-screen colors are separate settings.

Use the MIDI test control to check the selected output with your controller. Select the correct MIDI ports before using Learn elsewhere.

## Keyboard and MIDI shortcuts

![Global shortcut editors](images/current/settings-Shortcuts.png)

Click a binding field or **Learn** to assign a control. **×** clears it. Keyboard capture supports combinations such as Ctrl+Shift+F12, Ctrl+Alt+A and Shift+Alt+F2. These keyboard shortcuts operate while EchoPad is active.

The page contains Toggle Edit mode, Open settings, Select trim IN, Select trim OUT, and positive/negative 10 ms trim nudges. Trim actions affect the last selected pad in Edit mode.

MIDI Learn accepts notes and CC controls. A CC activates when it crosses the learned threshold; keeping it above that threshold does not repeatedly trigger it. Dropping below the threshold allows another press. The binding display hides raw diagnostic suffixes to keep the control readable.

## Audio folders

![Audio folders and drop-folder controls](images/current/settings-Folders.png)

Add folders to the import library with **Add folder**. Remove an entry with **Remove selected**.

For automatic assignment, choose the **Drop folder** and turn it on. **Use selected library folder** reuses a listed folder, **Open folder** opens it in Explorer, **Default** selects the default Documents location, and **Clear** disables the watcher and clears its path. Pads must have Drop Folder mode enabled to receive new files. Imported audio is assigned to an eligible pad; PNG files belong in the pad Graphics tab instead.

## Profiles

![Profile switching, linking and search](images/current/settings-Profiles.png)

There are 16 profile slots. In **Settings → Profiles**, configure the global profile modifiers and pad MIDI linking, then name and bind each slot. Search by profile name or number to find a slot quickly.

A slot containing only a key uses the global keyboard modifiers. A complete shortcut such as Ctrl+Alt+F1 works directly. MIDI slot switching uses the selected held MIDI control, if one is assigned. Pad MIDI linking can share MIDI alone, or MIDI and hotkeys, from Profile 1; independent bindings remain saved for when linking is turned off.

Use the small profile-number button beside Settings:

| Gesture | Result |
| --- | --- |
| Ctrl+left-click | Move to the next profile |
| Shift+left-click | Open the profile dropdown |
| Right-click | Open the Profiles settings tab |

![Dark profile dropdown](images/current/profile-dropdown.png)

Settings changes are saved automatically. **Save** writes the current settings and keeps the window open; **Close** saves and closes it. Invalid values must be corrected first.

## Pad setup: waveform and routing

Turn on **Edit** and right-click a pad to open its setup.

![Pad and routing tab](images/current/pad-Routing.png)

The audio file, waveform, Play/Pause, trim IN/OUT, Reset and gain remain above the tabs. Double-click the gain slider to reset it to 0 dB. **Save pad** commits the pad settings; **Cancel** discards pending settings changes.

![Complete trim section with waveform, playhead, IN/OUT values, Play and Reset](images/current/trim-section.png)

The white lines mark the trim IN and OUT boundaries. The colored playhead shows the playback position. IN and OUT are shown in milliseconds; **Play** previews the selection and **Reset** restores the full clip.

In **Pad & routing**, enter a name, choose an input, configure monitor playback, and assign the pad hotkey and MIDI trigger.

Choose the source behavior:

- **Echo** captures the current 15-second rolling buffer when an empty armed pad is triggered. The next trigger plays the captured clip.
- **Drop folder** receives new audio from the configured watched folder.
- Leave both off to load audio with **Browse**.

## Pad MIDI feedback

![Pad MIDI feedback tab](images/current/pad-Midi.png)

Set controller LED output values for Loaded/stopped, Playing and Cleared/empty. Enter 0–127, or **OFF** to disable that state. Entering a number enables it again. Raw MIDI messages such as `90 3C 7F` are also supported for controllers that need them. Feedback uses the MIDI output selected in global Settings.

## Pad PNGs and colors

![Stopped and playing PNG settings](images/current/pad-Graphics.png)

The **Graphics** tab has separate Stopped and Playing controls. Choose their colors using the palette, or leave the hex field blank to use the default.

For each state, choose a PNG, select **Fit** or **Crop**, and adjust its opacity:

- **Fit** keeps the whole image visible at its original aspect ratio.
- **Crop** fills the pad, trimming the edges from the center.
- **Opacity** affects only the artwork; pad text and state outlines stay visible.
- **Remove** clears that state's image assignment.

Square images such as 1000×1000 and rectangular images such as 4:3 are supported. PNG transparency is preserved. Files must be under 25 MB with dimensions up to 8192×8192. EchoPad keeps its own copy, so moving the original image will not break the pad. Changing Fit, Crop or opacity leaves the original PNG untouched.

Stopped artwork appears whenever the pad is not playing, including empty/armed pads. If the current state has no image, its normal pad surface is shown.

![Artwork on stopped and playing pads](images/current/main-artwork.png)

## Overall appearance and pad visibility

![Hue wheels and rounded intensity sliders](images/current/settings-Appearance-full.png)

**Settings → Appearance** controls the main background, pad card, card outline, Settings/Edit buttons, button text, EchoPad title, pad surface and pad names/numbers. Hue wheels keep a controlled brightness.

Three rounded sliders adjust all pads together: **Loaded center**, **Playing center**, and **Outlines**. The first two control the strength of the pad's state-color tint. The outline slider controls the bright state rings. Empty pad centers remain subdued. **Reset appearance** restores the global defaults.

![Empty, armed, loaded and playing pads](images/current/main-states.png)

## Copy a pad to several destinations

In Edit mode, hold Ctrl and click the source pad. Its yellow outline marks the selection. Keep holding Ctrl and click each target pad to copy the audio assignment, name, trim, gain, colors and artwork. Target routing, hotkeys and MIDI triggers remain independent.

Release Ctrl, press Escape, leave Edit mode or switch away from the window to end the copy gesture.

## Check for updates

![Manual update checker](images/current/settings-Updates.png)

Open **Settings → Updates** and click **Check for updates**. EchoPad checks public GitHub releases only when you ask. If a newer eligible release exists, the button turns orange and the release name and version appear.

![Example of an available update](images/current/settings-Updates-available.png)

Click the orange **Download and install** button to download the matching Windows installer. A rounded progress bar shows the download, and **Cancel download** keeps EchoPad running. After verifying the download, EchoPad saves settings, waits for pending captures and launches the installer. Once Windows accepts the launch, EchoPad closes. Finish the installer normally and use its **Launch EchoPad** option to reopen the application. A failed download, failed launch or canceled Windows elevation prompt leaves EchoPad open.

![Example installer download in progress](images/current/settings-Updates-downloading.png)

**Open release page** remains available for release notes and manual downloads. Releases without a supported installer and verification metadata use that manual path. Checking alone never installs anything. Development builds include newer development releases; stable builds check stable releases. Offline, rate-limit and unreadable-feed results are reported as errors, not as “up to date.”

This download/install flow begins with build `1.1.0-dev.20260929.2`. Older builds still open GitHub, so use their release-page link to install this build once. Subsequent supported updates can use the new button.

## Saved data and backups

The application installs by default in `C:\Program Files\ElkaSoft\EchoPad`. Settings, profiles, imported PNG copies and captures live in `%LOCALAPPDATA%\ElkaSoft\EchoPad`: `echopad.settings.json`, `profiles.json`, `PadImages` and `Captures`.

On first launch, EchoPad copies saves from the previous `%LOCALAPPDATA%\Echopad` folder, or older executable-folder saves, and recordings from Documents `Echopad\Captures`. Imported profiles/settings are updated to use the copied media. Originals and existing destination settings are kept. Audio loaded from external locations, audio libraries and watched drop folders stay at their configured paths. The default watched drop folder remains Documents `Echopad\Drop`.

Back up the complete new data directory and any external audio together. Stored image/audio paths are absolute; moving the backup to a different Windows user or computer can require reselecting those files. This ZIP build shares the normal EchoPad data folder and is not an isolated portable profile. See [migration details and recovery](MIGRATION.md) before manually moving saved data.
