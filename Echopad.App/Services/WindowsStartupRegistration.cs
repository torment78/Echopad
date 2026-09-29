using System.IO;
using Microsoft.Win32;

namespace Echopad.App.Services;

public interface IStartupRegistration
{
    void SetEnabled(bool enabled);
}

public interface IStartupEntryStore
{
    string? Read();
    void Write(string command);
    void Remove();
}

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private readonly IStartupEntryStore _store;
    private readonly string _executable;
    public WindowsStartupRegistration(string executable, IStartupEntryStore? store = null)
    { _executable = Path.GetFullPath(executable); _store = store ?? new RegistryStartupEntryStore(); }

    public static string BuildCommand(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.Contains('"') || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Windows startup needs a full path to the EchoPad executable.");
        string command = "\"" + executable + "\" --startup";
        if (command.Length > 260) throw new ArgumentException("The EchoPad path is too long for Windows startup. Install EchoPad in a shorter folder.");
        return command;
    }

    public void SetEnabled(bool enabled)
    {
        string? current = _store.Read();
        if (!enabled) { if (current != null) _store.Remove(); return; }
        if (!File.Exists(_executable)) throw new FileNotFoundException("Install or publish EchoPad before enabling Windows startup.", _executable);
        string command = BuildCommand(_executable);
        if (!string.Equals(command, current, StringComparison.Ordinal)) _store.Write(command);
    }
}

public sealed class RegistryStartupEntryStore : IStartupEntryStore
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string EntryName = "ElkaSoft.EchoPad";
    public string? Read() { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(EntryName) as string; }
    public void Write(string command) { using var key = Registry.CurrentUser.CreateSubKey(RunKey); key.SetValue(EntryName, command, RegistryValueKind.String); }
    public void Remove() { using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true); key?.DeleteValue(EntryName, throwOnMissingValue: false); }
}
