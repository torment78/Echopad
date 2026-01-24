🔊 Audio Setup (First-Time Setup)

Open Settings → Audio.

Configure:

Input 1 – main capture source (mic, mixer, etc.)

Input 2 – secondary capture source

Main Output – where pads play

Monitor Output – optional preview output

These inputs are also used by Echo Mode.

🎙️ Echo Mode (Live Capture)

Echo Mode allows you to capture the last few seconds of live audio and assign it to a pad.

How it works

Echopad continuously buffers audio from inputs

Buffer length ≈ 15 seconds

When you press an empty pad:

The last buffer is written to WAV

File is assigned to that pad

Pad becomes Loaded

When Echo Mode is ON:

Empty pad shows Armed color

Pressing the pad commits the buffer

This is perfect for:

stream highlights

call-outs

instant replays

voice effects

🎨 Pad Colors (Per-Pad)

Each pad can override its colors:

Active color (Loaded / Armed)

Running color (Playing)

You can:

click the color box to open a picker

paste a HEX value manually

If no override is set:

theme defaults are used

🎹 MIDI Support
MIDI Input

Trigger pads

Toggle Edit Mode

Open Settings

Learn mode supported

MIDI Learn

Click Learn

Press a MIDI pad / key

Binding is stored instantly

MIDI Output (LED feedback)

Each pad sends LED values:

Active

Running

Clear

Values are configurable per pad and globally.

📁 Drop Folder Mode

You can enable Drop Folder Mode on any pad.

How it works:

Files dropped into the folder:

auto-assign to the next eligible pad

pads marked for drop receive files first

Great for:

fast sample loading

drag-and-drop workflows

external automation

✂️ Audio Trimming

In Edit Mode:

hover over trim fields

use mouse wheel to adjust

keyboard trim hotkeys supported

Trimming is non-destructive and saved per pad.

⌨️ Keyboard Hotkeys

You can bind:

Toggle Edit Mode

Open Settings

Trigger pads

Trim controls

Hotkeys are human-readable (e.g. Ctrl+Shift+F1) and portable.

💾 Settings & Portability

All settings stored in:

echopad.settings.json


Located next to the EXE

No registry usage

Fully portable

Safe to back up or version control

🛠️ Intended Use Cases

Live streaming

Podcasting

Radio production

Voice effects

Soundboards

MIDI performance rigs

OBS / VoiceMeeter setups

🚧 Current Status

Actively developed

Stable core architecture

Installer planned (Inno Setup)

Feature-complete for live use

📜 License

(Your license here)

🙌 Credits

Built with:

.NET / WPF

NAudio

MIDI via NAudio

Custom audio engine