using System;
using System.Threading.Tasks;
using Echopad.Core;

namespace Echopad.Audio
{
    public interface IAudioEngine
    {
        event Action<int>? PadPlaybackEnded; // padIndex

        // LEGACY signature (used by older UI)
        Task PlayPadAsync(PadModel pad, string? mainOutDeviceId, string? monitorOutDeviceId, bool previewToMonitor);

        // NEW signature (used by your current MainWindow endpoint code)
        Task PlayPadAsync(PadModel pad, OutputEndpointSettings out1, OutputEndpointSettings out2, bool previewToMonitor);

        void StopPad(PadModel pad);
    }
}
