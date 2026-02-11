using System.Collections.Generic;
using Echopad.Core;

namespace Echopad.App.Services
{
    public sealed class ProfileData
    {
        public int ProfileIndex { get; set; } = 1; // 1..16
        public string? Name { get; set; }

        public Dictionary<int, PadSettings> Pads { get; set; } = new();
    }
}
