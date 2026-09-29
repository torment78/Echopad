using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Echopad.App;
using Echopad.App.Services;
using Echopad.App.Settings;
using Echopad.Core;

static partial class Program
{
    static void DesktopChecks(string root)
    {
        string executable = FixtureFile(Path.Combine(root, "Program Files", "ElkaSoft", "EchoPad"), "Echopad.App.exe", "not an executable; startup fixture only");
        var store = new StartupStore();
        var startup = new WindowsStartupRegistration(executable, store);
        startup.SetEnabled(true);
        Check(store.Value == "\"" + executable + "\" --startup", "Windows startup command quotes paths with spaces and marks login launches");
        startup.SetEnabled(true);
        Check(store.Writes == 1, "unchanged startup registration does not rewrite the Run entry");
        store.Value = "\"C:\\Old EchoPad\\Echopad.App.exe\" --startup";
        startup.SetEnabled(true);
        Check(store.Value == WindowsStartupRegistration.BuildCommand(executable), "startup registration repairs the executable path after relocation");
        startup.SetEnabled(false);
        Check(store.Value == null && store.Removes == 1, "turning Windows startup off removes the owned Run entry");
        bool invalid = false;
        try { WindowsStartupRegistration.BuildCommand("relative.exe"); } catch (ArgumentException) { invalid = true; }
        Check(invalid, "startup refuses relative executable paths");
        invalid = false;
        try { new WindowsStartupRegistration(Path.Combine(root, "missing.exe"), store).SetEnabled(true); } catch (FileNotFoundException) { invalid = true; }
        Check(invalid && store.Value == null, "missing executable cannot register broken Windows startup");

        var service = new SettingsService(Path.Combine(root, "settings"), startup);
        var settings = service.Load();
        Check(!settings.Desktop.StartWithWindows && !settings.Desktop.StartToTray && !settings.Desktop.CloseToTray && !settings.Desktop.MinimizeToTray,
            "all desktop options default off for existing and fresh users");
        using (var vm = new SettingsViewModel(service, new Devices(), new Devices()))
        {
            vm.StartWithWindows = vm.StartToTray = vm.CloseToTray = vm.MinimizeToTray = true;
            vm.Save();
        }
        settings = service.Load();
        Check(settings.Desktop.StartWithWindows && settings.Desktop.StartToTray && settings.Desktop.CloseToTray && settings.Desktop.MinimizeToTray && store.Value != null,
            "General switches persist and update the injected startup registration");
        new ProfileService(service).ApplyProfileToSettings(settings, 3);
        Check(service.Load().Desktop.CloseToTray && service.Load().Desktop.StartWithWindows, "profile switching preserves global startup and tray options");
        byte[] before = File.ReadAllBytes(Path.Combine(service.DataDirectory, "echopad.settings.json"));
        var failing = new SettingsService(service.DataDirectory, new FailingStartup());
        settings.Desktop.StartWithWindows = false;
        invalid = false;
        try { failing.Save(settings); } catch (UnauthorizedAccessException) { invalid = true; }
        Check(invalid && File.ReadAllBytes(Path.Combine(service.DataDirectory, "echopad.settings.json")).SequenceEqual(before),
            "registration failure is reported without overwriting saved settings");
        FixtureFile(service.DataDirectory, "echopad.settings.json", "{\"Desktop\":null}");
        Check(service.Load().Desktop is not null && !service.Load().Desktop.StartToTray, "legacy null desktop settings normalize safely");

        string instanceName = @"Local\EchoPad.Tests." + Guid.NewGuid().ToString("N");
        using (var primary = new SingleInstanceService(instanceName))
        using (var secondary = new SingleInstanceService(instanceName))
        using (var activated = new AutoResetEvent(false))
        {
            Check(primary.IsPrimary && !secondary.IsPrimary, "only one process owns an EchoPad instance identity");
            primary.Listen(() => activated.Set());
            secondary.ShowExisting();
            Check(activated.WaitOne(TimeSpan.FromSeconds(2)), "a repeated launch signals the existing instance to show");
        }
        using (var reopened = new SingleInstanceService(instanceName))
            Check(reopened.IsPrimary, "closing the primary instance releases its launch identity");

        var hostContext = SynchronizationContext.Current;
        using (var nativeIcon = new WindowsTrayIcon())
            Check(!nativeIcon.Visible, "native tray backend loads the embedded icon and menu without showing an unwanted icon");
        Check(ReferenceEquals(hostContext, SynchronizationContext.Current), "native tray construction preserves the host async context");
        TrayChecks();
    }

