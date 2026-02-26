using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Echopad.App.Settings
{
    public partial class ProfileManagerWindow : Window
    {
        private bool _isLearningHotkey;
        private bool _allowModifierOnly;
        private Action<string>? _hotkeyLearnCallback;
        // NEW: block MainWindow pads while this window is open
        private IDisposable? _uiBlock;
        public ProfileManagerWindow(ProfileManagerViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;

            // Always hooked, gated by _isLearningHotkey
            PreviewKeyDown += ProfileManagerWindow_PreviewKeyDown;

            // NEW: block MainWindow pad input while Profiles window is open
            Loaded += (_, __) =>
            {
                _uiBlock ??= Echopad.App.Services.UiInputBlocker.Acquire("ProfileManagerWindow");
            };

            Closed += (_, __) =>
            {
                try { _uiBlock?.Dispose(); } catch { }
                _uiBlock = null;
            };
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // -------------------------------------------------
        // SLOT HOTKEY LEARN (Ctrl+Shift+F1, etc.)
        // -------------------------------------------------
        private void BtnLearnHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.Tag is not ProfileSlotRowViewModel row) return;

            _isLearningHotkey = true;
            _allowModifierOnly = false;

            _hotkeyLearnCallback = hk =>
            {
                row.HotkeyBind = hk;

                if (DataContext is ProfileManagerViewModel vm)
                {
                    vm.StatusText = $"Hotkey set for Slot {row.Index}: {hk}";
                    vm.RequestAutoSave();
                }
            };

            if (DataContext is ProfileManagerViewModel vm2)
                vm2.StatusText = $"Listening for hotkey… (Slot {row.Index})";

            Keyboard.Focus(this);
        }

        // -------------------------------------------------
        // GLOBAL MODIFIER LEARN (Ctrl+Shift, Win, etc.)
        // -------------------------------------------------
        private void BtnLearnHotkeyModifier_Click(object sender, RoutedEventArgs e)
        {
            _isLearningHotkey = true;
            _allowModifierOnly = true;

            _hotkeyLearnCallback = hk =>
            {
                if (DataContext is ProfileManagerViewModel vm)
                {
                    vm.HotkeyModifier = hk;
                    vm.StatusText = $"Hotkey modifier set: {hk}";
                    vm.RequestAutoSave();
                }
            };

            if (DataContext is ProfileManagerViewModel vm2)
                vm2.StatusText = "Listening for hotkey modifier… (Ctrl / Shift / Alt / Win)";

            Keyboard.Focus(this);
        }

        // -------------------------------------------------
        // KEY CAPTURE
        // -------------------------------------------------
        private void ProfileManagerWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isLearningHotkey)
                return;

            var hot = BuildHotkeyText(e, _allowModifierOnly);
            if (string.IsNullOrWhiteSpace(hot))
                return;

            e.Handled = true;

            _isLearningHotkey = false;

            _hotkeyLearnCallback?.Invoke(hot);
            _hotkeyLearnCallback = null;
        }

        // -------------------------------------------------
        // HOTKEY FORMATTER
        // -------------------------------------------------
        private static string? BuildHotkeyText(KeyEventArgs e, bool allowModifierOnly)
        {
            var mods = Keyboard.Modifiers;

            bool isPureModifier =
                e.Key is Key.LeftCtrl or Key.RightCtrl
                or Key.LeftShift or Key.RightShift
                or Key.LeftAlt or Key.RightAlt
                or Key.LWin or Key.RWin;

            // Modifier-only allowed (for global modifier)
            if (allowModifierOnly && isPureModifier)
            {
                return BuildModifierString(mods);
            }

            // Ignore pure modifiers for normal slot hotkeys
            if (isPureModifier)
                return null;

            var key = (e.Key == Key.System) ? e.SystemKey : e.Key;

            var sb = new StringBuilder();
            if (mods.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
            if (mods.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
            if (mods.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
            if (mods.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");

            sb.Append(key.ToString());
            return sb.ToString();
        }

        private static string? BuildModifierString(ModifierKeys mods)
        {
            var sb = new StringBuilder();

            if (mods.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
            if (mods.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
            if (mods.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
            if (mods.HasFlag(ModifierKeys.Windows)) sb.Append("Win+");

            if (sb.Length == 0)
                return null;

            sb.Length--; // remove trailing '+'
            return sb.ToString();
        }
                // =========================================================
        // NEW: Ensure VM gets disposed so profile-switch lock is released
        // =========================================================
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                if (DataContext is IDisposable d)
                    d.Dispose();
            }
            catch { }

            base.OnClosed(e);
        }

        // =========================================================
        // Clear binding helpers (MIDI/Hotkey boxes)
        // =========================================================
        private void BindClearMenu_Clear_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi) return;
            if (mi.Parent is not ContextMenu cm) return;
            if (cm.PlacementTarget is not TextBox tb) return;

            ClearBoundTextBox(tb);
        }

        private void BindBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox tb) return;

            if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                ClearBoundTextBox(tb);
                e.Handled = true;
            }
        }

        private static void ClearBoundTextBox(TextBox tb)
        {
            tb.Text = string.Empty;
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }


    }
}
