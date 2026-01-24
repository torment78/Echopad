using System;
using System.IO;
using System.Text.Json;
using Echopad.Core;

namespace Echopad.App.Services
{
    public sealed class SettingsService
    {
        private readonly string _settingsPath;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public SettingsService()
        {
            // App-local file (same folder as exe)
            _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "echopad.settings.json");
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
            File.WriteAllText(_settingsPath, json);
        }
    }
}
