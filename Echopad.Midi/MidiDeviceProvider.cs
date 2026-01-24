using Echopad.Core.Devices;
using NAudio.Midi;
using System.Collections.Generic;

namespace Echopad.Midi
{
    public sealed class MidiDeviceProvider : IMidiDeviceProvider
    {
        public IReadOnlyList<DeviceItem> GetMidiInputs()
        {
            var list = new List<DeviceItem>();

            for (int i = 0; i < MidiIn.NumberOfDevices; i++)
            {
                var info = MidiIn.DeviceInfo(i);
                list.Add(new DeviceItem($"midi-in:{i}", info.ProductName));
            }

            return list;
        }

        public IReadOnlyList<DeviceItem> GetMidiOutputs()
        {
            var list = new List<DeviceItem>();

            for (int i = 0; i < MidiOut.NumberOfDevices; i++)
            {
                var info = MidiOut.DeviceInfo(i);
                list.Add(new DeviceItem($"midi-out:{i}", info.ProductName));
            }

            return list;
        }
    }
}
