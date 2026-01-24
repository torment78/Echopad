using System.Collections.Generic;

namespace Echopad.Core.Devices
{
    public interface IMidiDeviceProvider
    {
        IReadOnlyList<DeviceItem> GetMidiInputs();
        IReadOnlyList<DeviceItem> GetMidiOutputs();
    }
}
