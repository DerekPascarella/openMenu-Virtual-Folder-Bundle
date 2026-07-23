using System;
using System.IO;
using System.Threading.Tasks;

namespace GDMENUCardManager.Core.Audio
{
    public class BgmConversionResult
    {
        public TimeSpan Duration { get; set; }
        public long FileSize { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
    }

    /// <summary>
    /// Converts a WAV/MP3/OGG/FLAC file to the BGM.ADP format openMenu streams at boot.
    /// The file is a 32 byte OMBG header followed by AICA ADPCM data. Stereo packs one
    /// frame per byte with the left sample in the high nibble, matching what the KOS
    /// snd_adpcm_split routine expects. Mono packs two samples per byte, low nibble first.
    /// </summary>
    public static class BgmConverter
    {
        public const int TargetSampleRate = 44100;
        private const uint HeaderVersion = 1;
        private const int MaxDurationSeconds = 3600;

        public static Task<BgmConversionResult> ConvertAsync(string sourcePath, string destPath)
        {
            return Task.Run(() => Convert(sourcePath, destPath));
        }

        public static BgmConversionResult Convert(string sourcePath, string destPath)
        {
            var decoded = AudioDecoder.Decode(sourcePath);
            if (decoded.FrameCount == 0)
                throw new InvalidDataException("The selected file contains no audio.");
            if (decoded.FrameCount > (long)decoded.SampleRate * MaxDurationSeconds)
                throw new InvalidDataException("The selected track is longer than an hour, which is too long for menu music.");

            var samples = decoded.Samples;
            int channels = decoded.Channels;

            if (channels > 2)
            {
                samples = TakeFrontPair(samples, channels);
                channels = 2;
            }

            samples = Resampler.Resample(samples, channels, decoded.SampleRate, TargetSampleRate);

            int frames = samples.Length / channels;
            if (frames == 0)
                throw new InvalidDataException("The selected file contains no audio.");

            byte[] payload = channels == 2
                ? EncodeStereo(samples, frames)
                : EncodeMono(samples, frames);

            var tempPath = destPath + ".tmp";
            try
            {
                using (var fs = File.Create(tempPath))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(new[] { (byte)'O', (byte)'M', (byte)'B', (byte)'G' });
                    bw.Write(HeaderVersion);
                    bw.Write((uint)TargetSampleRate);
                    bw.Write((ushort)channels);
                    bw.Write((ushort)0);
                    bw.Write(0u);
                    bw.Write((uint)frames);
                    bw.Write(0ul);
                    bw.Write(payload);
                }
                File.Move(tempPath, destPath, overwrite: true);
            }
            catch
            {
                // a leftover tmp inside menu_data would get baked into the menu image
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }

            return new BgmConversionResult
            {
                Duration = TimeSpan.FromSeconds((double)frames / TargetSampleRate),
                FileSize = 32 + payload.Length,
                SampleRate = TargetSampleRate,
                Channels = channels
            };
        }

        private static byte[] EncodeStereo(float[] interleaved, int frames)
        {
            int padded = Pad32(frames);
            var payload = new byte[padded];
            var left = new AicaAdpcmEncoder();
            var right = new AicaAdpcmEncoder();

            for (int i = 0; i < frames; i++)
            {
                int ln = left.EncodeSample(ToPcm16(interleaved[i * 2]));
                int rn = right.EncodeSample(ToPcm16(interleaved[i * 2 + 1]));
                payload[i] = (byte)((ln << 4) | rn);
            }
            for (int i = frames; i < padded; i++)
            {
                int ln = left.EncodeSample(0);
                int rn = right.EncodeSample(0);
                payload[i] = (byte)((ln << 4) | rn);
            }
            return payload;
        }

        private static byte[] EncodeMono(float[] samples, int frames)
        {
            // two samples per byte, so pad the sample count to a 64 sample boundary
            int paddedFrames = Pad32((frames + 1) / 2) * 2;
            var payload = new byte[paddedFrames / 2];
            var enc = new AicaAdpcmEncoder();

            for (int i = 0; i < paddedFrames; i++)
            {
                int nibble = enc.EncodeSample(i < frames ? ToPcm16(samples[i]) : 0);
                if ((i & 1) == 0)
                    payload[i / 2] = (byte)nibble;
                else
                    payload[i / 2] |= (byte)(nibble << 4);
            }
            return payload;
        }

        private static int Pad32(int byteCount)
        {
            return (byteCount + 31) & ~31;
        }

        private static int ToPcm16(float sample)
        {
            int v = (int)Math.Round(sample * 32768f);
            if (v > short.MaxValue)
                v = short.MaxValue;
            else if (v < short.MinValue)
                v = short.MinValue;
            return v;
        }

        private static float[] TakeFrontPair(float[] interleaved, int channels)
        {
            int frames = interleaved.Length / channels;
            var stereo = new float[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                stereo[i * 2] = interleaved[i * channels];
                stereo[i * 2 + 1] = interleaved[i * channels + 1];
            }
            return stereo;
        }
    }
}
