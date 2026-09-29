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
        private readonly IStartupRegistration? _startup;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public string DataDirectory { get; }
        public string CapturesDirectory => Path.Combine(DataDirectory, "Captures");

        public SettingsService(string? dataDirectory = null, IStartupRegistration? startup = null)
        {
            var dir = dataDirectory ?? AppPaths.RootDir;

            Directory.CreateDirectory(dir);
            DataDirectory = dir;

            _settingsPath = Path.Combine(dir, "echopad.settings.json");
            _startup = startup ?? (dataDirectory == null
                ? new WindowsStartupRegistration(Path.Combine(AppContext.BaseDirectory, "Echopad.App.exe")) : null);

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

        public void ReconcileWindowsStartup() => _startup?.SetEnabled(Load().Desktop.StartWithWindows);

        public void Save(GlobalSettings settings)
        {
            if (settings == null) return;

            settings.Pads ??= new System.Collections.Generic.Dictionary<int, PadSettings>();
            settings.EnsureCompatibility();

            var json = JsonSerializer.Serialize(settings, JsonOpts);

            bool previousStartup = Load().Desktop.StartWithWindows;
            _startup?.SetEnabled(settings.Desktop.StartWithWindows);
            string temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json);
                File.Move(temporary, _settingsPath, overwrite: true);
            }
            catch
            {
                try { _startup?.SetEnabled(previousStartup); } catch { /* Preserve the save failure for the UI. */ }
                throw;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
