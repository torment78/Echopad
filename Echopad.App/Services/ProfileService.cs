using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Echopad.Core;

namespace Echopad.App.Services
{
    public sealed class ProfileService
    {
        private readonly SettingsService _settingsService;

        private const int ProfileCountConst = 16;

        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented = true
        };

        public ProfileService(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        // -------------------------------------------------
        // One-file storage: profiles.json
        // -------------------------------------------------
        private static string BaseDir
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Echopad");

        private static string ProfilesPath
            => Path.Combine(BaseDir, "profiles.json");

        public ProfileStore LoadStore()
        {
            Directory.CreateDirectory(BaseDir);

            if (!File.Exists(ProfilesPath))
            {
                var fresh = CreateFreshStore();
                SaveStore(fresh);
                return fresh;
            }

            try
            {
                var json = File.ReadAllText(ProfilesPath);
                var store = JsonSerializer.Deserialize<ProfileStore>(json, _json) ?? CreateFreshStore();
                NormalizeStore(store);
                return store;
            }
            catch
            {
                var fresh = CreateFreshStore();
                SaveStore(fresh);
                return fresh;
            }
        }

        public void SaveStore(ProfileStore store)
        {
            Directory.CreateDirectory(BaseDir);
            NormalizeStore(store);
            File.WriteAllText(ProfilesPath, JsonSerializer.Serialize(store, _json));
        }

        public int GetActiveProfileIndex()
        {
            var s = LoadStore();
            return s.ActiveProfileIndex;
        }

        public void SetActiveProfileIndex(int index)
        {
            var s = LoadStore();
            s.ActiveProfileIndex = ClampProfile(index);
            SaveStore(s);
        }

        public ProfileData GetProfile(int index)
        {
            var s = LoadStore();
            index = ClampProfile(index);

            var p = s.Profiles.FirstOrDefault(x => x.ProfileIndex == index);
            if (p == null)
            {
                p = new ProfileData { ProfileIndex = index, Name = $"Profile {index:00}" };
                s.Profiles.Add(p);
                NormalizeStore(s);
                SaveStore(s);
            }

            p.Pads ??= new System.Collections.Generic.Dictionary<int, PadSettings>();
            return p;
        }

        public void UpdateProfile(ProfileData profile)
        {
            var s = LoadStore();
            profile.ProfileIndex = ClampProfile(profile.ProfileIndex);
            profile.Pads ??= new System.Collections.Generic.Dictionary<int, PadSettings>();

            var existing = s.Profiles.FirstOrDefault(x => x.ProfileIndex == profile.ProfileIndex);
            if (existing == null)
                s.Profiles.Add(profile);
            else
            {
                existing.Name = profile.Name;
                existing.Pads = profile.Pads;
            }

            SaveStore(s);
        }

        // -------------------------------------------------
        // NEW: Expose a COPY of pads for a given profile
        // (safe: never returns internal references)
        // -------------------------------------------------
        public System.Collections.Generic.Dictionary<int, PadSettings> GetPadsForProfile(int profileIndex)
        {
            var p = GetProfile(profileIndex);

            if (p.Pads == null || p.Pads.Count == 0)
                return new System.Collections.Generic.Dictionary<int, PadSettings>();

            return ClonePadDictionary(p.Pads);
        }

        // -------------------------------------------------
        // NEW: Overlay Profile 1's pad MIDI (and optionally hotkeys)
        // onto a target pad set (typically the profile being switched to)
        // -------------------------------------------------
        public void OverlayPadMapFromProfile1(GlobalSettings targetSettings, bool includeHotkeys)
        {
            if (targetSettings == null)
                return;

            // Ensure Profile 1 exists and is seeded (safety)
            EnsureSeedFromCurrentSettings(targetSettings);

            var srcPads = GetPadsForProfile(1);
            if (srcPads.Count == 0)
                return;

            targetSettings.Pads ??= new System.Collections.Generic.Dictionary<int, PadSettings>();

            foreach (var kv in srcPads)
            {
                int padIndex = kv.Key;
                var src = kv.Value;
                if (src == null) continue;

                var dst = targetSettings.GetOrCreatePad(padIndex);

                // Always lock MIDI trigger display to Profile 1
                dst.MidiTriggerDisplay = src.MidiTriggerDisplay;

                // Optionally lock pad hotkeys too
                if (includeHotkeys)
                    dst.PadHotkey = src.PadHotkey;
            }
        }
        public void SyncProfileSwitchSlotsFromProfile1Pads(GlobalSettings gs, bool includeHotkeys)
        {
            if (gs == null)
                return;

            gs.ProfileSwitch ??= new ProfileSwitchSettings();
            gs.ProfileSwitch.EnsureSlots();

            // Make sure Profile 1 exists / seeded
            EnsureSeedFromCurrentSettings(gs);

            var p1Pads = GetPadsForProfile(1);
            if (p1Pads == null || p1Pads.Count == 0)
                return;

            // Slot 1..16 <-> Pad 1..16
            for (int i = 1; i <= 16 && i <= gs.ProfileSwitch.Slots.Count; i++)
            {
                if (!p1Pads.TryGetValue(i, out var ps) || ps == null)
                    continue;

                var slot = gs.ProfileSwitch.Slots[i - 1];
                if (slot == null)
                    continue;

                // Fill MIDI bind if empty
                if (string.IsNullOrWhiteSpace(slot.MidiBind) &&
                    !string.IsNullOrWhiteSpace(ps.MidiTriggerDisplay))
                {
                    slot.MidiBind = ps.MidiTriggerDisplay;
                }

                // Fill hotkey bind if enabled + empty
                if (includeHotkeys &&
                    string.IsNullOrWhiteSpace(slot.HotkeyBind) &&
                    !string.IsNullOrWhiteSpace(ps.PadHotkey))
                {
                    slot.HotkeyBind = ps.PadHotkey;
                }

                // Optional: keep names aligned if empty
                if (string.IsNullOrWhiteSpace(slot.Name) && !string.IsNullOrWhiteSpace(ps.PadName))
                {
                    slot.Name = ps.PadName;
                }
            }
        }
        // -------------------------------------------------
        // Seeding (Profile 1 from current pads)
        // -------------------------------------------------
        public void EnsureSeedFromCurrentSettings(GlobalSettings gs)
        {
            var s = LoadStore();

            // If profiles are empty or profile 1 has no pads, seed it
            var p1 = s.Profiles.FirstOrDefault(x => x.ProfileIndex == 1);
            if (p1 == null)
            {
                p1 = new ProfileData { ProfileIndex = 1, Name = "Profile 01" };
                s.Profiles.Add(p1);
            }

            p1.Pads ??= new System.Collections.Generic.Dictionary<int, PadSettings>();

            if (p1.Pads.Count == 0 && gs?.Pads != null && gs.Pads.Count > 0)
            {
                // IMPORTANT: clone, do not share references
                p1.Pads = ClonePadDictionary(gs.Pads);
                SaveStore(s);
            }
        }

        

        // NEW: optional preserve of existing MIDI/hotkeys (fixes "random" save wipe)
        public void SavePadsToProfile(GlobalSettings gs, int profileIndex, bool preserveExistingMidiAndHotkeys)
        {
            var p = GetProfile(profileIndex);

            // clone current runtime pad map
            var cloned = gs?.Pads != null
                ? ClonePadDictionary(gs.Pads)
                : new System.Collections.Generic.Dictionary<int, PadSettings>();

            // If lock mode is enabled for non-profile1, we must NOT persist overlayed MIDI/hotkeys.
            // We preserve what the profile already stored for those fields.
            if (preserveExistingMidiAndHotkeys && p.Pads != null && p.Pads.Count > 0)
            {
                foreach (var kv in p.Pads)
                {
                    int padIndex = kv.Key;
                    var existing = kv.Value;
                    if (existing == null) continue;

                    if (!cloned.TryGetValue(padIndex, out var dst) || dst == null)
                        continue;

                    dst.MidiTriggerDisplay = existing.MidiTriggerDisplay;
                    dst.PadHotkey = existing.PadHotkey;
                }
            }

            p.Pads = cloned;
            UpdateProfile(p);
        }

        // Convenience overload (keeps your old call sites working)
        public void SavePadsToProfile(GlobalSettings gs, int profileIndex)
            => SavePadsToProfile(gs, profileIndex, preserveExistingMidiAndHotkeys: false);

        // -------------------------------------------------
        // Apply profile pads into GlobalSettings + persist
        // (keeps your app stable since it already reads Pads from settings.json)
        // -------------------------------------------------
        public void ApplyProfileToSettings(GlobalSettings gs, int profileIndex)
        {
            var p = GetProfile(profileIndex);

            gs.Pads = p.Pads != null
                ? ClonePadDictionary(p.Pads)
                : new System.Collections.Generic.Dictionary<int, PadSettings>();

            // Keep legacy/new endpoint sync sane
            gs.EnsureCompatibility();

            _settingsService.Save(gs);
        }

        // -------------------------------------------------
        // Helpers
        // -------------------------------------------------
        private static ProfileStore CreateFreshStore()
        {
            var s = new ProfileStore { ActiveProfileIndex = 1 };
            for (int i = 1; i <= ProfileCountConst; i++)
                s.Profiles.Add(new ProfileData { ProfileIndex = i, Name = $"Profile {i:00}" });
            return s;
        }

        private static void NormalizeStore(ProfileStore s)
        {
            if (s.Profiles == null) s.Profiles = new();

            // Ensure 1..16 exist
            for (int i = 1; i <= ProfileCountConst; i++)
            {
                var p = s.Profiles.FirstOrDefault(x => x.ProfileIndex == i);
                if (p == null)
                    s.Profiles.Add(new ProfileData { ProfileIndex = i, Name = $"Profile {i:00}" });
            }

            // Remove duplicates by ProfileIndex (keep first)
            s.Profiles = s.Profiles
                .GroupBy(p => p.ProfileIndex)
                .Select(g => g.First())
                .OrderBy(p => p.ProfileIndex)
                .ToList();

            foreach (var p in s.Profiles)
                p.Pads ??= new System.Collections.Generic.Dictionary<int, PadSettings>();

            s.ActiveProfileIndex = ClampProfile(s.ActiveProfileIndex);
        }

        private static int ClampProfile(int v)
        {
            if (v < 1) return 1;
            if (v > ProfileCountConst) return ProfileCountConst;
            return v;
        }

        // deep-ish clone so profiles don't share references
        private static System.Collections.Generic.Dictionary<int, PadSettings> ClonePadDictionary(
            System.Collections.Generic.Dictionary<int, PadSettings> src)
        {
            var dst = new System.Collections.Generic.Dictionary<int, PadSettings>();

            foreach (var kv in src)
            {
                if (kv.Value == null)
                    continue;

                // Json clone is safe + fast enough for 16 pads
                var json = JsonSerializer.Serialize(kv.Value, _json);
                var copy = JsonSerializer.Deserialize<PadSettings>(json, _json);

                if (copy != null)
                    dst[kv.Key] = copy;
            }

            return dst;
        }
    }
}
