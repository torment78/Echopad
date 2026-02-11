using System.Collections.Generic;

namespace Echopad.App.Services
{
    public sealed class ProfileStore
    {
        public int ActiveProfileIndex { get; set; } = 1; // 1..16

        // 1..16 profiles in one file
        public List<ProfileData> Profiles { get; set; } = new();
    }
}
