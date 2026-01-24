using NAudio.Wave;
using System;
using System.IO;

namespace Echopad.Audio
{
    public sealed class RollingBufferCommitService
    {
        public sealed class CommitResult
        {
            public string FilePath { get; }
            public TimeSpan Duration { get; }

            public CommitResult(string filePath, TimeSpan duration)
            {
                FilePath = filePath;
                Duration = duration;
            }
        }

        public CommitResult CommitToWav(RollingAudioBuffer buffer, string outputDirectory, string fileNameNoExt)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            Directory.CreateDirectory(outputDirectory);

            var fullPath = Path.Combine(outputDirectory, fileNameNoExt + ".wav");

            var samples = buffer.ReadAll();
            if (samples == null || samples.Length == 0)
                throw new InvalidOperationException("Rolling buffer is empty.");

            // --- Write as 16-bit PCM for maximum compatibility ---
            var wf = new WaveFormat(buffer.SampleRate, 16, buffer.Channels);

            // Convert float [-1..1] -> int16
            var pcm = new byte[samples.Length * 2]; // 2 bytes per sample
            int o = 0;

            for (int i = 0; i < samples.Length; i++)
            {
                // clamp
                float f = samples[i];
                if (f > 1f) f = 1f;
                else if (f < -1f) f = -1f;

                short s = (short)Math.Round(f * short.MaxValue);

                pcm[o++] = (byte)(s & 0xFF);
                pcm[o++] = (byte)((s >> 8) & 0xFF);
            }

            using (var writer = new WaveFileWriter(fullPath, wf))
            {
                writer.Write(pcm, 0, pcm.Length);
                writer.Flush();
            }

            var seconds = (double)samples.Length / (buffer.SampleRate * buffer.Channels);
            var dur = TimeSpan.FromSeconds(Math.Max(0, seconds));

            return new CommitResult(fullPath, dur);
        }
    }
}
