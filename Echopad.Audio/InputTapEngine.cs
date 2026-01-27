using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Diagnostics;

namespace Echopad.Audio
{
    /// <summary>
    /// Captures audio from a selected WASAPI endpoint into a RollingAudioBuffer.
    /// - If endpoint is CAPTURE (microphone etc) -> WasapiCapture
    /// - If endpoint is RENDER (speakers / many Voicemeeter buses) -> WasapiLoopbackCapture
    /// </summary>
    public sealed class InputTapEngine : IDisposable
    {
        private readonly string? _deviceIdRaw;

        private MMDevice? _device;
        private IWaveIn? _capture;

        private WaveFormat? _waveFormat;
        private int _channels;
        private bool _running;

        public RollingAudioBuffer? Buffer { get; private set; }

        public InputTapEngine(string? deviceId)
        {
            _deviceIdRaw = deviceId;
        }

        public void Start(int rollingSeconds)
        {
            Stop();

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

                if (forceLoopback || _device.DataFlow == DataFlow.Render)
                {
                    _capture = new WasapiLoopbackCapture(_device);
                }
                else
                {
                    _capture = new WasapiCapture(_device);
                }

                _waveFormat = _capture.WaveFormat;
                _channels = _waveFormat.Channels;

                // Rolling buffer stores float samples. We'll convert incoming to float.
                // Keep wave format metadata for later commits.
                Buffer = new RollingAudioBuffer(rollingSeconds, _waveFormat);

                _capture.DataAvailable += Capture_DataAvailable;
                _capture.RecordingStopped += Capture_RecordingStopped;

                _capture.StartRecording();
                _running = true;

                Debug.WriteLine($"[Tap] Started. Format={_waveFormat.SampleRate}Hz ch={_waveFormat.Channels} bits={_waveFormat.BitsPerSample} enc={_waveFormat.Encoding}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Tap] Start failed: " + ex);
                Stop();
            }
        }

        public void Stop()
        {
            try
            {
                if (_capture != null)
                {
                    _capture.DataAvailable -= Capture_DataAvailable;
                    _capture.RecordingStopped -= Capture_RecordingStopped;

                    if (_running)
                        _capture.StopRecording();

                    _capture.Dispose();
                }
            }
            catch { }

            _running = false;
            _capture = null;

            try { _device?.Dispose(); } catch { }
            _device = null;

            // Keep Buffer reference (optional). If you prefer clearing:
            // Buffer = null;
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

                // Convert input bytes to float[] interleaved
                var floats = ConvertToFloatInterleaved(e.Buffer, e.BytesRecorded, _waveFormat);

                if (floats.Length > 0)
                    Buffer.AddSamples(floats, floats.Length);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Tap] DataAvailable error: " + ex);
            }
        }

        private static float[] ConvertToFloatInterleaved(byte[] data, int bytes, WaveFormat fmt)
        {
            if (bytes <= 0) return Array.Empty<float>();

            // Most WASAPI will be IEEE float 32 or PCM 16.
            // We'll support: IEEE float 32, PCM 16, PCM 24, PCM 32.
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
                    // sign extend 24-bit
                    if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000);
                    floats[i] = v / 8388608f; // 2^23
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

            // Fallback: try to interpret as 16-bit
            Debug.WriteLine($"[Tap] Unsupported format: enc={fmt.Encoding} bps={fmt.BitsPerSample}. Returning silence.");
            return Array.Empty<float>();
        }

        /// <summary>
        /// Your device provider may store IDs like "wasapi-in:{id}" or "wasapi:{id}" etc.
        /// This strips common prefixes so MMDeviceEnumerator.GetDevice() receives the real endpoint ID.
        /// </summary>
        private static string NormalizeDeviceId(string raw)
        {
            var s = raw.Trim();

            if (s.StartsWith("loop:", StringComparison.OrdinalIgnoreCase))
                return s.Substring(5);

            if (s.StartsWith("wasapi:", StringComparison.OrdinalIgnoreCase))
                return s.Substring(7);

            if (s.StartsWith("audio:", StringComparison.OrdinalIgnoreCase))
                return s.Substring(6);

            return s;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
