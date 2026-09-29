using System.Security.Principal;

namespace Echopad.App.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _wait;
    public bool IsPrimary { get; }
    public SingleInstanceService(string? name = null)
    {
        name ??= @"Local\ElkaSoft.EchoPad." + WindowsIdentity.GetCurrent().User!.Value;
        _mutex = new Mutex(initiallyOwned: true, name, out bool created);
        IsPrimary = created;
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Show");
    }
    public void ShowExisting() => _activation.Set();
    public void Listen(Action show) => _wait = ThreadPool.RegisterWaitForSingleObject(
        _activation, (_, _) => show(), null, Timeout.Infinite, executeOnlyOnce: false);
    public void Dispose()
    {
        _wait?.Unregister(null);
        _activation.Dispose();
        if (IsPrimary) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
