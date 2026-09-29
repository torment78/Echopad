using System.Windows;
using Microsoft.Win32;

namespace Echopad.App.Settings;

public partial class PadSettingsWindow
{
    private void ChoosePadImage_Click(object sender, RoutedEventArgs e)
    {
        bool playing = (sender as FrameworkElement)?.Tag?.ToString() == "Playing";
        var dialog = new OpenFileDialog { Title = playing ? "Choose playing PNG" : "Choose stopped PNG", Filter = "PNG images (*.png)|*.png" };
        if (dialog.ShowDialog(this) != true) return;
        try { _vm.ImportImage(playing, dialog.FileName); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not use this PNG"); }
    }
    private void RemovePadImage_Click(object sender, RoutedEventArgs e) => _vm.ClearImage((sender as FrameworkElement)?.Tag?.ToString() == "Playing");
    private void ClearPadMidi_Click(object sender, RoutedEventArgs e)
    {
        if (_isLearningMidi) (Application.Current?.MainWindow as MainWindow)?.CancelMidiLearn();
        _isLearningMidi = false; _vm.IsMidiLearning = false; _vm.MidiTriggerRaw = null;
    }
}
