using Echopad.Core;
using Echopad.Audio.Vban;

using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Echopad.Audio
{
    public sealed class AudioEngine : IAudioEngine
    {
        public event Action<int>? PadPlaybackEnded;

        // Local playback chains per pad
        private readonly ConcurrentDictionary<int, IWavePlayer> _players = new();
        private readonly ConcurrentDictionary<int, AudioFileReader> _readers = new();

        // VBAN streaming per pad
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _vbanCts = new();
        private readonly ConcurrentDictionary<int, VbanTxEngine> _vbanTx = new();

        // =========================================================
        // OLD signature (kept) — maps to endpoint model internally
        // =========================================================
        public async Task PlayPadAsync(PadModel pad, string? mainOutDeviceId, string? monitorOutDeviceId, bool previewToMonitor)
        {
            // Map legacy device ids -> endpoint settings (Local mode)
            var out1 = new OutputEndpointSettings
            {
                Mode = AudioEndpointMode.Local,
                LocalDeviceId = mainOutDeviceId
            };

            var out2 = new OutputEndpointSettings
            {
                Mode = AudioEndpointMode.Local,
                LocalDeviceId = monitorOutDeviceId
            };

            await PlayPadAsync(pad, out1, out2, previewToMonitor);
        }

        // =========================================================
        // NEW signature — per-channel Local/VBAN output
        // =========================================================
        public async Task PlayPadAsync(PadModel pad, OutputEndpointSettings out1, OutputEndpointSettings out2, bool previewToMonitor)
        {
            StopPad(pad);

            if (pad == null) return;
            if (string.IsNullOrWhiteSpace(pad.ClipPath)) return;
            if (!File.Exists(pad.ClipPath)) return;

            var endpoint = previewToMonitor ? out2 : out1;

            Debug.WriteLine($"[AudioEngine] Play pad={pad.Index} preview={previewToMonitor} mode={endpoint.Mode} clip='{pad.ClipPath}' start={pad.StartMs} end={pad.EndMs}");

            // Reader (float samples)
            var reader = new AudioFileReader(pad.ClipPath);

            // Apply absolute trim (StartMs..EndMs)
            var totalMs = (int)reader.TotalTime.TotalMilliseconds;

            var startMs = Math.Clamp(pad.StartMs, 0, Math.Max(0, totalMs));
            var endMs = pad.EndMs <= 0 ? totalMs : Math.Clamp(pad.EndMs, 0, Math.Max(0, totalMs));
            if (endMs < startMs) endMs = startMs;

            reader.CurrentTime = TimeSpan.FromMilliseconds(startMs);

            var playMs = Math.Max(0, endMs - startMs);

            var limited = new OffsetSampleProvider(reader)
            {
                SkipOver = TimeSpan.Zero,
                Take = TimeSpan.FromMilliseconds(playMs)
            };

            // Store reader so StopPad always disposes it
            _readers[pad.Index] = reader;

            if (endpoint.Mode == AudioEndpointMode.Vban)
            {
                await StartVbanTxPadAsync(pad, limited, endpoint);
                return;
            }

            // Local: WASAPI playback (existing behavior)
            var player = CreateWasapiOutById(endpoint.LocalDeviceId);

            player.PlaybackStopped += (_, __) =>
            {
                try
                {
                    StopPad(pad);
                    PadPlaybackEnded?.Invoke(pad.Index);
                }
                catch { }
            };

            player.Init(limited.ToWaveProvider());
            _players[pad.Index] = player;

            player.Play();

            await Task.CompletedTask;
        }

        private async Task StartVbanTxPadAsync(PadModel pad, ISampleProvider sampleProvider, OutputEndpointSettings endpoint)
        {
            // Cancel any existing VBAN run (should already be stopped by StopPad)
            var cts = new CancellationTokenSource();
            _vbanCts[pad.Index] = cts;

            // Create TX engine
            var tx = new VbanTxEngine(endpoint.Vban);
            _vbanTx[pad.Index] = tx;

            // Pace in real-time based on sample rate
            int sampleRate = endpoint.Vban.SampleRate;
            int channels = endpoint.Vban.Channels;

            // NOTE:
            // AudioFileReader's sampleProvider has its own WaveFormat sample rate.
            // For v1 we assume you configure VBAN to match the file's sample rate.
            // If you want forced SR conversion later, we can add resampling.
            var srcFmt = sampleProvider.WaveFormat;
            sampleRate = srcFmt.SampleRate;
            channels = srcFmt.Channels;

            // Frame sizing
            int frameSamplesPerChannel = Math.Clamp(endpoint.Vban.FrameSamples, 64, 1024);
            int floatsPerFrame = frameSamplesPerChannel * channels;

            var buffer = new float[floatsPerFrame];

            Debug.WriteLine($"[AudioEngine] VBAN TX pad={pad.Index} -> {endpoint.Vban.RemoteIp}:{endpoint.Vban.Port} stream='{endpoint.Vban.StreamName}' sr={sampleRate} ch={channels} frame={frameSamplesPerChannel}");

            // Run streaming loop
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        int read = sampleProvider.Read(buffer, 0, buffer.Length);
                        if (read <= 0)
                            break;

                        tx.SendInterleavedFloat32Frame(buffer, read, sampleRate, channels);

                        // Pace to real-time
                        // durationSeconds = framesPerChannel / sampleRate
                        int samplesPerChannelSent = read / Math.Max(1, channels);
                        double seconds = (double)samplesPerChannelSent / Math.Max(1, sampleRate);
                        int delayMs = (int)Math.Round(seconds * 1000.0);

                        if (delayMs > 0)
                            await Task.Delay(delayMs, cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Debug.WriteLine("[AudioEngine] VBAN TX error: " + ex);
                }
                finally
                {
                    try
                    {
                        StopPad(pad);
                        PadPlaybackEnded?.Invoke(pad.Index);
                    }
                    catch { }
                }
            }, cts.Token);

            await Task.CompletedTask;
        }

        public void StopPad(PadModel pad)
        {
            if (pad == null) return;

            // Stop local player
            if (_players.TryRemove(pad.Index, out var player))
            {
                try { player.Stop(); } catch { }
                try { player.Dispose(); } catch { }
            }

            // Stop VBAN streaming
            if (_vbanCts.TryRemove(pad.Index, out var cts))
            {
                try { cts.Cancel(); } catch { }
                try { cts.Dispose(); } catch { }
            }

            if (_vbanTx.TryRemove(pad.Index, out var tx))
            {
                try { tx.Dispose(); } catch { }
            }

            // Dispose reader
            if (_readers.TryRemove(pad.Index, out var reader))
            {
                try { reader.Dispose(); } catch { }
            }
        }

        private static IWavePlayer CreateWasapiOutById(string? deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                return new WasapiOut(AudioClientShareMode.Shared, 50);

            try
            {
                var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDevice(deviceId);
                return new WasapiOut(device, AudioClientShareMode.Shared, false, 50);
            }
            catch
            {
                return new WasapiOut(AudioClientShareMode.Shared, 50);
            }
        }
    }
}
