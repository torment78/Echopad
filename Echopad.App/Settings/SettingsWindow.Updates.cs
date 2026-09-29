using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using Echopad.App.Services;

namespace Echopad.App.Settings;

public partial class SettingsWindow
{
    private readonly CancellationTokenSource _updateCancellation = new();
    private string? _releaseUrl;
    private InstallerAsset? _installer;
    private CancellationTokenSource? _downloadCancellation;
    private bool _closingForUpdate;
    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_installer != null) await DownloadUpdateAsync(_installer);
        else await CheckForUpdateAsync();
    }
    private async void RecheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckForUpdateAsync();
    private async Task CheckForUpdateAsync()
    {
        UpdateCheckButton.IsEnabled = false;
        RecheckUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Checking GitHub releases…";
        try { ShowUpdateResult(await new UpdateService().CheckAsync(UpdateService.InstalledVersion, _updateCancellation.Token)); }
        catch (OperationCanceledException) { }
        finally { UpdateCheckButton.IsEnabled = true; RecheckUpdateButton.IsEnabled = true; }
    }
    internal void ShowUpdateResult(UpdateResult result)
    {
        UpdateStatus.Text = result.Message;
        UpdateReleaseName.Text = result.ReleaseName ?? "";
        _releaseUrl = result.ReleaseUrl;
        _installer = result.Available ? result.Installer : null;
        OpenReleaseButton.Visibility = result.Available ? Visibility.Visible : Visibility.Collapsed;
        RecheckUpdateButton.Visibility = _installer != null ? Visibility.Visible : Visibility.Collapsed;
        UpdateCheckButton.Content = _installer != null ? "Download and install" : result.Available ? "Update available · Check again" : "Check for updates";
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
    private async Task DownloadUpdateAsync(InstallerAsset installer)
    {
        if (_downloadCancellation != null || !Commit()) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_updateCancellation.Token);
        _downloadCancellation = cancellation;
        UpdateCheckButton.IsEnabled = false;
        RecheckUpdateButton.IsEnabled = false;
        UpdateDownloadProgress.Value = 0;
        UpdateDownloadProgress.Visibility = Visibility.Visible;
        CancelUpdateButton.IsEnabled = true;
        CancelUpdateButton.Visibility = Visibility.Visible;
        UpdateStatus.Text = "Downloading installer…";
        var progress = new Progress<InstallerDownloadProgress>(p =>
        {
            if (_downloadCancellation != cancellation || cancellation.IsCancellationRequested) return;
            UpdateDownloadProgress.Value = p.Percent;
            UpdateStatus.Text = $"Downloading installer… {p.Percent:0}% ({p.Received / 1048576d:0.0} / {p.Total / 1048576d:0.0} MB)";
        });
        try
        {
            await new InstallerUpdateService().DownloadAndInstallAsync(installer, _vm.UpdatesDirectory,
                async token =>
                {
                    UpdateStatus.Text = "Download verified. Preparing to install…";
                    if (Owner is MainWindow main) await main.WaitForPendingCapturesAsync(token);
                    if (!Commit()) throw new InvalidOperationException("Save the corrected settings before installing the update.");
                    CancelUpdateButton.IsEnabled = false;
                    UpdateStatus.Text = "Starting installer. EchoPad will close after the Windows prompt is accepted…";
                }, InstallerUpdateService.LaunchInstaller,
                () => { _closingForUpdate = true; Application.Current.Shutdown(); }, progress, cancellation.Token);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { UpdateStatus.Text = "Installer canceled. EchoPad is still running."; }
        catch (OperationCanceledException)
        { UpdateStatus.Text = cancellation.IsCancellationRequested ? "Download canceled. EchoPad is still running." : "Download timed out. Please try again."; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        { UpdateStatus.Text = "Could not install the update. EchoPad is still running. " + ex.Message; }
        finally
        {
            _downloadCancellation = null;
            UpdateCheckButton.IsEnabled = true;
            RecheckUpdateButton.IsEnabled = true;
            CancelUpdateButton.Visibility = Visibility.Collapsed;
            UpdateDownloadProgress.Visibility = Visibility.Collapsed;
        }
    }
    private void CancelUpdate_Click(object sender, RoutedEventArgs e)
    {
        _downloadCancellation?.Cancel();
        CancelUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Canceling download…";
    }
    private void OpenRelease_Click(object sender, RoutedEventArgs e)
    {
        if (!UpdateService.IsReleaseUrl(_releaseUrl)) return;
        try { Process.Start(new ProcessStartInfo(_releaseUrl!) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not open the release page"); }
    }
}
