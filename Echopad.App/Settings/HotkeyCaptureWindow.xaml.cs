using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
namespace Echopad.App.Settings;

public partial class HotkeyCaptureWindow : Window, INotifyPropertyChanged
{
    private readonly bool _modifiersOnly;
    private string? _hotkeyText;
    private ModifierKeys _captureModifiers;
    public string? HotkeyText { get => _hotkeyText; set { _hotkeyText = value; OnPropertyChanged(); } }
    public HotkeyCaptureWindow(string? initialHotkey = null, bool modifiersOnly = false)
    {
        InitializeComponent(); DataContext = this; _modifiersOnly = modifiersOnly; HotkeyText = initialHotkey ?? "";
        Title = modifiersOnly ? "Learn profile modifier" : "Learn shortcut";
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { DialogResult = false; e.Handled = true; return; }
        if ((key is Key.Back or Key.Delete) && Keyboard.Modifiers == ModifierKeys.None) { HotkeyText = ""; e.Handled = true; return; }
        if (HotkeyTextBuilder.IsModifier(key)) { _captureModifiers |= Keyboard.Modifiers; HotkeyText = HotkeyTextBuilder.Modifiers(_captureModifiers); }
        else if (!_modifiersOnly) HotkeyText = HotkeyTextBuilder.Build(key, Keyboard.Modifiers);
        e.Handled = true;
    }
    private void Window_KeyUp(object sender, KeyEventArgs e) { if (Keyboard.Modifiers == ModifierKeys.None) _captureModifiers = ModifierKeys.None; e.Handled = true; }
    private void Clear_Click(object sender, RoutedEventArgs e) => HotkeyText = "";
    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public static class HotkeyTextBuilder
{
    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
    public static string Modifiers(ModifierKeys mods) => string.Join("+", new[] { mods.HasFlag(ModifierKeys.Control) ? "Ctrl" : null, mods.HasFlag(ModifierKeys.Shift) ? "Shift" : null, mods.HasFlag(ModifierKeys.Alt) ? "Alt" : null, mods.HasFlag(ModifierKeys.Windows) ? "Win" : null }.Where(x => x != null));
    public static string? Build(Key key, ModifierKeys mods) { if (IsModifier(key) || key == Key.None) return null; var prefix = Modifiers(mods); return prefix.Length == 0 ? key.ToString() : prefix + "+" + key; }
}
