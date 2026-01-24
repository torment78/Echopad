using Echopad.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Echopad.Audio
{
    public sealed class AudioEngine : IAudioEngine
    {
        // NEW: lets MainWindow reset UI when playback ends naturally
        public event Action<int>? PadPlaybackEnded;

        // One playback chain per pad
        private readonly ConcurrentDictionary<int, IWavePlayer> _players = new();
        private readonly ConcurrentDictionary<int, AudioFileReader> _readers = new();

        public async Task PlayPadAsync(PadModel pad, string? mainOutDeviceId, string? monitorOutDeviceId, bool previewToMonitor)
        {
            // Always stop existing playback for this pad before starting
            StopPad(pad);

            if (pad == null) return;
            if (string.IsNullOrWhiteSpace(pad.ClipPath)) return;
            if (!File.Exists(pad.ClipPath)) return;

            // Decide which device to use:
            // - Normal play => Main output device
            // - Edit preview (and enabled) => Monitor output device
            var deviceId = previewToMonitor ? monitorOutDeviceId : mainOutDeviceId;

            Debug.WriteLine($"[AudioEngine] Play pad={pad.Index} preview={previewToMonitor} deviceId='{deviceId}' clip='{pad.ClipPath}' start={pad.StartMs} end={pad.EndMs}");

            // Create reader
            var reader = new AudioFileReader(pad.ClipPath);

            // Apply absolute trim (StartMs..EndMs)
            var totalMs = (int)reader.TotalTime.TotalMilliseconds;

            var startMs = Math.Clamp(pad.StartMs, 0, Math.Max(0, totalMs));
            var endMs = pad.EndMs <= 0 ? totalMs : Math.Clamp(pad.EndMs, 0, Math.Max(0, totalMs));
            if (endMs < startMs) endMs = startMs;

            // Seek to start
            reader.CurrentTime = TimeSpan.FromMilliseconds(startMs);

            // Limit playback length
            var playMs = Math.Max(0, endMs - startMs);
            var limited = new OffsetSampleProvider(reader)
            {
                SkipOver = TimeSpan.Zero,
                Take = TimeSpan.FromMilliseconds(playMs)
            };

            // Build output
            var player = CreateWasapiOutById(deviceId);

            // NEW: when playback stops (natural end OR stop), clean up + notify UI
            player.PlaybackStopped += (_, __) =>
            {
                try
                {
                    // StopPad disposes and removes dictionaries safely
                    StopPad(pad);

                    // Tell UI to reset button/state
                    PadPlaybackEnded?.Invoke(pad.Index);
                }
                catch { }
            };

            // Init + store
            player.Init(limited.ToWaveProvider());
            _players[pad.Index] = player;
            _readers[pad.Index] = reader;

            // Play
            player.Play();

            await Task.CompletedTask;
        }

        public void StopPad(PadModel pad)
        {
            if (pad == null) return;

            if (_players.TryRemove(pad.Index, out var player))
            {
                try { player.Stop(); } catch { }
                try { player.Dispose(); } catch { }
            }

            if (_readers.TryRemove(pad.Index, out var reader))
            {
                try { reader.Dispose(); } catch { }
            }
        }

        private static IWavePlayer CreateWasapiOutById(string? deviceId)
        {
            // If deviceId is missing, use default audio endpoint
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return new WasapiOut(AudioClientShareMode.Shared, 50);
            }

            try
            {
                var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDevice(deviceId);
                return new WasapiOut(device, AudioClientShareMode.Shared, false, 50);
            }
            catch
            {
                // Fallback to default if device lookup fails
                return new WasapiOut(AudioClientShareMode.Shared, 50);
            }
        }
    }
}
