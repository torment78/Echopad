using Echopad.Core.Devices;
using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;

namespace Echopad.Audio
{
    public sealed class AudioDeviceProvider : IAudioDeviceProvider
    {
        public IReadOnlyList<DeviceItem> GetInputDevices()
        {
            var list = new List<DeviceItem>();

            // System default capture
            list.Add(new DeviceItem("", "(System Default Input)"));

            // CAPTURE endpoints (mics, etc.)
            list.AddRange(Enumerate(DataFlow.Capture, prefix: "", labelPrefix: ""));

            // RENDER endpoints exposed as LOOPBACK targets
            // These will be selected from the SAME "Input" dropdown,
            // but stored with "loop:" prefix so InputTapEngine can choose WasapiLoopbackCapture.
            list.AddRange(Enumerate(DataFlow.Render, prefix: "loop:", labelPrefix: "Loopback: "));

            return list;
        }

        public IReadOnlyList<DeviceItem> GetOutputDevices()
        {
            var list = new List<DeviceItem>();
            list.Add(new DeviceItem("", "(System Default Output)"));
            list.AddRange(Enumerate(DataFlow.Render, prefix: "", labelPrefix: ""));
            return list;
        }

        private static IReadOnlyList<DeviceItem> Enumerate(DataFlow flow, string prefix, string labelPrefix)
        {
            var list = new List<DeviceItem>();

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);

                foreach (var d in devices)
                {
                    var id = d.ID;
                    var name = d.FriendlyName;

                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    list.Add(new DeviceItem(prefix + id, labelPrefix + name));
                }
            }
            catch
            {
                // keep whatever we have
            }

            return list;
        }
    }
}
