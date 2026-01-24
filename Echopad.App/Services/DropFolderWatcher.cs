using Echopad.App.Services;
using Echopad.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Echopad.App.Services
{
    public sealed class DropFolderWatcher : IDisposable
    {
        private readonly SettingsService _settingsService;
        private FileSystemWatcher? _watcher;
        private readonly SemaphoreSlim _gate = new(1, 1);

        // NEW: de-dupe fast duplicate events (Created + Changed + Rename spam)
        private readonly Dictionary<string, DateTime> _recent = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan RecentWindow = TimeSpan.FromSeconds(2);

        // You decide what formats you accept
        private static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".mp3", ".aac", ".flac", ".ogg", ".wma", ".m4a", ".aiff" };

        public event Action<string>? FileArrived;

        public DropFolderWatcher(SettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public void Start(string folderPath)
        {
            Stop();

            Directory.CreateDirectory(folderPath);

            _watcher = new FileSystemWatcher(folderPath)
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite,
                Filter = "*.*"
            };

            _watcher.Created += OnCreatedOrChanged;
            _watcher.Changed += OnCreatedOrChanged;
            _watcher.Renamed += OnRenamed;
        }

        public void Stop()
        {
            if (_watcher == null) return;

            try
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnCreatedOrChanged;
                _watcher.Changed -= OnCreatedOrChanged;
                _watcher.Renamed -= OnRenamed;
                _watcher.Dispose();
            }
            catch { }
            finally
            {
                _watcher = null;
            }
        }

        private void OnRenamed(object sender, RenamedEventArgs e) => _ = HandleIncomingAsync(e.FullPath);
        private void OnCreatedOrChanged(object sender, FileSystemEventArgs e) => _ = HandleIncomingAsync(e.FullPath);

        private async Task HandleIncomingAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            // Only care about real audio files
            var ext = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(ext) || !AudioExt.Contains(ext))
                return;

            // Wait until the file is actually finished being written
            if (!await WaitForFileReadyAsync(path, TimeSpan.FromSeconds(10)))
                return;

            // De-dupe rapid change events + serialize assignment logic
            await _gate.WaitAsync();
            try
            {
                // NEW: recent de-dupe (Created + Changed often fires twice)
                CleanupRecent();

                if (_recent.TryGetValue(path, out var last) && (DateTime.UtcNow - last) < RecentWindow)
                    return;

                _recent[path] = DateTime.UtcNow;

                FileArrived?.Invoke(path);
            }
            finally
            {
                _gate.Release();
            }
        }

        private void CleanupRecent()
        {
            if (_recent.Count == 0) return;

            var now = DateTime.UtcNow;
            var toRemove = _recent.Where(kv => (now - kv.Value) > TimeSpan.FromSeconds(10))
                                  .Select(kv => kv.Key)
                                  .ToList();

            foreach (var k in toRemove)
                _recent.Remove(k);
        }

        private static async Task<bool> WaitForFileReadyAsync(string path, TimeSpan timeout)
        {
            var stopAt = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < stopAt)
            {
                try
                {
                    if (!File.Exists(path))
                        return false;

                    // NEW: use broader sharing flags for "copy into folder" scenarios
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);

                    if (stream.Length > 0)
                        return true;
                }
                catch
                {
                    // still being written/locked
                }

                await Task.Delay(150);
            }

            return false;
        }

        public void Dispose()
        {
            Stop();
            _gate.Dispose();
        }
    }
}
