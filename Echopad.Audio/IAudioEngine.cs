using System;
using System.Threading.Tasks;
using Echopad.Core;

namespace Echopad.Audio
{
    public interface IAudioEngine
    {
        // NEW: fired when a pad finishes (or stops) so UI can reset state
        event Action<int>? PadPlaybackEnded; // padIndex

        // Output routing driven by GlobalSettings device IDs
        Task PlayPadAsync(PadModel pad, string? mainOutDeviceId, string? monitorOutDeviceId, bool previewToMonitor);

        void StopPad(PadModel pad);
    }
}
