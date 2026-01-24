namespace Echopad.Core.Devices
{
    public sealed class DeviceItem
    {
        public string Id { get; }
        public string Name { get; }

        public DeviceItem(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Name;
    }
}
