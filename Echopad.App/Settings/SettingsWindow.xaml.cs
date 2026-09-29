using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Echopad.App.Services;
using Microsoft.Win32;

namespace Echopad.App.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private readonly DispatcherTimer _meterTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private IDisposable? _inputBlock;
    private BindingEditor? _learning;
    public SettingsWindow(SettingsViewModel vm, string? initialPage = null)
    {
        InitializeComponent(); _vm = vm; DataContext = vm;
        InstalledVersionText.Text = "Installed version: " + UpdateService.InstalledVersion;
        ReleaseChannelText.Text = ReleaseVersion.Parse(UpdateService.InstalledVersion)?.IsDevelopment == true ? "Development channel · includes unsigned development releases" : "Stable release channel";
        Closed += (_, _) => { _updateCancellation.Cancel(); _updateCancellation.Dispose(); };
        if (initialPage != null) Pages.SelectedItem = Pages.Items.OfType<TabItem>().FirstOrDefault(t => Equals(t.Tag, initialPage)) ?? Pages.Items[0];
        _vm.Saved += Apply;
        _meterTimer.Tick += (_, _) =>
        {
            if (Owner is not MainWindow main) return;
            foreach (var route in vm.Routes.Where(r => r.IsInput)) route.UpdateMeter(main.GetInputPeakDb(route.InputNumber));
        };
        Loaded += (_, _) => { _inputBlock = UiInputBlocker.Acquire("Settings"); _meterTimer.Start(); };
        Closing += OnClosing;
        Closed += (_, _) => { CancelMidiLearn(); _meterTimer.Stop(); _vm.Saved -= Apply; _vm.Dispose(); _inputBlock?.Dispose(); };
    }
    private void Apply() { if (Owner is MainWindow main) main.ApplySettingsLive(); }
    public void StartMidiLearn(BindingEditor editor)
    {
        bool same = ReferenceEquals(_learning, editor); CancelMidiLearn(); if (same) return;
        // Flush device selections before arming learn; learning text never enters saved settings.
        _vm.Save();
        if (Owner is not MainWindow main || !main.HasMidiInput)
        {
            MessageBox.Show(this, "Select an available MIDI input on the MIDI & colors tab first.", "MIDI input"); return;
        }
        _learning = editor; editor.SetLearning(true);
        main.BeginMidiLearn(bind =>
        {
            if (!ReferenceEquals(_learning, editor)) return;
            editor.Learned(bind.Split('|')[0].Trim()); _learning = null;
        });
    }
    public void CancelMidiLearn() { _learning?.SetLearning(false); _learning = null; (Owner as MainWindow)?.CancelMidiLearn(); }
    private bool Commit()
    {
        // Finish text edits before validation and saving.
        System.Windows.Input.Keyboard.ClearFocus();
        if (HasErrors(this)) { MessageBox.Show(this, "Please correct the highlighted field before saving.", "Check settings"); return false; }
        if (!_vm.ValidateSettings(out var error)) { MessageBox.Show(this, error, "Check settings"); return false; }
        try { _vm.Save(); return true; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save settings"); return false; }
    }
    private static bool HasErrors(DependencyObject node)
    {
        if (Validation.GetHasError(node)) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) if (HasErrors(VisualTreeHelper.GetChild(node, i))) return true;
        return false;
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!_closingForUpdate && !Commit()) e.Cancel = true; }
    private void Save_Click(object sender, RoutedEventArgs e) => Commit();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void ResetAppearance_Click(object sender, RoutedEventArgs e) => _vm.ResetAppearance();
    private void PickInputColor_Click(object sender, RoutedEventArgs e)
    {
        bool firstInput = (sender as FrameworkElement)?.Tag?.ToString() == "1";
        var hex = firstInput ? _vm.UiArmedInput1Hex : _vm.UiArmedInput2Hex;
        using var picker = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex ?? "");
            picker.Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
        }
        catch { /* An unfinished hex edit can be replaced by choosing a color. */ }
        var owner = new PaletteOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (picker.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK) return;
        var chosen = picker.Color;
        string selected = $"#{chosen.R:X2}{chosen.G:X2}{chosen.B:X2}";
        if (firstInput) _vm.UiArmedInput1Hex = selected;
        else _vm.UiArmedInput2Hex = selected;
    }
    private sealed class PaletteOwner(IntPtr handle) : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle => handle;
    }
    private void AddFolder_Click(object sender, RoutedEventArgs e) { var picker = new OpenFolderDialog { Title = "Add audio library folder" }; if (picker.ShowDialog(this) == true) _vm.AddFolder(picker.FolderName); }
    private void RemoveFolder_Click(object sender, RoutedEventArgs e) { if (AudioFolderList.SelectedItem is string folder) _vm.RemoveFolder(folder); }
    private void BrowseDrop_Click(object sender, RoutedEventArgs e) { var picker = new OpenFolderDialog { Title = "Choose drop folder" }; if (picker.ShowDialog(this) == true) _vm.DropWatchFolder = picker.FolderName; }

    private void TestMidi_Click(object sender, RoutedEventArgs e) { if (Commit() && Owner is MainWindow main) main.SendHardMidiOutTest_Value25(); }
    private void UseLibraryFolder_Click(object sender, RoutedEventArgs e) { if (AudioFolderList.SelectedItem is string folder) _vm.DropWatchFolder = folder; }
    private void ClearDrop_Click(object sender, RoutedEventArgs e) { _vm.DropFolderEnabled = false; _vm.DropWatchFolder = ""; }
    private void DefaultDrop_Click(object sender, RoutedEventArgs e) { _vm.DropWatchFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Echopad", "Drop"); }
    private void OpenDrop_Click(object sender, RoutedEventArgs e)
    {
        if (System.IO.Directory.Exists(_vm.DropWatchFolder))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_vm.DropWatchFolder!) { UseShellExecute = true });
    }
}
