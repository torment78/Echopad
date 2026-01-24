using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace Echopad.App.Settings
{
    public partial class HotkeyCaptureWindow : Window, INotifyPropertyChanged
    {
        private string? _hotkeyText;
        public string? HotkeyText
        {
            get => _hotkeyText;
            set { _hotkeyText = value; OnPropertyChanged(); }
        }

        public HotkeyCaptureWindow(string? initialHotkey = null)
        {
            InitializeComponent();
            DataContext = this;

            // Show the existing value immediately
            HotkeyText = initialHotkey ?? "";
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Esc cancels
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                return;
            }

            // Backspace/Delete clears (nice UX)
            if (e.Key == Key.Back || e.Key == Key.Delete)
            {
                HotkeyText = "";
                e.Handled = true;
                return;
            }

            var text = BuildHotkeyText(e);
            if (string.IsNullOrWhiteSpace(text))
                return;

            HotkeyText = text;
            e.Handled = true;
        }

        private static string? BuildHotkeyText(KeyEventArgs e)
        {
            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return null;

            var mods = Keyboard.Modifiers;
            var key = (e.Key == Key.System) ? e.SystemKey : e.Key;

            var s = "";
            if (mods.HasFlag(ModifierKeys.Control)) s += "Ctrl+";
            if (mods.HasFlag(ModifierKeys.Shift)) s += "Shift+";
            if (mods.HasFlag(ModifierKeys.Alt)) s += "Alt+";
            if (mods.HasFlag(ModifierKeys.Windows)) s += "Win+";

            s += key.ToString();
            return s;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            HotkeyText = "";
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
