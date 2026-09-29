using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Text.Json;
using Echopad.App.Services;
using Echopad.App.Settings;
using Echopad.Core;

namespace Echopad.App;

public partial class MainWindow
{
    private bool _backgroundServicesStarted;
    private int _pendingCaptureCount;
    internal void StartBackgroundServices()
    {
        if (_backgroundServicesStarted) return;
        _backgroundServicesStarted = true;
        RefreshDropWatcher(); SetupMidiDevices(); SetupInputTaps(); SyncAllPadLeds();
    }
    internal void OpenGeneralSettings() => OpenSettingsWindow("General");
    private void PadGutter_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePadHostSquare();
    public bool HasMidiInput => _midiIn != null;
    public void CancelMidiLearn() => _pendingMidiLearn = null;
    internal async Task WaitForPendingCapturesAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (_pendingCaptureCount > 0)
        {
            if (DateTime.UtcNow >= deadline) throw new InvalidOperationException("A pad is still saving audio. Wait for it to finish and try again.");
            await Task.Delay(50, cancellationToken);
        }
    }
    public string ActiveProfileName => _profiles?.GetProfile(ActiveProfileIndex).Name ?? $"Profile {ActiveProfileIndex:00}";

    private void ShowProfileDropdown(FrameworkElement anchor)
    {
        if (IsProfileSwitchBlocked()) return;
        var menu = CreateProfileDropdown(anchor);
        menu.IsOpen = true;
    }

    private ContextMenu CreateProfileDropdown(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        menu.Style = (Style)FindResource("ProfileContextMenuStyle");
        menu.ItemContainerStyle = (Style)FindResource("ProfileMenuItemStyle");
        foreach (var profile in _profiles.LoadStore().Profiles.Where(p => p.ProfileIndex is >= 1 and <= 16))
        {
            int index = profile.ProfileIndex;
            var item = new MenuItem { Header = $"{index:00}   {profile.Name}", IsCheckable = true, IsChecked = index == ActiveProfileIndex };
            item.Click += (_, _) => SwitchToProfile(index); menu.Items.Add(item);
        }
        return menu;
    }
    private void ApplySettingsLiveCore()
    {
        var previous = GlobalSettings;
        var next = _settingsService.Load();
        next.EnsureCompatibility();
        bool inputsChanged = JsonSerializer.Serialize(new[] { previous.Input1, previous.Input2 }) != JsonSerializer.Serialize(new[] { next.Input1, next.Input2 });
        bool midiChanged = previous.MidiInDeviceId != next.MidiInDeviceId || previous.MidiOutDeviceId != next.MidiOutDeviceId;
        bool foldersChanged = previous.DropFolderEnabled != next.DropFolderEnabled || previous.DropWatchFolder != next.DropWatchFolder;
        bool linksChanged = previous.ProfileSwitch.PadsMidiSameAsProfile1 != next.ProfileSwitch.PadsMidiSameAsProfile1 ||
            previous.ProfileSwitch.PadsMidiAndHotkeysSameAsProfile1 != next.ProfileSwitch.PadsMidiAndHotkeysSameAsProfile1;
        if (linksChanged)
        {
            // Keep independent bindings in the profile store; overlays are runtime choices.
            var stored = _profiles.GetPadsForProfile(ActiveProfileIndex);
            foreach (var kv in next.Pads) if (stored.TryGetValue(kv.Key, out var original))
            {
                kv.Value.MidiTriggerDisplay = original.MidiTriggerDisplay; kv.Value.PadHotkey = original.PadHotkey;
            }
            if (ActiveProfileIndex != 1 && (next.ProfileSwitch.PadsMidiSameAsProfile1 || next.ProfileSwitch.PadsMidiAndHotkeysSameAsProfile1))
                _profiles.OverlayPadMapFromProfile1(next, next.ProfileSwitch.PadsMidiAndHotkeysSameAsProfile1);
            _settingsService.Save(next);
        }
        GlobalSettings = next;
        AppearanceTheme.Apply(next.Appearance);
        if (foldersChanged) RefreshDropWatcher();
        if (midiChanged) SetupMidiDevices();
        if (inputsChanged) SetupInputTaps();
        OnPropertyChanged(nameof(ActiveProfileName));
        SyncAllPadLeds();
    }

    private IEnumerable<(string? Binding, int Action)> TrimBindings(bool midi)
    {
        var s = GlobalSettings;
        yield return (midi ? s.MidiBindTrimSelectIn : s.HotkeyTrimSelectIn, 0);
        yield return (midi ? s.MidiBindTrimSelectOut : s.HotkeyTrimSelectOut, 1);
        yield return (midi ? s.MidiBindTrimNudgePlus : s.HotkeyTrimNudgePlus, 2);
        yield return (midi ? s.MidiBindTrimNudgeMinus : s.HotkeyTrimNudgeMinus, 3);
    }
    private void ApplyTrimShortcut(int action)
    {
        if (DataContext is not MainViewModel vm || !vm.IsEditMode || !IsPadsInputEnabled) return;
        if (action == 0) { _activeTrimTarget = ActiveTrimTarget.Start; return; }
        if (action == 1) { _activeTrimTarget = ActiveTrimTarget.End; return; }
        var pad = TryGetLastActivatedPad();
        if (pad == null || string.IsNullOrWhiteSpace(pad.ClipPath) || _activeTrimTarget == ActiveTrimTarget.None) return;
        int duration = (int)pad.ClipDuration.TotalMilliseconds;
        int end = pad.EndMs > 0 ? pad.EndMs : duration;
        int delta = action == 2 ? 10 : -10;
        if (_activeTrimTarget == ActiveTrimTarget.Start) pad.StartMs = Math.Clamp(pad.StartMs + delta, 0, Math.Max(0, end - 1));
        else pad.EndMs = Math.Clamp(end + delta, Math.Min(duration, pad.StartMs + 1), duration);
        PersistKeyboardTrim(pad);
    }
}
