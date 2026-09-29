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
        private SingleInstanceService? _instance;
        private TrayWindowController? _tray;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _instance = new SingleInstanceService();
            if (!_instance.IsPrimary)
            {
                if (!e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase)) _instance.ShowExisting();
                Shutdown();
                return;
            }
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
            var main = new MainWindow(settings);
            MainWindow = main;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            try { settings.ReconcileWindowsStartup(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
            { MessageBox.Show("Windows startup could not be updated. You can change it in Settings → General.\n\n" + ex.Message, "EchoPad startup"); }
            try
            {
                _tray = new TrayWindowController(main, new WindowsTrayIcon(), () => main.GlobalSettings.Desktop,
                    main.OpenGeneralSettings, () => main.WaitForPendingCapturesAsync(CancellationToken.None));
                _tray.ExitFailed += ex => MessageBox.Show(main, ex.Message, "EchoPad could not exit");
                _tray.Start(e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or ArgumentException)
            {
                _tray?.Dispose(); _tray = null;
                main.ShowInTaskbar = true; main.Show();
                MessageBox.Show(main, "The tray icon could not be created. EchoPad will stay visible.\n\n" + ex.Message, "EchoPad tray");
            }
            // Start audio and MIDI even when the window begins hidden; Loaded is not raised until Show.
            main.StartBackgroundServices();
            _instance.Listen(() =>
            {
                if (!Dispatcher.HasShutdownStarted)
                    Dispatcher.BeginInvoke(new Action(() => { if (_tray != null) _tray.Show(); else { main.Show(); main.Activate(); } }));
            });
        }
        public void ExitForUpdate() { _tray?.AllowExit(); Shutdown(); }
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        { _tray?.AllowExit(); base.OnSessionEnding(e); }
        protected override void OnExit(ExitEventArgs e)
        { _tray?.Dispose(); _instance?.Dispose(); base.OnExit(e); }
    }

}
