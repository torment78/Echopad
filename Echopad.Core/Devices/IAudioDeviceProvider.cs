using System.Collections.Generic;

namespace Echopad.Core.Devices
{
    public interface IAudioDeviceProvider
    {
        IReadOnlyList<DeviceItem> GetInputDevices();
        IReadOnlyList<DeviceItem> GetOutputDevices();
    }
}
