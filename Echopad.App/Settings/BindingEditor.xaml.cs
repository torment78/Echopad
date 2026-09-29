using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace Echopad.App.Settings;

public partial class BindingEditor : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(BindingEditor), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Kind { get; set; } = "Hotkey";
    public BindingEditor() { InitializeComponent(); }
    public void SetLearning(bool learning) { LearnButton.Content = learning ? "Cancel" : "Learn"; LearnButton.ToolTip = learning ? "Press or move a MIDI control; click again to cancel." : "Learn " + Kind; }
    public void Learned(string text) { SetCurrentValue(TextProperty, text); SetLearning(false); }
    private void Field_Click(object sender, MouseButtonEventArgs e) { e.Handled = true; Learn(); }
    private void Learn_Click(object sender, RoutedEventArgs e) => Learn();
    private void Learn()
    {
        if (Kind == "Midi") { (Window.GetWindow(this) as SettingsWindow)?.StartMidiLearn(this); return; }
        var capture = new HotkeyCaptureWindow(Text, Kind == "Modifiers") { Owner = Window.GetWindow(this) };
        if (capture.ShowDialog() == true) SetCurrentValue(TextProperty, capture.HotkeyText);
    }
    private void Clear_Click(object sender, RoutedEventArgs e) { (Window.GetWindow(this) as SettingsWindow)?.CancelMidiLearn(); SetCurrentValue(TextProperty, ""); }
}
