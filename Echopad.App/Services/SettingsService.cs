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

        public SettingsService(string? dataDirectory = null)
        {
            // =====================================================
            // OLD (breaks under Program Files)
            // _settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "echopad.settings.json");
            // =====================================================

            // =====================================================
            // NEW: LocalAppData (writeable)
            // %LOCALAPPDATA%\Echopad\echopad.settings.json
            // =====================================================
            var dir = dataDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Echopad"
            );

            Directory.CreateDirectory(dir);
            DataDirectory = dir;

            _settingsPath = Path.Combine(dir, "echopad.settings.json");

            // OPTIONAL: one-time migration from old exe-folder settings (keeps old users)
            if (dataDirectory == null) TryMigrateFromExeFolder(dir);
        }

        // =====================================================
        // NEW: settings migration helper
        // =====================================================
        private void TryMigrateFromExeFolder(string newDir)
        {
            try
            {
                // If new settings already exists, do nothing
                if (File.Exists(_settingsPath))
                    return;

                var oldPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "echopad.settings.json");
                if (!File.Exists(oldPath))
                    return;

                // Copy old -> new
                File.Copy(oldPath, _settingsPath, overwrite: false);
            }
            catch
            {
                // ignore migration failure (never block startup)
            }
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
