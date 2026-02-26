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

        // -------------------------------------------------
        // Helper: is this pad currently playing (engine truth)
        // -------------------------------------------------
        public bool IsPadPlaying(int padIndex)
            => _players.ContainsKey(padIndex) || _vbanCts.ContainsKey(padIndex);

        // =========================================================
        // OLD signature (kept) — maps to endpoint model internally
        // =========================================================
        public async Task PlayPadAsync(PadModel pad, string? mainOutDeviceId, string? monitorOutDeviceId, bool previewToMonitor)
        {
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
            if (pad == null) return;
            if (string.IsNullOrWhiteSpace(pad.ClipPath)) return;
            if (!File.Exists(pad.ClipPath)) return;

            // HARD STOP any existing run for this pad FIRST (safe + non-reentrant)
            StopPad(pad);

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

            // =====================================================
            // NEW: Apply per-pad gain (dB -> linear)
            // =====================================================
            var gainDb = Math.Clamp(pad.GainDb, -60f, 20f);
            var linear = (float)Math.Pow(10.0, gainDb / 20.0);

            // VolumeSampleProvider can be > 1.0 for boost
            ISampleProvider playSource = new VolumeSampleProvider(limited)
            {
                Volume = linear
            };

            // store reader so StopPad disposes it
            _readers[pad.Index] = reader;

            if (endpoint.Mode == AudioEndpointMode.Vban)
            {
                await StartVbanTxPadAsync(pad, playSource, endpoint);
                return;
            }

            // Local: WASAPI playback
            var player = CreateWasapiOutById(endpoint.LocalDeviceId);

            // IMPORTANT:
            // PlaybackStopped MUST NOT call StopPad(), because StopPad() calls player.Stop()
            // which can re-enter PlaybackStopped and create “needs many clicks” behavior.
            player.PlaybackStopped += (_, __) =>
            {
                try
                {
                    CleanupLocalOnly(pad.Index);
                    PadPlaybackEnded?.Invoke(pad.Index);
                }
                catch { }
            };

            player.Init(playSource.ToWaveProvider());
            _players[pad.Index] = player;

            player.Play();

            await Task.CompletedTask;
        }

        private async Task StartVbanTxPadAsync(PadModel pad, ISampleProvider sampleProvider, OutputEndpointSettings endpoint)
        {
            // Cancel any existing VBAN run (already stopped by StopPad)
            var cts = new CancellationTokenSource();
            _vbanCts[pad.Index] = cts;

            // Create TX engine
            endpoint.Vban ??= new VbanTxSettings();
            var tx = new VbanTxEngine(endpoint.Vban);
            _vbanTx[pad.Index] = tx;

            // Pace in real-time based on sample rate
            var srcFmt = sampleProvider.WaveFormat;
            int sampleRate = srcFmt.SampleRate;
            int channels = srcFmt.Channels;

            int frameSamplesPerChannel = Math.Clamp(endpoint.Vban.FrameSamples, 64, 1024);
            int floatsPerFrame = frameSamplesPerChannel * channels;

            var buffer = new float[floatsPerFrame];

            Debug.WriteLine($"[AudioEngine] VBAN TX pad={pad.Index} -> {endpoint.Vban.RemoteIp}:{endpoint.Vban.Port} stream='{endpoint.Vban.StreamName}' sr={sampleRate} ch={channels} frame={frameSamplesPerChannel}");

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
                        CleanupVbanOnly(pad.Index);
                        CleanupReaderOnly(pad.Index);
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

            int idx = pad.Index;

            // Remove first (engine truth changes immediately)
            if (_players.TryRemove(idx, out var player))
            {
                try { player.PlaybackStopped -= Player_PlaybackStopped_NoOp; } catch { }
                try { player.Stop(); } catch { }
                try { player.Dispose(); } catch { }
            }

            CleanupVbanOnly(idx);
            CleanupReaderOnly(idx);
        }
        // =====================================================
        // NEW: dB to linear gain
        // =====================================================
        private static float DbToLinear(float db)
        {
            // safety clamp (matches your UI)
            db = Math.Clamp(db, -60f, 20f);

            // 0 dB -> 1.0
            // -6 dB -> ~0.501
            // +6 dB -> ~1.995
            return (float)Math.Pow(10.0, db / 20.0);
        }
        // Used only to allow safe “-=” even if we didn’t attach this handler.
        private void Player_PlaybackStopped_NoOp(object? s, StoppedEventArgs e) { }

        // -------------------------------------------------
        // Cleanup helpers (NO Stop())
        // -------------------------------------------------
        private void CleanupLocalOnly(int padIndex)
        {
            if (_players.TryRemove(padIndex, out var player))
            {
                try { player.Dispose(); } catch { }
            }

            CleanupReaderOnly(padIndex);
        }

        private void CleanupVbanOnly(int padIndex)
        {
            if (_vbanCts.TryRemove(padIndex, out var cts))
            {
                try { cts.Cancel(); } catch { }
                try { cts.Dispose(); } catch { }
            }

            if (_vbanTx.TryRemove(padIndex, out var tx))
            {
                try { tx.Dispose(); } catch { }
            }
        }

        private void CleanupReaderOnly(int padIndex)
        {
            if (_readers.TryRemove(padIndex, out var reader))
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
