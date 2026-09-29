using System.Collections.Generic;
namespace Echopad.App.Services;

/// <summary>Tracks CC threshold crossings so a held button or moving fader fires once per press.</summary>
public sealed class MidiCcEdges
{
    private readonly Dictionary<(int Channel, int Number), int> _values = new();
    private readonly Dictionary<(int Channel, int Number), int> _previous = new();
    public void Observe(int channel, int number, int value)
    {
        var key = (channel, number); _previous[key] = _values.GetValueOrDefault(key); _values[key] = value;
    }
    public bool Crossed(int channel, int number, int threshold) => _previous.GetValueOrDefault((channel, number)) < threshold && _values.GetValueOrDefault((channel, number)) >= threshold;
    public void ObserveSuppressedRelease(int channel, int number, int value)
    {
        if (value < _values.GetValueOrDefault((channel, number))) Observe(channel, number, value);
    }
    public void Clear() { _values.Clear(); _previous.Clear(); }
}
