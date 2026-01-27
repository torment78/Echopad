namespace Echopad.Core
{
    /// <summary>
    /// How an endpoint is routed.
    /// Local = WASAPI device (normal)
    /// Vban  = VBAN over UDP (network)
    /// </summary>
    public enum AudioEndpointMode
    {
        Local = 0,
        Vban = 1
    }
}
