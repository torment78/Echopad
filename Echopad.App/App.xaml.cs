using System.Configuration;
using System.Data;
using System.Windows;
using System.IO;
using Echopad.App.Services;

namespace Echopad.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            SettingsService settings;
            try { settings = new SettingsService(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                MessageBox.Show("EchoPad could not migrate or open its saved data. Your original files have been kept. " +
                    "Close older EchoPad instances, check the file or folder below, and start EchoPad again.\n\n" + ex.Message,
                    "EchoPad data migration", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            MainWindow = new MainWindow(settings);
            MainWindow.Show();
        }
    }

}
