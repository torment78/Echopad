using Echopad.Audio.Vban;
using Echopad.Core;

using NAudio.CoreAudioApi;
using NAudio.Wave;

using System;
using System.Diagnostics;

namespace Echopad.Audio
{
    /// <summary>
    /// Captures audio into a RollingAudioBuffer.
    /// LOCAL:
    /// - CAPTURE endpoint -> WasapiCapture
    /// - RENDER endpoint  -> WasapiLoopbackCapture (or deviceId "loop:{id}")
    ///
    /// VBAN:
    /// - Receives VBAN AUDIO stream via UDP and writes to RollingAudioBuffer
    /// </summary>
    public sealed class InputTapEngine : IDisposable
    {
        // OLD input path (kept for compatibility)
        private readonly string? _deviceIdRaw;

        // NEW endpoint path
        private readonly InputEndpointSettings? _endpoint;

        // WASAPI
        private MMDevice? _device;
        private IWaveIn? _capture;

        // VBAN
        private VbanRxEngine? _vbanRx;

        private WaveFormat? _waveFormat;
        private int _channels;
        private bool _running;

        private int _requestedRollingSeconds;

        public RollingAudioBuffer? Buffer { get; private set; }

        // =========================================================
        // OLD ctor (kept)
        // =========================================================
        public InputTapEngine(string? deviceId)
        {
            _deviceIdRaw = deviceId;
        }

        // =========================================================
        // NEW ctor (preferred)
        // =========================================================
        public InputTapEngine(InputEndpointSettings input)
        {
            _endpoint = input ?? throw new ArgumentNullException(nameof(input));
            _deviceIdRaw = input.LocalDeviceId;
        }

        public void Start(int rollingSeconds)
        {
            Stop();

            _requestedRollingSeconds = Math.Max(1, rollingSeconds);

            var mode = _endpoint?.Mode ?? AudioEndpointMode.Local;

            if (mode == AudioEndpointMode.Vban)
            {
                StartVban();
                return;
            }

            StartWasapi(_requestedRollingSeconds);
        }

        // =========================================================
        // WASAPI
        // =========================================================
        private void StartWasapi(int rollingSeconds)
        {
            if (string.IsNullOrWhiteSpace(_deviceIdRaw))
            {
                Debug.WriteLine("[Tap] No device selected.");
                return;
            }

            try
            {
                bool forceLoopback = _deviceIdRaw.StartsWith("loop:", StringComparison.OrdinalIgnoreCase);

                var endpointId = NormalizeDeviceId(_deviceIdRaw);
                var enumerator = new MMDeviceEnumerator();
                _device = enumerator.GetDevice(endpointId);

                Debug.WriteLine($"[Tap] Using endpoint: {_device.FriendlyName} | Flow={_device.DataFlow} | ForceLoop={forceLoopback}");

                _capture = (forceLoopback || _device.DataFlow == DataFlow.Render)
                    ? new WasapiLoopbackCapture(_device)
                    : new WasapiCapture(_device);

                _waveFormat = _capture.WaveFormat;
                _channels = _waveFormat.Channels;

                Buffer = new RollingAudioBuffer(rollingSeconds, _waveFormat);

                _capture.DataAvailable += Capture_DataAvailable;
                _capture.RecordingStopped += Capture_RecordingStopped;

                _capture.StartRecording();
                _running = true;

                Debug.WriteLine($"[Tap] Started (WASAPI). {_waveFormat.SampleRate}Hz ch={_channels} enc={_waveFormat.Encoding}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Tap] Start (WASAPI) failed: " + ex);
                Stop();
            }
        }

        // =========================================================
        // VBAN RX
        // =========================================================
        private void StartVban()
        {
            try
            {
                if (_endpoint == null)
                {
                    Debug.WriteLine("[Tap] VBAN mode requested but endpoint is null.");
                    return;
                }

                _vbanRx = new VbanRxEngine(_endpoint.Vban);
                _vbanRx.SamplesReceived += VbanRx_SamplesReceived;
                _vbanRx.Start();

                Buffer = null;
                _waveFormat = null;
                _channels = 0;

                _running = true;

                Debug.WriteLine($"[Tap] Started (VBAN). Port={_endpoint.Vban.Port} Stream='{_endpoint.Vban.StreamName}'");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Tap] Start (VBAN) failed: " + ex);
                Stop();
            }
        }

