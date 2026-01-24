# 🛠️ Echopad – Setup & Usage Guide

This guide covers first-time setup, per-pad configuration, and everyday usage.

Follow the steps in order.

---

## 1️⃣ Audio Setup

Open **Settings → Audio**.

### Audio Inputs

![Audio input setup](images/setup-audio-input.png)

- **Input 1** – primary live capture source
- **Input 2** – secondary live capture source

These inputs are used by Echo Mode and per-pad routing.

---

### Main Output

![Main output setup](images/setup-audio-output.png)

- Select the main playback device
- This is where pads play during Run Mode

---

### Monitoring Output

![Monitoring output setup](images/setup-audio-monitoring.png)

- Optional preview output
- Used when previewing pads in Edit Mode

---

## 2️⃣ MIDI Setup

Open **Settings → MIDI**.

### MIDI Input

![MIDI input setup](images/setup-midi-input.png)

- Select your MIDI controller
- This device triggers pads and global actions

---

### MIDI Output (LED Feedback)

![MIDI return setup](images/setup-midi-return.png)

- Optional but recommended
- Sends pad state back to the controller
- Enables LED feedback for pad states

---

## 3️⃣ Enter Edit Mode

Click the **Edit** button in the top bar.

![Edit button](images/edit-bar-pad.png)

Edit Mode enables configuration instead of playback.

---

## 4️⃣ Edit Mode Active

![Edit mode active](images/edit-per-pad-active.png)

When Edit Mode is active:
- Pads no longer play audio
- Pads open configuration instead

---

## 5️⃣ Open Per-Pad Settings

Right-click any pad while in Edit Mode.

![Right click pad](images/right-click-pad.png)

This opens the per-pad settings window.

---

## 6️⃣ Per-Pad Settings – Audio & Input

![Pick audio input](images/edit-per-pad-window-pick-audio-input.png)

From here you can:
- Assign an audio file
- Select Input 1 or Input 2
- Define which input Echo Mode captures from

---

## 7️⃣ Per-Pad Settings – Echo & Drop Folder

![Echo and drop folder](images/edit-per-pad-window-pick-echo-mode.png)

Options include:
- **Echo Mode** – enables live capture
- **Drop Folder Mode** – auto-assign files from a folder

---

## 8️⃣ Trimming Audio (Edit Mode)

Hover over trim values and use the mouse wheel.

### Trim In

![Trim in](images/trim-in.png)

Adjusts the playback start position.

---

### Trim Out

![Trim out](images/trim-out.png)

Adjusts the playback end position.

---

## 9️⃣ Run Mode Usage

Exit Edit Mode.

Pads now:
- Play audio
- Capture live audio if armed
- Respond to MIDI and keyboard triggers

---

## 🔁 Typical Workflow

1. Configure audio and MIDI
2. Enter Edit Mode
3. Configure pads and Echo Mode
4. Trim audio if needed
5. Exit Edit Mode
6. Perform live

---

## 💾 Settings Persistence

All configuration is saved automatically to:

echopad.settings.json

No manual saving is required.

---

## ✅ Setup Complete

Echopad is now ready for daily use and live performance.