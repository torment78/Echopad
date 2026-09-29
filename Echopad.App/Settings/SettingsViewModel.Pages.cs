using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media;
using Echopad.App.Services;
using Echopad.Core;

namespace Echopad.App.Settings;

public sealed partial class SettingsViewModel : IDisposable
{
    public ObservableCollection<RouteSettingsViewModel> Routes { get; } = new();
    public ObservableCollection<ProfileSettingsRow> Profiles { get; } = new();
    public ObservableCollection<HueOption> AppearanceColors { get; } = new();
    public ICollectionView ProfileView { get; private set; } = null!;
    public event Action? Saved;
    public string SaveStatus { get; private set; } = "Changes are saved automatically.";
    private string _profileSearch = "";
    public string ProfileSearch { get => _profileSearch; set { _profileSearch = value; ProfileView.Refresh(); OnPropertyChanged(); } }
    public string? ProfileModifier { get => Settings.ProfileSwitch.HotkeyModifier; set { Settings.ProfileSwitch.HotkeyModifier = value; Changed(); } }
    public string? ProfileMidiModifier { get => Settings.ProfileSwitch.MidiModifierBind; set { Settings.ProfileSwitch.MidiModifierBind = value; Changed(); } }
    public int ProfileLinkMode
    {
        get => Settings.ProfileSwitch.PadsMidiAndHotkeysSameAsProfile1 ? 2 : Settings.ProfileSwitch.PadsMidiSameAsProfile1 ? 1 : 0;
        set
        {
            Settings.ProfileSwitch.PadsMidiSameAsProfile1 = value == 1;
            Settings.ProfileSwitch.PadsMidiAndHotkeysSameAsProfile1 = value == 2;
            Settings.ProfileSwitch.MidiLinkMode = (ProfileMidiLinkMode)value;
            Changed();
        }
    }
    public string? UiArmedInput1Hex { get => Settings.UiArmedInput1Hex; set { Settings.UiArmedInput1Hex = value; Changed(); } }
    public string? UiArmedInput2Hex { get => Settings.UiArmedInput2Hex; set { Settings.UiArmedInput2Hex = value; Changed(); } }
    public double LoadedFillIntensity { get => Settings.Appearance.LoadedFillIntensity; set { Settings.Appearance.LoadedFillIntensity = Math.Clamp(value, 0, 100); Changed(); } }
    public double PlayingFillIntensity { get => Settings.Appearance.PlayingFillIntensity; set { Settings.Appearance.PlayingFillIntensity = Math.Clamp(value, 0, 100); Changed(); } }
    public double OutlineIntensity { get => Settings.Appearance.OutlineIntensity; set { Settings.Appearance.OutlineIntensity = Math.Clamp(value, 0, 100); AppearanceTheme.Apply(Settings.Appearance); Changed(); } }
    private void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null) { OnPropertyChanged(name); RequestAutoSave(); }
    private void InitializePages()
    {
        Routes.Add(new("Input 1", "15-second rolling capture", Settings.Input1, null, AudioInputs, RequestAutoSave, 1));
        Routes.Add(new("Input 2", "15-second rolling capture", Settings.Input2, null, AudioInputs, RequestAutoSave, 2));
        Routes.Add(new("Main output", "Live pad playback", null, Settings.Out1, AudioOutputs, RequestAutoSave, 0));
        Routes.Add(new("Monitor output", "Private preview in Edit and pad setup", null, Settings.Out2, AudioOutputs, RequestAutoSave, 0));
        var store = new ProfileService(_settingsService).LoadStore();
        foreach (var profile in store.Profiles.Where(p => p.ProfileIndex is >= 1 and <= 16))
        {
            var slot = Settings.ProfileSwitch.Slots[profile.ProfileIndex - 1];
            if (string.IsNullOrWhiteSpace(slot.Name)) slot.Name = profile.Name;
            Profiles.Add(new(profile.ProfileIndex, slot, RequestAutoSave));
        }
        ProfileView = CollectionViewSource.GetDefaultView(Profiles);
        ProfileView.Filter = item => item is ProfileSettingsRow p && (string.IsNullOrWhiteSpace(ProfileSearch) ||
            p.Name.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase) || p.Number.Contains(ProfileSearch, StringComparison.OrdinalIgnoreCase));
        BuildAppearanceOptions();
    }
    private void BuildAppearanceOptions()
    {
        AppearanceColors.Clear();
        var a = Settings.Appearance;
        void Add(string label, Func<double> get, Action<double> set, double s, double v) =>
            AppearanceColors.Add(new(label, get, set, s, v, () => { AppearanceTheme.Apply(a); RequestAutoSave(); }));
        Add("Main background", () => a.BackgroundHue, v => a.BackgroundHue = v, .24, .085);
        Add("Pad card", () => a.CardHue, v => a.CardHue = v, .20, .135);
        Add("Card outline", () => a.OutlineHue, v => a.OutlineHue = v, .23, .26);
        Add("Settings / Edit", () => a.ButtonHue, v => a.ButtonHue = v, .25, .19);
        Add("Button text", () => a.ButtonTextHue, v => a.ButtonTextHue = v, .18, .94);
        Add("EchoPad title", () => a.TitleHue, v => a.TitleHue = v, .36, .95);
        Add("Pad surface", () => a.PadHue, v => a.PadHue = v, .25, .14);
        Add("Pad numbers / names", () => a.PadTextHue, v => a.PadTextHue = v, .18, .94);
    }
    public void ResetAppearance() {
        Settings.Appearance = new(); BuildAppearanceOptions(); AppearanceTheme.Apply(Settings.Appearance);
        OnPropertyChanged(nameof(LoadedFillIntensity)); OnPropertyChanged(nameof(PlayingFillIntensity)); OnPropertyChanged(nameof(OutlineIntensity));
        RequestAutoSave();
    }
    public void Dispose() { _autoSaveTimer.Stop(); }
    public bool ValidateSettings(out string error)
    {
        foreach (var r in Routes) if (r.VbanEnabled)
        {
            if (!System.Net.IPAddress.TryParse(r.RemoteIp, out _)) { error = r.Title + ": enter a valid VBAN IP address."; return false; }
            if (r.Port is < 1 or > 65535) { error = r.Title + ": port must be 1–65535."; return false; }
            if (string.IsNullOrWhiteSpace(r.StreamName) || r.StreamName.Length > 16 || r.StreamName.Any(c => c > 127))
            {
                error = r.Title + ": stream name must contain 1–16 ASCII characters."; return false;
            }
        }
        foreach (var color in new[] { UiArmedInput1Hex, UiArmedInput2Hex })
        {
            try { _ = ColorConverter.ConvertFromString(color ?? ""); }
            catch { error = "MIDI & colors: enter valid input colors, for example #FF4DB8."; return false; }
        }
        error = ""; return true;
    }
    private void SavePages()
    {
        var profiles = new ProfileService(_settingsService);
        var store = profiles.LoadStore();
        foreach (var row in Profiles)
        {
            var profile = store.Profiles.First(p => p.ProfileIndex == row.Index);
            profile.Name = string.IsNullOrWhiteSpace(row.Name) ? $"Profile {row.Index:00}" : row.Name.Trim();
        }
        profiles.SaveStore(store);
    }
}