        private void VbanRx_SamplesReceived(float[] samples, int sampleCount)
        {
            try
            {
                if (sampleCount <= 0 || _vbanRx == null)
                    return;

                var fmt = _vbanRx.WaveFormat;
                if (fmt == null)
                    return;

                if (Buffer == null || _waveFormat == null ||
                    _waveFormat.SampleRate != fmt.SampleRate ||
                    _waveFormat.Channels != fmt.Channels ||
                    _waveFormat.BitsPerSample != fmt.BitsPerSample ||
                    _waveFormat.Encoding != fmt.Encoding)
                {
                    _waveFormat = fmt;
                    _channels = fmt.Channels;
                    Buffer = new RollingAudioBuffer(_requestedRollingSeconds, fmt);

                    Debug.WriteLine($"[Tap] VBAN buffer init: {fmt.SampleRate}Hz ch={fmt.Channels}");
                }

                Buffer.AddSamples(samples, sampleCount);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Tap] VBAN SamplesReceived error: " + ex);
            }
        }

        // =========================================================
        // Cleanup
        // =========================================================
        public void Stop()
        {
            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= Capture_DataAvailable;
                    _capture.RecordingStopped -= Capture_RecordingStopped;
                    if (_running) _capture.StopRecording();
                    _capture.Dispose();
                }
            }
            catch { }

            _capture = null;

            try { _device?.Dispose(); } catch { }
            _device = null;

            try
            {
                if (_vbanRx != null)
                {
                    _vbanRx.SamplesReceived -= VbanRx_SamplesReceived;
                    _vbanRx.Dispose();
                }
            }
            catch { }

            _vbanRx = null;
            _running = false;
        }

        private void Capture_RecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
                Debug.WriteLine("[Tap] Recording stopped with error: " + e.Exception);
            _running = false;
        }

        private void Capture_DataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                if (Buffer == null || _waveFormat == null)
                    return;

                var floats = ConvertToFloatInterleaved(e.Buffer, e.BytesRecorded, _waveFormat);
                if (floats.Length > 0)
                    Buffer.AddSamples(floats, floats.Length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Tap] DataAvailable error: " + ex);
            }
        }

        // =========================================================
        // Helpers
        // =========================================================
        private static float[] ConvertToFloatInterleaved(byte[] data, int bytes, WaveFormat fmt)
        {
            if (bytes <= 0) return Array.Empty<float>();

            if (fmt.Encoding == WaveFormatEncoding.IeeeFloat && fmt.BitsPerSample == 32)
            {
                int samples = bytes / 4;
                var floats = new float[samples];
                System.Buffer.BlockCopy(data, 0, floats, 0, samples * 4);
                return floats;
            }

            if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 16)
            {
                int samples = bytes / 2;
                var floats = new float[samples];
                int o = 0;
                for (int i = 0; i < samples; i++)
                {
                    short s = (short)(data[o] | (data[o + 1] << 8));
                    floats[i] = s / 32768f;
                    o += 2;
                }
                return floats;
            }

            if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 24)
            {
                int samples = bytes / 3;
                var floats = new float[samples];
                int o = 0;
                for (int i = 0; i < samples; i++)
                {
                    int v = (data[o] | (data[o + 1] << 8) | (data[o + 2] << 16));
                    if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                    floats[i] = v / 8388608f;
                    o += 3;
                }
                return floats;
            }

            if (fmt.Encoding == WaveFormatEncoding.Pcm && fmt.BitsPerSample == 32)
            {
                int samples = bytes / 4;
                var floats = new float[samples];
                int o = 0;
                for (int i = 0; i < samples; i++)
                {
                    int v = BitConverter.ToInt32(data, o);
                    floats[i] = v / 2147483648f;
                    o += 4;
                }
                return floats;
            }

            return Array.Empty<float>();
        }

        private static string NormalizeDeviceId(string raw)
        {
            var s = raw.Trim();
            if (s.StartsWith("loop:", StringComparison.OrdinalIgnoreCase)) return s[5..];
            if (s.StartsWith("wasapi:", StringComparison.OrdinalIgnoreCase)) return s[7..];
            if (s.StartsWith("audio:", StringComparison.OrdinalIgnoreCase)) return s[6..];
            return s;
        }

        public void Dispose() => Stop();
    }
}
