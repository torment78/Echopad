using System;
using System.IO;
using System.Text.Json;
using Echopad.Core;

namespace Echopad.App.Services
{
    public sealed class SettingsService
    {
        // =====================================================
        // SEARCH ANCHOR: _settingsPath
        // =====================================================
        private readonly string _settingsPath;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public string DataDirectory { get; }
        public string CapturesDirectory => Path.Combine(DataDirectory, "Captures");

        public SettingsService(string? dataDirectory = null)
        {
            var dir = dataDirectory ?? AppPaths.RootDir;

            Directory.CreateDirectory(dir);
            DataDirectory = dir;

            _settingsPath = Path.Combine(dir, "echopad.settings.json");

            // Injected test directories never inspect or migrate the real user's data.
            if (dataDirectory == null) LegacyDataMigration.MigrateDefault(dir);
        }

        public GlobalSettings Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                    return new GlobalSettings();

                var json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<GlobalSettings>(json, JsonOpts) ?? new GlobalSettings();

                // Backwards-compat: older JSON won't have Pads
                loaded.Pads ??= new System.Collections.Generic.Dictionary<int, PadSettings>();
                loaded.EnsureCompatibility();

                return loaded;
            }
            catch
            {
                // If file is corrupted, fall back safely
                return new GlobalSettings();
            }
        }

        public void Save(GlobalSettings settings)
        {
            if (settings == null) return;

            settings.Pads ??= new System.Collections.Generic.Dictionary<int, PadSettings>();

            var json = JsonSerializer.Serialize(settings, JsonOpts);

            // NOTE: still simple write; you can make this atomic later if you want
            File.WriteAllText(_settingsPath, json);
        }
    }
}
