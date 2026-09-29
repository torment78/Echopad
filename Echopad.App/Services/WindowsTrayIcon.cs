using System.Windows;
using Forms = System.Windows.Forms;

namespace Echopad.App.Services;

public sealed class WindowsTrayIcon : ITrayIcon
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ContextMenuStrip _menu;
    private readonly System.Drawing.Icon _image;
    public event Action? OpenRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public WindowsTrayIcon()
    {
        var hostContext = SynchronizationContext.Current;
        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Echopad.App;component/Assets/AppIcon/AppIcon.ico"))!.Stream;
            using var original = new System.Drawing.Icon(stream, 32, 32);
            _image = (System.Drawing.Icon)original.Clone();
            _menu = new Forms.ContextMenuStrip { BackColor = System.Drawing.Color.FromArgb(28, 31, 36), ForeColor = System.Drawing.Color.WhiteSmoke, ShowImageMargin = false };
            _menu.Items.Add("Open EchoPad", null, (_, _) => OpenRequested?.Invoke());
            _menu.Items.Add("Settings", null, (_, _) => SettingsRequested?.Invoke());
            _menu.Items.Add(new Forms.ToolStripSeparator());
            _menu.Items.Add("Exit EchoPad", null, (_, _) => ExitRequested?.Invoke());
            _icon = new Forms.NotifyIcon { Text = "EchoPad", Icon = _image, ContextMenuStrip = _menu };
            _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        }
        finally
        {
            // Creating a WinForms control can install its own context. WPF owns our message loop.
            SynchronizationContext.SetSynchronizationContext(hostContext);
        }
    }
    public bool Visible { get => _icon.Visible; set => _icon.Visible = value; }
    public void Dispose() { _icon.Visible = false; _icon.Dispose(); _menu.Dispose(); _image.Dispose(); }
}
