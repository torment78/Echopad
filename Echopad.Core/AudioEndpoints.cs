using System;

namespace Echopad.Core
{
    
    // INPUT: Local capture or loopback, or VBAN RX
    public sealed class InputEndpointSettings
    {
        public AudioEndpointMode Mode { get; set; } = AudioEndpointMode.Local;

        // Local
        public string? LocalDeviceId { get; set; }      // mic id or "loop:{renderId}"
        public bool LocalIsLoopback { get; set; }       // optional hint for UI

        // VBAN RX
        public VbanRxSettings Vban { get; set; } = new();
    }

    // OUTPUT: Local WASAPI render, or VBAN TX
    public sealed class OutputEndpointSettings
    {
        public AudioEndpointMode Mode { get; set; } = AudioEndpointMode.Local;

        // Local
        public string? LocalDeviceId { get; set; }

        // VBAN TX
        public VbanTxSettings Vban { get; set; } = new();
    }

    public sealed class VbanRxSettings
    {
        public string RemoteIp { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 6980;
        public string StreamName { get; set; } = "ECHO_IN";
        public int JitterMs { get; set; } = 60; // simple initial buffer
    }

    public sealed class VbanTxSettings
    {
        public string RemoteIp { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 6980;
        public string StreamName { get; set; } = "ECHO_OUT";

        // Keep v1 simple and stable: fixed format (no resampler yet)
        public int SampleRate { get; set; } = 48000;
        public int Channels { get; set; } = 2;
        public bool Float32 { get; set; } = true;

        // Samples PER CHANNEL in each packet/frame (keep small to reduce latency)
        public int FrameSamples { get; set; } = 256;
    }
}