public sealed class ProfileSettingsRow : INotifyPropertyChanged
{
    private readonly ProfileSlotBind _slot; private readonly Action _changed;
    public int Index { get; }
    public string Number => $"{Index:00}";
    public ProfileSettingsRow(int index, ProfileSlotBind slot, Action changed) { Index = index; _slot = slot; _changed = changed; }
    public string Name { get => _slot.Name ?? ""; set { _slot.Name = value; Change(nameof(Name)); } }
    public string? HotkeyBind { get => _slot.HotkeyBind; set { _slot.HotkeyBind = value; Change(nameof(HotkeyBind)); } }
    public string? MidiBind { get => _slot.MidiBind; set { _slot.MidiBind = value; Change(nameof(MidiBind)); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Change(string name) { PropertyChanged?.Invoke(this, new(name)); _changed(); }
}

public sealed class RouteSettingsViewModel : INotifyPropertyChanged
{
    private readonly InputEndpointSettings? _input;
    private readonly OutputEndpointSettings? _output;
    private readonly Action _changed;
    public string Title { get; }
    public string Subtitle { get; }
    public int InputNumber { get; }
    public bool IsInput => _input != null;
    public ObservableCollection<DeviceOption> Devices { get; }
    public RouteSettingsViewModel(string title, string subtitle, InputEndpointSettings? input, OutputEndpointSettings? output, ObservableCollection<DeviceOption> devices, Action changed, int inputNumber)
    {
        Title = title; Subtitle = subtitle; _input = input; _output = output; Devices = devices; _changed = changed; InputNumber = inputNumber;
    }
    private bool Enabled { get => _input?.Enabled ?? _output!.Enabled; set { if (_input != null) _input.Enabled = value; else _output!.Enabled = value; } }
    public AudioEndpointMode Mode { get => _input?.Mode ?? _output!.Mode; private set { if (_input != null) _input.Mode = value; else _output!.Mode = value; } }
    public bool LocalEnabled { get => Enabled && Mode == AudioEndpointMode.Local; set { SetMode(AudioEndpointMode.Local, value); } }
    public bool VbanEnabled { get => Enabled && Mode == AudioEndpointMode.Vban; set { SetMode(AudioEndpointMode.Vban, value); } }
    private void SetMode(AudioEndpointMode mode, bool on)
    {
        if (on) { Mode = mode; Enabled = true; } else if (Mode == mode) Enabled = false;
        Changed(nameof(LocalEnabled)); Changed(nameof(VbanEnabled));
    }
    public string? DeviceId { get => (_input?.LocalDeviceId ?? _output?.LocalDeviceId) ?? ""; set { if (_input != null) _input.LocalDeviceId = value; else _output!.LocalDeviceId = value; Changed(); } }
    public string RemoteIp { get => _input?.Vban.RemoteIp ?? _output!.Vban.RemoteIp; set { if (_input != null) _input.Vban.RemoteIp = value; else _output!.Vban.RemoteIp = value; Changed(); } }
    public int Port { get => _input?.Vban.Port ?? _output!.Vban.Port; set { if (_input != null) _input.Vban.Port = value; else _output!.Vban.Port = value; Changed(); } }
    public string StreamName { get => _input?.Vban.StreamName ?? _output!.Vban.StreamName; set { if (_input != null) _input.Vban.StreamName = value; else _output!.Vban.StreamName = value; Changed(); } }
    private double _localDb = -60, _vbanDb = -60, _localPeak = -60, _vbanPeak = -60;
    private DateTime _localHold, _vbanHold;
    public double LocalDb => _localDb; public double VbanDb => _vbanDb; public double LocalPeak => _localPeak; public double VbanPeak => _vbanPeak;
    public void UpdateMeter(double db)
    {
        db = double.IsFinite(db) ? Math.Clamp(db, -60, 0) : -60;
        _localDb = LocalEnabled ? Math.Max(db, _localDb - 3) : -60; _vbanDb = VbanEnabled ? Math.Max(db, _vbanDb - 3) : -60;
        var now = DateTime.UtcNow;
        if (_localDb >= _localPeak) { _localPeak = _localDb; _localHold = now; } else if ((now - _localHold).TotalSeconds > 1) _localPeak = Math.Max(-60, _localPeak - 1.2);
        if (_vbanDb >= _vbanPeak) { _vbanPeak = _vbanDb; _vbanHold = now; } else if ((now - _vbanHold).TotalSeconds > 1) _vbanPeak = Math.Max(-60, _vbanPeak - 1.2);
        if (!LocalEnabled) _localPeak = -60; if (!VbanEnabled) _vbanPeak = -60;
        foreach (var name in new[] { nameof(LocalDb), nameof(VbanDb), nameof(LocalPeak), nameof(VbanPeak) }) PropertyChanged?.Invoke(this, new(name));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([System.Runtime.CompilerServices.CallerMemberName] string? name = null) { PropertyChanged?.Invoke(this, new(name)); _changed(); }
}

public sealed class HueOption : INotifyPropertyChanged
{
    private readonly Func<double> _get; private readonly Action<double> _set; private readonly Action _changed; private readonly double _s, _v;
    public string Label { get; }
    public HueOption(string label, Func<double> get, Action<double> set, double saturation, double value, Action changed) { Label = label; _get = get; _set = set; _s = saturation; _v = value; _changed = changed; }
    public double Hue { get => _get(); set { _set(AppearanceTheme.Normalize(value)); foreach (var n in new[] { nameof(Hue), nameof(Swatch), nameof(Hex) }) PropertyChanged?.Invoke(this, new(n)); _changed(); } }
    public Brush Swatch => new SolidColorBrush(AppearanceTheme.ColorAt(Hue, _s, _v));
    public string Hex => AppearanceTheme.ColorAt(Hue, _s, _v).ToString();
    public event PropertyChangedEventHandler? PropertyChanged;
}
