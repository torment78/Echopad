using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using Echopad.App.Services;

namespace Echopad.App.Settings;

public partial class SettingsWindow
{
    private readonly CancellationTokenSource _updateCancellation = new();
    private string? _releaseUrl;
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateCheckButton.IsEnabled = false;
        UpdateStatus.Text = "Checking GitHub releases…";
        try { ShowUpdateResult(await new UpdateService().CheckAsync(UpdateService.InstalledVersion, _updateCancellation.Token)); }
        catch (OperationCanceledException) { }
        finally { UpdateCheckButton.IsEnabled = true; }
    }
    internal void ShowUpdateResult(UpdateResult result)
    {
        UpdateStatus.Text = result.Message;
        UpdateReleaseName.Text = result.ReleaseName ?? "";
        _releaseUrl = result.ReleaseUrl;
        OpenReleaseButton.Visibility = result.Available ? Visibility.Visible : Visibility.Collapsed;
        UpdateCheckButton.Content = result.Available ? "Update available · Check again" : "Check for updates";
        if (result.Available)
        {
            UpdateCheckButton.Background = new SolidColorBrush(Color.FromRgb(225, 136, 46));
            UpdateCheckButton.Foreground = Brushes.Black;
            UpdateCheckButton.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 187, 86));
        }
        else
        {
            UpdateCheckButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            UpdateCheckButton.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
            UpdateCheckButton.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
        }
    }
    private void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        if (!UpdateService.IsReleaseUrl(_releaseUrl)) return;
        try { Process.Start(new ProcessStartInfo(_releaseUrl!) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open the release page"); }
    }
}