    static void CaptureExitChecks(MainWindow main, MainViewModel model)
    {
        var wait = typeof(MainWindow).GetMethod("WaitForPendingCapturesAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var captures = typeof(MainWindow).GetField("_pendingCaptureCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task Wait() => (Task)wait.Invoke(main, new object[] { CancellationToken.None })!;
        var pad = model.Pads[0]; var oldState = pad.State; bool oldBusy = pad.IsBusy;
        try
        {
            pad.State = PadState.Playing; pad.IsBusy = true;
            Check(Wait().IsCompletedSuccessfully, "exit and updates do not wait for a playing clip to end");
            captures.SetValue(main, 1);
            Task pending = Wait();
            Check(!pending.IsCompleted, "exit preparation waits while a capture is being saved");
            captures.SetValue(main, 0);
            PumpUntil(() => pending.IsCompleted);
            Check(pending.IsCompletedSuccessfully, "exit preparation continues once the capture save completes");
        }
        finally { captures.SetValue(main, 0); pad.State = oldState; pad.IsBusy = oldBusy; }
    }

    static Window TrayTestWindow() => new() { Width = 180, Height = 100, Left = -32000, Top = -32000,
        ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow };
    static void PumpUntil(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(3);
        while (!done() && DateTime.UtcNow < until)
        { Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); Thread.Sleep(5); }
        if (!done()) throw new TimeoutException("The tray lifecycle did not complete.");
    }
    static void TrayChecks()
    {
        var window = TrayTestWindow();
        var icon = new TestTrayIcon();
        var settings = new DesktopSettings { StartToTray = true, CloseToTray = true, MinimizeToTray = true };
        int closed = 0, openedSettings = 0;
        window.Closed += (_, _) => closed++;
        var pendingCapture = new TaskCompletionSource();
        using var tray = new TrayWindowController(window, icon, () => settings, () => openedSettings++, () => pendingCapture.Task, _ => { });
        try
        {
            tray.Start();
            Check(icon.Visible && !window.IsVisible && !window.ShowInTaskbar, "open-to-tray creates a reachable hidden window without a taskbar button");
            icon.Open();
            Check(window.IsVisible && window.ShowInTaskbar, "tray Open restores the window and taskbar entry");
            window.Close();
            Check(closed == 0 && !window.IsVisible && icon.Visible, "close-to-tray cancels real shutdown so runtime services stay alive");
            tray.Show();
            window.WindowState = WindowState.Minimized;
            Check(!window.IsVisible && !window.ShowInTaskbar && closed == 0, "minimize-to-tray hides without closing the application");
            icon.Settings();
            Check(openedSettings == 1 && window.IsVisible && window.WindowState == WindowState.Normal, "tray Settings restores the app and opens its General page");
            Task quitting = tray.ExitAsync();
            Check(!quitting.IsCompleted && closed == 0, "tray Exit waits for a pending capture to finish");
            pendingCapture.SetResult();
            PumpUntil(() => quitting.IsCompleted);
            quitting.GetAwaiter().GetResult();
            Check(closed == 1 && !icon.Visible && icon.Disposed, "explicit tray Exit closes and releases the notification icon even when close-to-tray is on");
        }
        finally { tray.AllowExit(); if (closed == 0) window.Close(); }

        var ordinary = TrayTestWindow(); var ordinaryIcon = new TestTrayIcon(); int normalClosed = 0;
        ordinary.Closed += (_, _) => normalClosed++;
        using var normal = new TrayWindowController(ordinary, ordinaryIcon, () => new DesktopSettings(), () => { }, () => Task.CompletedTask, _ => { });
        normal.Start(); ordinary.Close(); PumpUntil(() => normalClosed == 1);
        Check(normalClosed == 1 && ordinaryIcon.Disposed, "normal X still exits when close-to-tray is disabled");

        var shutdownWindow = TrayTestWindow(); var shutdownIcon = new TestTrayIcon(); int shutdownClosed = 0;
        shutdownWindow.Closed += (_, _) => shutdownClosed++;
        using var shutdown = new TrayWindowController(shutdownWindow, shutdownIcon, () => settings, () => { }, () => Task.CompletedTask, _ => { });
        shutdown.Start(showOverride: true);
        Check(shutdownWindow.IsVisible, "explicit show overrides open-to-tray");
        shutdown.AllowExit(); shutdownWindow.Close();
        Check(shutdownClosed == 1 && shutdownIcon.Disposed, "update and session-ending exit bypass close-to-tray");

        var failedWindow = TrayTestWindow(); var failedIcon = new TestTrayIcon(); int failedClosed = 0;
        failedWindow.Closed += (_, _) => failedClosed++;
        using var failed = new TrayWindowController(failedWindow, failedIcon, () => settings, () => { },
            () => Task.FromException(new IOException("capture still busy")), _ => { });
        failed.Start();
        bool rejected = false;
        try { failed.ExitAsync().GetAwaiter().GetResult(); } catch (IOException) { rejected = true; }
        Check(rejected && failedClosed == 0 && failedIcon.Visible, "failed exit preparation keeps the app reachable in the tray");
        failed.AllowExit(); failedWindow.Close();
    }

    sealed class StartupStore : IStartupEntryStore
    {
        public string? Value; public int Writes, Removes;
        public string? Read() => Value;
        public void Write(string command) { Value = command; Writes++; }
        public void Remove() { Value = null; Removes++; }
    }
    sealed class FailingStartup : IStartupRegistration
    { public void SetEnabled(bool enabled) => throw new UnauthorizedAccessException("startup entry blocked"); }
    sealed class TestTrayIcon : ITrayIcon
    {
        public bool Visible { get; set; }
        public bool Disposed { get; private set; }
        public event Action? OpenRequested;
        public event Action? SettingsRequested;
        public event Action? ExitRequested;
        public void Open() => OpenRequested?.Invoke();
        public void Settings() => SettingsRequested?.Invoke();
        public void Exit() => ExitRequested?.Invoke();
        public void Dispose() => Disposed = true;
    }
}
