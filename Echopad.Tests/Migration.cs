using System.IO;
using System.Text.Json.Nodes;
using Echopad.App.Services;
using Echopad.Core;

static partial class Program
{
    static void MigrationChecks(string root)
    {
        string legacy = Path.Combine(root, "old-user-data");
        string oldInstall = Path.Combine(root, "custom-old-install");
        string captures = Path.Combine(root, "Documents", "Echopad", "Captures");
        string target = Path.Combine(root, "ElkaSoft", "EchoPad");
        string clip = FixtureFile(captures, "voice.wav", "original capture bytes");
        string image = FixtureFile(Path.Combine(legacy, "PadImages"), "stopped.png", "original PNG bytes");
        string playing = FixtureFile(Path.Combine(legacy, "PadImages", "nested"), "playing.png", "playing PNG bytes");
        string external = FixtureFile(legacy + "-external", "music.wav", "external audio");
        var oldService = new SettingsService(legacy);
        var settings = new GlobalSettings { DropWatchFolder = Path.Combine(root, "external-drop"), HotkeyToggleEdit = "Ctrl+Shift+F9" };
        settings.AudioFolders.Add(Path.GetDirectoryName(external)!);
        var pad = settings.GetOrCreatePad(1);
        pad.PadName = "Íslenska voice"; pad.ClipPath = clip; pad.StartMs = 125; pad.EndMs = 850;
        pad.InputSource = 2; pad.PadHotkey = "Ctrl+Alt+F1"; pad.MidiTriggerDisplay = "CC:2:17:80";
        pad.Graphics.StoppedImage = image; pad.Graphics.PlayingImage = playing; pad.Graphics.StoppedOpacity = 43;
        settings.GetOrCreatePad(2).ClipPath = external;
        oldService.Save(settings);
        var profiles = new ProfileService(oldService);
        profiles.SavePadsToProfile(settings, 7); profiles.SetActiveProfileIndex(7);
        var named = profiles.GetProfile(7); named.Name = "Original profile"; profiles.UpdateProfile(named);
        string sourceJson = Path.Combine(legacy, "echopad.settings.json");
        var json = JsonNode.Parse(File.ReadAllText(sourceJson))!;
        json["FutureSetting"] = "keep unknown properties";
        File.WriteAllText(sourceJson, json.ToJsonString());
        byte[] oldSettingsBytes = File.ReadAllBytes(sourceJson);
        byte[] oldProfileBytes = File.ReadAllBytes(Path.Combine(legacy, "profiles.json"));
        File.SetAttributes(sourceJson, FileAttributes.ReadOnly);
        var older = new SettingsService(oldInstall);
        older.Save(new GlobalSettings { HotkeyToggleEdit = "F1" });
        FixtureFile(oldInstall, "Echopad.App.exe", "must never be copied into data");

        // Collision with an already present newer capture must preserve both files.
        string newerCapture = FixtureFile(Path.Combine(target, "Captures"), "voice.wav", "newer capture bytes");
        LegacyDataMigration.Migrate(target, new[] { legacy, oldInstall }, captures);
        var migratedService = new SettingsService(target);
        var migrated = migratedService.Load();
        var migratedPad = migrated.GetOrCreatePad(1);
        Check(migrated.HotkeyToggleEdit == "Ctrl+Shift+F9" && migratedPad.PadName == "Íslenska voice", "migration prioritizes user data and preserves Unicode names and hotkeys");
        Check(migratedPad.ClipPath != newerCapture && migratedPad.ClipPath!.StartsWith(Path.Combine(target, "Captures")) &&
            File.ReadAllText(migratedPad.ClipPath) == "original capture bytes" && File.ReadAllText(newerCapture) == "newer capture bytes", "migration preserves conflicting recordings and rewrites the imported clip path");
        Check(migratedPad.Graphics.StoppedImage == Path.Combine(target, "PadImages", "stopped.png") &&
            File.ReadAllText(migratedPad.Graphics.StoppedImage) == "original PNG bytes" &&
            migratedPad.Graphics.PlayingImage == Path.Combine(target, "PadImages", "nested", "playing.png") &&
            migratedPad.Graphics.StoppedOpacity == 43, "migration copies both PNG states recursively and keeps opacity");
        Check(migrated.GetOrCreatePad(2).ClipPath == external && migrated.DropWatchFolder == settings.DropWatchFolder &&
            migrated.AudioFolders.SequenceEqual(settings.AudioFolders), "migration leaves external media, libraries and watched folders in place");
        var migratedProfiles = new ProfileService(migratedService);
        var saved = migratedProfiles.GetProfile(7);
        Check(migratedProfiles.GetActiveProfileIndex() == 7 && saved.Name == "Original profile" &&
            saved.Pads[1].ClipPath == migratedPad.ClipPath && saved.Pads[1].Graphics.PlayingImage == migratedPad.Graphics.PlayingImage,
            "migration retains active/named profiles and updates their media references");
        Check(saved.Pads[1].StartMs == 125 && saved.Pads[1].EndMs == 850 && saved.Pads[1].InputSource == 2 &&
            saved.Pads[1].MidiTriggerDisplay == "CC:2:17:80" && saved.Pads[1].PadHotkey == "Ctrl+Alt+F1", "migration preserves trim, routing and pad bindings");
        Check(JsonNode.Parse(File.ReadAllText(Path.Combine(target, "echopad.settings.json")))!["FutureSetting"]!.GetValue<string>() == "keep unknown properties",
            "migration retains unrecognized JSON properties");
        Check(File.ReadAllBytes(sourceJson).SequenceEqual(oldSettingsBytes) &&
            File.ReadAllBytes(Path.Combine(legacy, "profiles.json")).SequenceEqual(oldProfileBytes) &&
            File.ReadAllText(clip) == "original capture bytes" && File.ReadAllText(image) == "original PNG bytes", "migration leaves all original saves and media intact");
        Check(!File.Exists(Path.Combine(target, "Echopad.App.exe")) && File.Exists(Path.Combine(target, LegacyDataMigration.MarkerName)),
            "migration copies data only and marks successful completion");
        Check(migratedService.CapturesDirectory == Path.Combine(target, "Captures") && !File.Exists(Path.Combine(legacy, LegacyDataMigration.MarkerName)),
            "capture output uses the injected data root without triggering real-user migration");
        File.Delete(migratedPad.Graphics.StoppedImage!);
        byte[] destinationSettings = File.ReadAllBytes(Path.Combine(target, "echopad.settings.json"));
        LegacyDataMigration.Migrate(target, new[] { legacy, oldInstall }, captures);
        Check(!File.Exists(migratedPad.Graphics.StoppedImage) &&
            File.ReadAllBytes(Path.Combine(target, "echopad.settings.json")).SequenceEqual(destinationSettings), "completed migration never reimports removed media or old settings");

        string existingTarget = Path.Combine(root, "existing-destination");
        var existingService = new SettingsService(existingTarget);
        existingService.Save(new GlobalSettings { HotkeyOpenSettings = "F12" });
        new ProfileService(existingService).SetActiveProfileIndex(12);
        byte[] existingSettings = File.ReadAllBytes(Path.Combine(existingTarget, "echopad.settings.json"));
        byte[] existingProfiles = File.ReadAllBytes(Path.Combine(existingTarget, "profiles.json"));
        LegacyDataMigration.Migrate(existingTarget, new[] { legacy }, captures);
        Check(File.ReadAllBytes(Path.Combine(existingTarget, "echopad.settings.json")).SequenceEqual(existingSettings) &&
            File.ReadAllBytes(Path.Combine(existingTarget, "profiles.json")).SequenceEqual(existingProfiles), "existing destination settings and profiles are never overwritten");

        string ancient = Path.Combine(root, "ancient-install");
        FixtureFile(ancient, "relative.wav", "relative audio");
        FixtureFile(ancient, "settings.json", "{ // old filename and permissive JSON\n\"Pads\":{\"1\":{\"Index\":1,\"ClipPath\":\"relative.wav\"}},}");
        string ancientTarget = Path.Combine(root, "ancient-target");
        LegacyDataMigration.Migrate(ancientTarget, new[] { ancient }, Path.Combine(root, "missing-captures"));
        string? relativeClip = new SettingsService(ancientTarget).Load().GetOrCreatePad(1).ClipPath;
        Check(relativeClip == Path.Combine(ancientTarget, "LegacyMedia", "relative.wav") && File.ReadAllText(relativeClip) == "relative audio",
            "executable-folder migration supports legacy filename, comments, trailing commas and relative media");

        string damaged = Path.Combine(root, "damaged");
        var damagedService = new SettingsService(damaged); damagedService.Save(new GlobalSettings());
        string damagedProfiles = FixtureFile(damaged, "profiles.json", "{invalid-json}");
        string retryTarget = Path.Combine(root, "retry-target");
        bool rejected = false;
        try { LegacyDataMigration.Migrate(retryTarget, new[] { damaged }, captures); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected && !File.Exists(Path.Combine(retryTarget, "echopad.settings.json")) &&
            !File.Exists(Path.Combine(retryTarget, LegacyDataMigration.MarkerName)), "damaged legacy JSON stops migration before defaults can replace it");
        File.WriteAllText(damagedProfiles, "{}");
        // Force a copy failure without changing filesystem permissions or using real user folders.
        string blocker = FixtureFile(retryTarget, "Captures", "a file blocking the directory");
        rejected = false;
        try { LegacyDataMigration.Migrate(retryTarget, new[] { damaged }, captures); }
        catch (IOException) { rejected = true; }
        Check(rejected && !File.Exists(Path.Combine(retryTarget, "echopad.settings.json")) &&
            !File.Exists(Path.Combine(retryTarget, LegacyDataMigration.MarkerName)), "failed media copy does not commit settings or a completion marker");
        File.Delete(blocker);
        LegacyDataMigration.Migrate(retryTarget, new[] { damaged }, captures);
        Check(File.Exists(Path.Combine(retryTarget, LegacyDataMigration.MarkerName)) &&
            File.ReadAllText(Path.Combine(retryTarget, "Captures", "voice.wav")) == "original capture bytes" &&
            !Directory.EnumerateFiles(retryTarget, "*.tmp", SearchOption.AllDirectories).Any(), "migration retries successfully after repair without temporary-file leftovers");

        string partialTarget = Path.Combine(root, "partial-target");
        new SettingsService(partialTarget).Save(new GlobalSettings { HotkeyOpenSettings = "F10" });
        LegacyDataMigration.Migrate(partialTarget, new[] { oldInstall, legacy }, captures);
        Check(new SettingsService(partialTarget).Load().HotkeyOpenSettings == "F10" &&
            new ProfileService(new SettingsService(partialTarget)).GetProfile(7).Name == "Original profile", "missing profiles migrate independently when destination settings already exist");
    }

    static string FixtureFile(string directory, string name, string contents)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, contents);
        return path;
    }
}
