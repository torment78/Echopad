using Echopad.Core;

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Echopad.Audio.Vban
{
    /// <summary>
    /// Minimal VBAN AUDIO transmitter (UDP).
    /// - Sends PCM (Float32 by default) in VBAN frames.
    /// - Builds 28-byte VBAN header + payload.
    /// </summary>
    public sealed class VbanTxEngine : IDisposable
    {
        private readonly VbanTxSettings _cfg;
        private readonly UdpClient _udp;
        private readonly IPEndPoint _remote;

        private uint _frameCounter;

        public VbanTxEngine(VbanTxSettings cfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));

            _udp = new UdpClient();
            _udp.Client.SendBufferSize = 1_048_576;

            _remote = new IPEndPoint(IPAddress.Parse(_cfg.RemoteIp), _cfg.Port);

            Debug.WriteLine($"[VBAN-TX] Ready -> {_cfg.RemoteIp}:{_cfg.Port} stream='{_cfg.StreamName}' sr={_cfg.SampleRate} ch={_cfg.Channels} float32={_cfg.Float32} frameSamples={_cfg.FrameSamples}");
        }

        public void SendInterleavedFloat32Frame(float[] interleaved, int sampleCount, int sampleRate, int channels)
        {
            if (sampleCount <= 0) return;

            // We only support float32 payload in v1 (stable + simple)
            // Caller ensures float samples are in -1..+1
            int bytes = sampleCount * 4;

            // VBAN AUDIO header is 28 bytes
            var packet = new byte[28 + bytes];

            WriteHeader(
                packet,
                sampleRate: sampleRate,
                channels: channels,
                samplesPerChannel: sampleCount / Math.Max(1, channels),
                dataType: 0x04,   // FLOAT32
                codec: 0x00,      // PCM
                streamName: _cfg.StreamName,
                frameCounter: _frameCounter++
            );

            // payload
            System.Buffer.BlockCopy(interleaved, 0, packet, 28, bytes);

            _udp.Send(packet, packet.Length, _remote);
        }

        private static void WriteHeader(
            byte[] packet,
            int sampleRate,
            int channels,
            int samplesPerChannel,
            int dataType,
            int codec,
            string streamName,
            uint frameCounter)
        {
            // "VBAN"
            packet[0] = (byte)'V';
            packet[1] = (byte)'B';
            packet[2] = (byte)'A';
            packet[3] = (byte)'N';

            // format_SR: subprotocol(3 bits) + SR index(5 bits)
            // subprotocol AUDIO = 0x00 (top 3 bits = 000)
            byte srIndex = GetSampleRateIndex(sampleRate);
            packet[4] = (byte)(0x00 | (srIndex & 0x1F));

            // format_nbs: samples per channel minus 1 (1..256 supported)
            int nbs = Math.Clamp(samplesPerChannel, 1, 256);
            packet[5] = (byte)(nbs - 1);

            // format_nbc: channels minus 1
            int nbc = Math.Clamp(channels, 1, 256);
            packet[6] = (byte)(nbc - 1);

            // format_bit: codec (upper nibble) + datatype (lower 3 bits)
            // codec PCM = 0x00
            packet[7] = (byte)((codec & 0xF0) | (dataType & 0x07));

            // stream name 16 bytes ASCII, null padded
            var nameBytes = Encoding.ASCII.GetBytes(streamName ?? "");
            int n = Math.Min(16, nameBytes.Length);
            Array.Copy(nameBytes, 0, packet, 8, n);
            for (int i = 8 + n; i < 24; i++) packet[i] = 0;

            // frame counter little endian uint32
            packet[24] = (byte)(frameCounter & 0xFF);
            packet[25] = (byte)((frameCounter >> 8) & 0xFF);
            packet[26] = (byte)((frameCounter >> 16) & 0xFF);
            packet[27] = (byte)((frameCounter >> 24) & 0xFF);
        }

        private static byte GetSampleRateIndex(int sampleRate)
        {
            // VBAN official SR list (indices 0..20)
            // 6000,12000,24000,48000,96000,192000,384000,
            // 8000,16000,32000,64000,128000,256000,512000,
            // 11025,22050,44100,88200,176400,352800,705600
            return sampleRate switch
            {
                6000 => 0,
                12000 => 1,
                24000 => 2,
                48000 => 3,
                96000 => 4,
                192000 => 5,
                384000 => 6,

                8000 => 7,
                16000 => 8,
                32000 => 9,
                64000 => 10,
                128000 => 11,
                256000 => 12,
                512000 => 13,

                11025 => 14,
                22050 => 15,
                44100 => 16,
                88200 => 17,
                176400 => 18,
                352800 => 19,
                705600 => 20,

                _ => 3 // default to 48000
            };
        }

        public void Dispose()
        {
            try { _udp.Close(); } catch { }
            try { _udp.Dispose(); } catch { }
        }
    }
}
