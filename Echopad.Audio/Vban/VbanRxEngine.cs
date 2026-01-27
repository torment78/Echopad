using Echopad.Core;

using NAudio.Wave;

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Echopad.Audio.Vban
{
    /// <summary>
    /// Minimal VBAN AUDIO receiver (UDP).
    /// - Listens on a UDP port
    /// - Filters by StreamName (and optionally by RemoteIp)
    /// - Decodes PCM (16/24/32 int, 32f) to float interleaved
    ///
    /// VBAN header is 28 bytes:
    /// FOURCC 'VBAN' + format_SR + format_nbs + format_nbc + format_bit + streamname[16] + nuFrame (u32)
    /// </summary>
    public sealed class VbanRxEngine : IDisposable
    {
        private readonly VbanRxSettings _cfg;
        private readonly string? _expectedRemoteIp; // optional filter
        private UdpClient? _udp;
        private CancellationTokenSource? _cts;
        private Task? _rxTask;

        public WaveFormat? WaveFormat { get; private set; }

        public event Action<float[], int>? SamplesReceived; // (samples, sampleCount)

        public VbanRxEngine(VbanRxSettings cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));

            // If user leaves it as 0.0.0.0 / empty, treat as "any"
            _expectedRemoteIp =
                string.IsNullOrWhiteSpace(cfg.RemoteIp) ? null :
                cfg.RemoteIp.Trim() == "0.0.0.0" ? null :
                cfg.RemoteIp.Trim();
        }

        public void Start()
        {
            Stop();

            _cts = new CancellationTokenSource();

            // Bind to port on all NICs
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, _cfg.Port))
            {
                // Helps reduce packet loss under burst
                Client =
                {
                    ReceiveBufferSize = 1_048_576
                }
            };

            _rxTask = Task.Run(() => RxLoop(_cts.Token));
            Debug.WriteLine($"[VBAN-RX] Started listening on UDP :{_cfg.Port} stream='{_cfg.StreamName}' ipFilter='{_expectedRemoteIp ?? "*"}'");
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _udp?.Close(); } catch { }
            try { _udp?.Dispose(); } catch { }

            _udp = null;

            try { _rxTask?.Wait(250); } catch { }
            _rxTask = null;

            try { _cts?.Dispose(); } catch { }
            _cts = null;
        }

        private async Task RxLoop(CancellationToken ct)
        {
            if (_udp == null) return;

            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult res;
                try
                {
                    res = await _udp.ReceiveAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[VBAN-RX] Receive error: " + ex);
                    await Task.Delay(25, ct).ConfigureAwait(false);
                    continue;
                }

                // Optional remote IP filter
                if (_expectedRemoteIp != null && res.RemoteEndPoint.Address.ToString() != _expectedRemoteIp)
                    continue;

                var buf = res.Buffer;
                if (buf == null || buf.Length < 28)
                    continue;

                if (!VbanPacket.TryParseHeader(buf, out var h))
                    continue;

                // Must match stream name
                if (!string.Equals(h.StreamName, _cfg.StreamName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Only AUDIO protocol, only PCM codec for now
                if (h.SubProtocol != 0x00) // VBAN_PROTOCOL_AUDIO
                    continue;

                if (h.Codec != 0x00) // VBAN_CODEC_PCM
                    continue;

                // Build wave format once (or if sender changes)
                var fmt = CreateWaveFormat(h.SampleRate, h.Channels, h.DataType);
                if (WaveFormat == null ||
                    WaveFormat.SampleRate != fmt.SampleRate ||
                    WaveFormat.Channels != fmt.Channels ||
                    WaveFormat.Encoding != fmt.Encoding ||
                    WaveFormat.BitsPerSample != fmt.BitsPerSample)
                {
                    WaveFormat = fmt;
                    Debug.WriteLine($"[VBAN-RX] Format locked: {fmt.SampleRate}Hz ch={fmt.Channels} enc={fmt.Encoding} bps={fmt.BitsPerSample}");
                }

                // Decode payload -> float interleaved
                var payloadOffset = 28;
                var payloadBytes = buf.Length - payloadOffset;
                if (payloadBytes <= 0) continue;

                var floats = VbanPacket.DecodePayloadToFloat(buf, payloadOffset, payloadBytes, h.DataType);
                if (floats.Length == 0) continue;

                SamplesReceived?.Invoke(floats, floats.Length);
            }

            Debug.WriteLine("[VBAN-RX] RxLoop ended.");
        }

        private static WaveFormat CreateWaveFormat(int sampleRate, int channels, int dataType)
        {
            // For RollingAudioBuffer, the Encoding isn't super critical, but keep it accurate.
            // We decode to float anyway, but WaveFormat metadata is used elsewhere.
            return dataType switch
            {
                0x04 => WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels), // FLOAT32
                0x05 => WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels), // FLOAT64 (we'll decode to float32)
                0x01 => new WaveFormat(sampleRate, 16, channels), // INT16
                0x02 => new WaveFormat(sampleRate, 24, channels), // INT24
                0x03 => new WaveFormat(sampleRate, 32, channels), // INT32
                _ => WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels),
            };
        }

        public void Dispose() => Stop();
    }

    internal static class VbanPacket
    {
        // From official spec SRList (indices 0..20) :contentReference[oaicite:1]{index=1}
        private static readonly int[] SRList = new[]
        {
            6000, 12000, 24000, 48000, 96000, 192000, 384000,
            8000, 16000, 32000, 64000, 128000, 256000, 512000,
            11025, 22050, 44100, 88200, 176400, 352800, 705600
        };

        public sealed class Header
        {
            public int SampleRate { get; init; }
            public int Channels { get; init; }
            public int SamplesPerFrame { get; init; }
            public int DataType { get; init; }
            public int Codec { get; init; }
            public int SubProtocol { get; init; }
            public string StreamName { get; init; } = "";
            public uint FrameCounter { get; init; }
        }

        public static bool TryParseHeader(byte[] buf, out Header header)
        {
            header = default!;

            // FOURCC "VBAN"
            if (buf[0] != (byte)'V' || buf[1] != (byte)'B' || buf[2] != (byte)'A' || buf[3] != (byte)'N')
                return false;

            var formatSR = buf[4];
            var srIndex = formatSR & 0x1F;           // 5 bits
            var subProto = formatSR & 0xE0;          // 3 bits

            if (srIndex >= SRList.Length)
                return false;

            var nbs = buf[5] + 1;                   // 0..255 means 1..256 :contentReference[oaicite:2]{index=2}
            var nbc = buf[6] + 1;                   // 0..255 means 1..256 :contentReference[oaicite:3]{index=3}

            var formatBit = buf[7];
            var dataType = formatBit & 0x07;        // lower 3 bits :contentReference[oaicite:4]{index=4}
            var codec = formatBit & 0xF0;           // upper nibble codec :contentReference[oaicite:5]{index=5}

            // stream name 16 bytes ASCII
            var name = Encoding.ASCII.GetString(buf, 8, 16).TrimEnd('\0', ' ');

            // frame counter little endian
            uint frame = (uint)(
                buf[24] |
                (buf[25] << 8) |
                (buf[26] << 16) |
                (buf[27] << 24));

            header = new Header
            {
                SampleRate = SRList[srIndex],
                Channels = nbc,
                SamplesPerFrame = nbs,
                DataType = dataType,
                Codec = codec,
                SubProtocol = subProto,
                StreamName = name,
                FrameCounter = frame
            };

            return true;
        }

        public static float[] DecodePayloadToFloat(byte[] buf, int offset, int bytes, int dataType)
        {
            if (bytes <= 0) return Array.Empty<float>();

            // Data types per spec :contentReference[oaicite:6]{index=6}
            return dataType switch
            {
                0x04 => DecodeFloat32(buf, offset, bytes), // FLOAT32
                0x01 => DecodeInt16(buf, offset, bytes),   // INT16
                0x02 => DecodeInt24(buf, offset, bytes),   // INT24
                0x03 => DecodeInt32(buf, offset, bytes),   // INT32
                0x05 => DecodeFloat64ToFloat32(buf, offset, bytes), // FLOAT64
                _ => Array.Empty<float>()
            };
        }

        private static float[] DecodeFloat32(byte[] buf, int offset, int bytes)
        {
            var samples = bytes / 4;
            if (samples <= 0) return Array.Empty<float>();

            var floats = new float[samples];
            Buffer.BlockCopy(buf, offset, floats, 0, samples * 4);
            return floats;
        }

        private static float[] DecodeInt16(byte[] buf, int offset, int bytes)
        {
            var samples = bytes / 2;
            if (samples <= 0) return Array.Empty<float>();

            var floats = new float[samples];
            int o = offset;
            for (int i = 0; i < samples; i++)
            {
                short s = (short)(buf[o] | (buf[o + 1] << 8));
                floats[i] = s / 32768f;
                o += 2;
            }
            return floats;
        }

        private static float[] DecodeInt24(byte[] buf, int offset, int bytes)
        {
            var samples = bytes / 3;
            if (samples <= 0) return Array.Empty<float>();

            var floats = new float[samples];
            int o = offset;
            for (int i = 0; i < samples; i++)
            {
                int v = (buf[o] | (buf[o + 1] << 8) | (buf[o + 2] << 16));
                if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000); // sign extend
                floats[i] = v / 8388608f; // 2^23
                o += 3;
            }
            return floats;
        }

        private static float[] DecodeInt32(byte[] buf, int offset, int bytes)
        {
            var samples = bytes / 4;
            if (samples <= 0) return Array.Empty<float>();

            var floats = new float[samples];
            int o = offset;
            for (int i = 0; i < samples; i++)
            {
                int v = BitConverter.ToInt32(buf, o);
                floats[i] = v / 2147483648f;
                o += 4;
            }
            return floats;
        }

        private static float[] DecodeFloat64ToFloat32(byte[] buf, int offset, int bytes)
        {
            var samples = bytes / 8;
            if (samples <= 0) return Array.Empty<float>();

            var floats = new float[samples];
            int o = offset;
            for (int i = 0; i < samples; i++)
            {
                double d = BitConverter.ToDouble(buf, o);
                floats[i] = (float)Math.Clamp(d, -1.0, 1.0);
                o += 8;
            }
            return floats;
        }
    }
}
