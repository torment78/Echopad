using NAudio.Wave;
using System;

namespace Echopad.Audio
{
    /// <summary>
    /// Thread-safe rolling ring buffer of interleaved float samples.
    /// Stores last N seconds of audio in RAM.
    /// </summary>
    public sealed class RollingAudioBuffer
    {
        private readonly object _lock = new();

        private float[] _ring = Array.Empty<float>();
        private int _writeIndex;     // in samples (not frames)
        private int _filled;         // number of valid samples (<= _ring.Length)

        public WaveFormat Format { get; private set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public int SampleRate => Format.SampleRate;
        public int Channels => Format.Channels;

        public int CapacitySamples => _ring.Length;

        public RollingAudioBuffer(int seconds, WaveFormat format)
        {
            Reset(seconds, format);
        }

        public void Reset(int seconds, WaveFormat format)
        {
            if (seconds < 1) seconds = 1;
            Format = format ?? throw new ArgumentNullException(nameof(format));

            // float samples, interleaved (frames * channels)
            int capacitySamples = Math.Max(Format.SampleRate * Format.Channels * seconds, 1);

            lock (_lock)
            {
                _ring = new float[capacitySamples];
                _writeIndex = 0;
                _filled = 0;
            }
        }

        /// <summary>Add interleaved float samples to the ring buffer.</summary>
        public void AddSamples(float[] interleaved, int sampleCount)
        {
            if (interleaved == null) return;
            if (sampleCount <= 0) return;

            lock (_lock)
            {
                if (_ring.Length == 0) return;

                if (sampleCount > interleaved.Length)
                    sampleCount = interleaved.Length;

                // If more samples than capacity, keep only last part
                if (sampleCount >= _ring.Length)
                {
                    int start = sampleCount - _ring.Length;
                    Array.Copy(interleaved, start, _ring, 0, _ring.Length);
                    _writeIndex = 0;
                    _filled = _ring.Length;
                    return;
                }

                int remaining = sampleCount;
                int srcOffset = 0;

                while (remaining > 0)
                {
                    int spaceToEnd = _ring.Length - _writeIndex;
                    int toCopy = Math.Min(spaceToEnd, remaining);

                    Array.Copy(interleaved, srcOffset, _ring, _writeIndex, toCopy);

                    _writeIndex += toCopy;
                    if (_writeIndex >= _ring.Length)
                        _writeIndex = 0;

                    srcOffset += toCopy;
                    remaining -= toCopy;
                }

                _filled = Math.Min(_filled + sampleCount, _ring.Length);
            }
        }

        // =====================================================
        // NEW: ReadAll() for RollingBufferCommitService compatibility
        // Returns the currently buffered audio in chronological order (oldest -> newest).
        // =====================================================
        public float[] ReadAll()
        {
            lock (_lock)
            {
                if (_ring.Length == 0 || _filled == 0)
                    return Array.Empty<float>();

                var dst = new float[_filled];

                // Oldest sample is writeIndex - filled (wrapped)
                int start = _writeIndex - _filled;
                if (start < 0) start += _ring.Length;

                int idx = start;
                for (int i = 0; i < _filled; i++)
                {
                    dst[i] = _ring[idx];
                    idx++;
                    if (idx >= _ring.Length) idx = 0;
                }

                return dst;
            }
        }

        /// <summary>
        /// Returns RMS (0..1-ish) across the last windowMs of audio.
        /// Downmixes by averaging channels per frame.
        /// </summary>
        public float GetRmsLastMs(int windowMs)
        {
            if (windowMs <= 0) windowMs = 120;

            lock (_lock)
            {
                if (_ring.Length == 0 || _filled == 0)
                    return 0f;

                int framesToRead = (int)((long)SampleRate * windowMs / 1000L);
                if (framesToRead < 1) framesToRead = 1;

                int samplesToRead = framesToRead * Channels;
                samplesToRead = Math.Min(samplesToRead, _filled);

                if (samplesToRead <= 0)
                    return 0f;

                int start = _writeIndex - samplesToRead;
                if (start < 0) start += _ring.Length;

                double sumSq = 0.0;
                int frameCount = samplesToRead / Channels;
                if (frameCount <= 0) return 0f;

                int idx = start;
                for (int f = 0; f < frameCount; f++)
                {
                    double mono = 0.0;
                    for (int ch = 0; ch < Channels; ch++)
                    {
                        mono += _ring[idx];
                        idx++;
                        if (idx >= _ring.Length) idx = 0;
                    }

                    mono /= Channels;
                    sumSq += mono * mono;
                }

                double meanSq = sumSq / frameCount;
                double rms = Math.Sqrt(meanSq);

                if (rms < 0) rms = 0;
                if (rms > 1) rms = 1;
                return (float)rms;
            }
        }

        /// <summary>
        /// Returns dBFS approx from RMS of last windowMs. Floor at -90 dB.
        /// </summary>
        public float GetDbLastMs(int windowMs)
        {
            var rms = GetRmsLastMs(windowMs);
            if (rms <= 0.000001f)
                return -90f;

            var db = 20f * (float)Math.Log10(rms);
            if (db < -90f) db = -90f;
            if (db > 0f) db = 0f;
            return db;
        }

        // =====================================================
        // PEAK + dB PEAK (more like Voicemeeter meters)
        // =====================================================
        public float GetPeakLastMs(int windowMs)
        {
            if (windowMs <= 0) windowMs = 120;

            lock (_lock)
            {
                if (_ring.Length == 0 || _filled == 0)
                    return 0f;

                int framesToRead = (int)((long)SampleRate * windowMs / 1000L);
                if (framesToRead < 1) framesToRead = 1;

                int samplesToRead = framesToRead * Channels;
                samplesToRead = Math.Min(samplesToRead, _filled);
                if (samplesToRead <= 0) return 0f;

                int start = _writeIndex - samplesToRead;
                if (start < 0) start += _ring.Length;

                float peak = 0f;

                int idx = start;
                for (int i = 0; i < samplesToRead; i++)
                {
                    var a = Math.Abs(_ring[idx]);
                    if (a > peak) peak = a;

                    idx++;
                    if (idx >= _ring.Length) idx = 0;
                }

                if (peak < 0f) peak = 0f;
                if (peak > 1f) peak = 1f;
                return peak;
            }
        }

        public float GetPeakDbLastMs(int windowMs)
        {
            var peak = GetPeakLastMs(windowMs);
            if (peak <= 0.000001f) return -90f;

            var db = 20f * (float)Math.Log10(peak);
            if (db < -90f) db = -90f;
            if (db > 0f) db = 0f;
            return db;
        }

        /// <summary>
        /// Snapshot the most recent samples (interleaved floats).
        /// Intended for committing to WAV.
        /// </summary>
        public float[] SnapshotLastSeconds(int seconds)
        {
            if (seconds < 1) seconds = 1;

            lock (_lock)
            {
                if (_ring.Length == 0 || _filled == 0)
                    return Array.Empty<float>();

                int targetSamples = SampleRate * Channels * seconds;
                targetSamples = Math.Min(targetSamples, _filled);
                if (targetSamples <= 0)
                    return Array.Empty<float>();

                var dst = new float[targetSamples];

                int start = _writeIndex - targetSamples;
                if (start < 0) start += _ring.Length;

                int idx = start;
                for (int i = 0; i < targetSamples; i++)
                {
                    dst[i] = _ring[idx];
                    idx++;
                    if (idx >= _ring.Length) idx = 0;
                }

                return dst;
            }
        }
    }
}
