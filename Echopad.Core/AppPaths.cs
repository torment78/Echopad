// =====================================================
// FILE: Echopad.Core/AppPaths.cs   (NEW)
// =====================================================
using System;
using System.IO;

namespace Echopad.Core
{
    public static class AppPaths
    {
        // "Echopad" folder inside LocalAppData
        public static string RootDir
        {
            get
            {
                var root = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Echopad"
                );
                Directory.CreateDirectory(root);
                return root;
            }
        }

        // Settings files (centralized)
        public static string SettingsJsonPath => Path.Combine(RootDir, "settings.json");
        public static string ProfilesJsonPath => Path.Combine(RootDir, "profiles.json");

        // Optional: logs / captures / etc
        public static string LogsDir
        {
            get
            {
                var p = Path.Combine(RootDir, "Logs");
                Directory.CreateDirectory(p);
                return p;
            }
        }
    }
}