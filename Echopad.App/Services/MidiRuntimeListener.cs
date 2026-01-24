using System;
using NAudio.Midi;

namespace Echopad.App.Services
{
    /// <summary>
    /// Minimal runtime MIDI listener. Opens one MIDI IN device and emits learned-text tokens.
    /// Does NOT own routing, LED, etc. (we'll add later).
    /// </summary>
    public sealed class MidiRuntimeListener : IDisposable
    {
        private MidiIn? _midiIn;

        public event Action<string>? BindingArrived;

        public void Start(int deviceIndex)
        {
            Stop();

            _midiIn = new MidiIn(deviceIndex);
            _midiIn.MessageReceived += MidiIn_MessageReceived;
            _midiIn.ErrorReceived += (_, __) => { };
            _midiIn.Start();
        }

        public void Stop()
        {
            if (_midiIn == null) return;

            try { _midiIn.MessageReceived -= MidiIn_MessageReceived; } catch { }
            try { _midiIn.Stop(); } catch { }
            try { _midiIn.Dispose(); } catch { }
            _midiIn = null;
        }

        private void MidiIn_MessageReceived(object? sender, MidiInMessageEventArgs e)
        {
            var me = e.MidiEvent;

            string? text = null;

            if (me is NoteOnEvent noteOn && noteOn.Velocity > 0)
                text = $"NoteOn ch{noteOn.Channel + 1} note{noteOn.NoteNumber}";

            else if (me is ControlChangeEvent cc)
                text = $"CC ch{cc.Channel + 1} cc{(int)cc.Controller}";

            if (text != null)
                BindingArrived?.Invoke(text);
        }

        public void Dispose() => Stop();
    }
}
