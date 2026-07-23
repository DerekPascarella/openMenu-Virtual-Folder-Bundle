using System;
using System.Collections.Generic;
using System.IO;

namespace GDMENUCardManager.Core.Audio
{
    /// <summary>
    /// Decodes WAV, MP3, OGG and FLAC files to interleaved float PCM.
    /// </summary>
    public static class AudioDecoder
    {
        public static readonly string[] SupportedExtensions = new[] { ".wav", ".mp3", ".ogg", ".flac" };

        public static bool IsSupported(string path)
        {
            var ext = Path.GetExtension(path);
            foreach (var s in SupportedExtensions)
                if (string.Equals(ext, s, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        public static DecodedAudio Decode(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".wav":
                    return DecodeWav(path);
                case ".mp3":
                    return DecodeMp3(path);
                case ".ogg":
                    return DecodeOgg(path);
                case ".flac":
                    return DecodeFlac(path);
                default:
                    throw new NotSupportedException($"Unsupported audio format: {ext}");
            }
        }

        private static DecodedAudio DecodeMp3(string path)
        {
            using (var mpeg = new NLayer.MpegFile(path))
            {
                var chunks = new List<float[]>();
                long total = 0;
                var buffer = new float[mpeg.SampleRate * mpeg.Channels];
                int read;
                while ((read = mpeg.ReadSamples(buffer, 0, buffer.Length)) > 0)
                {
                    var chunk = new float[read];
                    Array.Copy(buffer, chunk, read);
                    chunks.Add(chunk);
                    total += read;
                }
                return new DecodedAudio(Combine(chunks, total), mpeg.SampleRate, mpeg.Channels);
            }
        }

        private static DecodedAudio DecodeOgg(string path)
        {
            using (var vorbis = new NVorbis.VorbisReader(path))
            {
                var chunks = new List<float[]>();
                long total = 0;
                var buffer = new float[vorbis.SampleRate * vorbis.Channels];
                int read;
                while ((read = vorbis.ReadSamples(buffer, 0, buffer.Length)) > 0)
                {
                    var chunk = new float[read];
                    Array.Copy(buffer, chunk, read);
                    chunks.Add(chunk);
                    total += read;
                }
                return new DecodedAudio(Combine(chunks, total), vorbis.SampleRate, vorbis.Channels);
            }
        }

        private static DecodedAudio DecodeFlac(string path)
        {
            var reader = new CUETools.Codecs.FLAKE.FlakeReader(path, null);
            try
            {
                int channels = reader.PCM.ChannelCount;
                int bits = reader.PCM.BitsPerSample;
                float scale = (float)(1.0 / (1L << (bits - 1)));

                var chunks = new List<float[]>();
                long total = 0;
                var buff = new CUETools.Codecs.AudioBuffer(reader, 65536);
                while (reader.Read(buff, -1) > 0)
                {
                    var chunk = new float[buff.Length * channels];
                    int pos = 0;
                    for (int i = 0; i < buff.Length; i++)
                        for (int ch = 0; ch < channels; ch++)
                            chunk[pos++] = buff.Samples[i, ch] * scale;
                    chunks.Add(chunk);
                    total += chunk.Length;
                }
                return new DecodedAudio(Combine(chunks, total), reader.PCM.SampleRate, channels);
            }
            finally
            {
                reader.Close();
            }
        }

        private static DecodedAudio DecodeWav(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                if (br.ReadUInt32() != 0x46464952)
                    throw new InvalidDataException("Not a RIFF file.");
                br.ReadUInt32();
                if (br.ReadUInt32() != 0x45564157)
                    throw new InvalidDataException("Not a WAVE file.");

                ushort formatTag = 0, channels = 0, bits = 0;
                int sampleRate = 0;
                byte[] data = null;

                while (fs.Position + 8 <= fs.Length)
                {
                    uint chunkId = br.ReadUInt32();
                    uint chunkSize = br.ReadUInt32();
                    long next = fs.Position + chunkSize + (chunkSize % 2);

                    if (chunkId == 0x20746D66)
                    {
                        if (chunkSize < 16)
                            throw new InvalidDataException("Invalid fmt chunk in WAV file.");
                        formatTag = br.ReadUInt16();
                        channels = br.ReadUInt16();
                        sampleRate = br.ReadInt32();
                        br.ReadInt32();
                        br.ReadUInt16();
                        bits = br.ReadUInt16();
                        if (formatTag == 0xFFFE && chunkSize >= 40)
                        {
                            br.ReadUInt16();
                            br.ReadUInt16();
                            br.ReadUInt32();
                            formatTag = br.ReadUInt16();
                        }
                    }
                    else if (chunkId == 0x61746164)
                    {
                        if (chunkSize > int.MaxValue)
                            throw new InvalidDataException("WAV file is too large to convert.");
                        data = br.ReadBytes((int)chunkSize);
                    }

                    if (next > fs.Length)
                        break;
                    fs.Position = next;
                }

                if (data == null || channels == 0 || sampleRate == 0)
                    throw new InvalidDataException("Missing fmt or data chunk in WAV file.");
                if (formatTag != 1 && formatTag != 3)
                    throw new NotSupportedException("Only PCM and float WAV files are supported.");

                var samples = ConvertWavData(data, formatTag, bits);
                return new DecodedAudio(samples, sampleRate, channels);
            }
        }

        private static float[] ConvertWavData(byte[] data, ushort formatTag, int bits)
        {
            if (formatTag == 3)
            {
                if (bits == 32)
                {
                    var f = new float[data.Length / 4];
                    Buffer.BlockCopy(data, 0, f, 0, f.Length * 4);
                    return f;
                }
                if (bits == 64)
                {
                    var f = new float[data.Length / 8];
                    for (int i = 0; i < f.Length; i++)
                        f[i] = (float)BitConverter.ToDouble(data, i * 8);
                    return f;
                }
                throw new NotSupportedException($"Unsupported float WAV bit depth: {bits}");
            }

            switch (bits)
            {
                case 8:
                    {
                        var f = new float[data.Length];
                        for (int i = 0; i < f.Length; i++)
                            f[i] = (data[i] - 128) / 128f;
                        return f;
                    }
                case 16:
                    {
                        var f = new float[data.Length / 2];
                        for (int i = 0; i < f.Length; i++)
                            f[i] = BitConverter.ToInt16(data, i * 2) / 32768f;
                        return f;
                    }
                case 24:
                    {
                        var f = new float[data.Length / 3];
                        for (int i = 0; i < f.Length; i++)
                        {
                            int v = data[i * 3] | (data[i * 3 + 1] << 8) | ((sbyte)data[i * 3 + 2] << 16);
                            f[i] = v / 8388608f;
                        }
                        return f;
                    }
                case 32:
                    {
                        var f = new float[data.Length / 4];
                        for (int i = 0; i < f.Length; i++)
                            f[i] = BitConverter.ToInt32(data, i * 4) / 2147483648f;
                        return f;
                    }
                default:
                    throw new NotSupportedException($"Unsupported WAV bit depth: {bits}");
            }
        }

        private static float[] Combine(List<float[]> chunks, long total)
        {
            var all = new float[total];
            int offset = 0;
            foreach (var c in chunks)
            {
                Array.Copy(c, 0, all, offset, c.Length);
                offset += c.Length;
            }
            return all;
        }
    }
}
