using System.Diagnostics;
using NAudio.Wave;
using Echopad.Core;

namespace Echopad.Audio.Vban;

/// <summary>VBAN output for the pad editor's existing play/pause preview pipeline.</summary>
public sealed class VbanWavePlayer : IWavePlayer
{
    private readonly VbanTxSettings _settings;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly VbanTxEngine _sender;
    private ISampleProvider? _source;
    private CancellationTokenSource? _cancel;
    private Task? _run;
    private volatile PlaybackState _state;
    public VbanWavePlayer(VbanTxSettings settings) { _settings = settings; _sender = new(settings); }
    public PlaybackState PlaybackState => _state;
    public WaveFormat OutputWaveFormat => _source?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    public float Volume { get; set; } = 1;
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;
    public void Init(IWaveProvider provider) { Stop(); _source = provider.ToSampleProvider(); }
    public void Play()
    {
        if (_source == null) throw new InvalidOperationException("Initialize the preview source first.");
        if (_state == PlaybackState.Playing) return;
        _cancel = new(); var token = _cancel.Token; var source = _source;
        _state = PlaybackState.Playing;
        _run = Task.Run(async () =>
        {
            Exception? error = null;
            try
            {
                int channels = source.WaveFormat.Channels, rate = source.WaveFormat.SampleRate;
                int frame = Math.Clamp(_settings.FrameSamples, 16, 256);
                var buffer = new float[frame * channels];
                long framesSent = 0; var clock = Stopwatch.StartNew();
                while (!token.IsCancellationRequested)
                {
                    int read = source.Read(buffer, 0, buffer.Length); if (read == 0) break;
                    for (int i = 0; i < read; i++) buffer[i] *= Volume;
                    _sender.SendInterleavedFloat32Frame(buffer, read, rate, channels);
                    framesSent += read / channels;
                    double remaining = framesSent * 1000d / rate - clock.Elapsed.TotalMilliseconds;
                    if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { error = ex; }
            finally
            {
                _state = PlaybackState.Stopped;
                void Notify(object? _) => PlaybackStopped?.Invoke(this, new StoppedEventArgs(error));
                if (_context != null) _context.Post(Notify, null); else Notify(null);
            }
        });
    }
    public void Pause() { Stop(); _state = PlaybackState.Paused; }
    public void Stop()
    {
        _cancel?.Cancel();
        // Join before the caller repositions or disposes the shared audio reader.
        try { _run?.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        _run = null; _cancel?.Dispose(); _cancel = null; _state = PlaybackState.Stopped;
    }
    public void Dispose() { Stop(); _sender.Dispose(); }
}
