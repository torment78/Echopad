using System.ComponentModel;
using System.Windows;
using Echopad.Core;

namespace Echopad.App.Services;

public interface ITrayIcon : IDisposable
{
    bool Visible { get; set; }
    event Action? OpenRequested;
    event Action? SettingsRequested;
    event Action? ExitRequested;
}

public sealed class TrayWindowController : IDisposable
{
    private readonly Window _window;
    private readonly ITrayIcon _icon;
    private readonly Func<DesktopSettings> _settings;
    private readonly Action _openSettings;
    private readonly Func<Task> _prepareExit;
    private readonly Action<Window> _activate;
    private bool _allowExit, _exiting, _disposed;
    public event Action<Exception>? ExitFailed;
    public TrayWindowController(Window window, ITrayIcon icon, Func<DesktopSettings> settings,
        Action openSettings, Func<Task> prepareExit, Action<Window>? activate = null)
    {
        _window = window; _icon = icon; _settings = settings; _openSettings = openSettings; _prepareExit = prepareExit;
        _activate = activate ?? (target => target.Activate());
        _window.Closing += OnClosing;
        _window.StateChanged += OnStateChanged;
        _window.Closed += OnClosed;
        _icon.OpenRequested += Show;
        _icon.SettingsRequested += OpenSettings;
        _icon.ExitRequested += Exit;
    }
    public void Start(bool showOverride = false)
    {
        _icon.Visible = true;
        if (_settings().StartToTray && !showOverride)
        {
            _window.ShowInTaskbar = false;
            new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle();
            Hide();
        }
        else Show();
    }
    public void Show()
    {
        if (_disposed) return;
        _window.ShowInTaskbar = true;
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Show();
        _activate(_window.OwnedWindows.Cast<Window>().LastOrDefault(w => w.IsVisible) ?? _window);
    }
    private void Hide() { _window.Hide(); _window.ShowInTaskbar = false; }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowExit && _settings().CloseToTray && _icon.Visible)
        { e.Cancel = true; Hide(); }
        else if (!_allowExit)
        {
            e.Cancel = true;
            // Complete an in-flight capture before an ordinary X/Alt+F4 exit too.
            _window.Dispatcher.BeginInvoke(new Action(Exit));
        }
    }
    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (!_allowExit && _window.WindowState == WindowState.Minimized && _settings().MinimizeToTray && _icon.Visible) Hide();
    }
    private void OpenSettings() { bool enabled = _window.IsEnabled; Show(); if (enabled) _openSettings(); }
    private async void Exit()
    {
        try { await ExitAsync(); }
        catch (Exception ex) { Show(); ExitFailed?.Invoke(ex); }
    }
    public async Task ExitAsync()
    {
        if (_exiting || _disposed) return;
        _exiting = true;
        try
        {
            foreach (Window child in _window.OwnedWindows.Cast<Window>().ToArray())
            { child.Close(); if (child.IsVisible) return; }
            await _prepareExit();
            _allowExit = true;
            _window.Close();
            if (!_disposed) _allowExit = false; // Another closing handler may have canceled.
        }
        finally { _exiting = false; }
    }
    public void AllowExit() => _allowExit = true; // Updates and Windows session ending must really exit.
    private void OnClosed(object? sender, EventArgs e) => Dispose();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Closing -= OnClosing; _window.StateChanged -= OnStateChanged; _window.Closed -= OnClosed;
        _icon.OpenRequested -= Show; _icon.SettingsRequested -= OpenSettings; _icon.ExitRequested -= Exit;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
