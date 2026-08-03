using System;

namespace GDMENUCardManager.Core.Audio
{
    /// <summary>
    /// Windowed sinc sample rate converter. Offline quality, not realtime.
    /// </summary>
    public static class Resampler
    {
        private const int TapsPerSide = 16;

        public static float[] Resample(float[] interleaved, int channels, int srcRate, int dstRate)
        {
            if (srcRate == dstRate)
                return interleaved;

            int srcFrames = interleaved.Length / channels;
            long dstFrames = (long)srcFrames * dstRate / srcRate;
            var output = new float[dstFrames * channels];

            double ratio = (double)srcRate / dstRate;
            // When downsampling the kernel widens and the cutoff drops below source Nyquist.
            double cutoff = Math.Min(1.0, (double)dstRate / srcRate) * 0.95;
            int taps = (int)Math.Ceiling(TapsPerSide * Math.Max(1.0, ratio));

            for (long n = 0; n < dstFrames; n++)
            {
                double center = n * ratio;
                int first = (int)Math.Floor(center) - taps + 1;
                int last = (int)Math.Floor(center) + taps;

                for (int ch = 0; ch < channels; ch++)
                {
                    double sum = 0;
                    double weightSum = 0;
                    for (int i = first; i <= last; i++)
                    {
                        double x = center - i;
                        double w = Sinc(x * cutoff) * cutoff * Blackman(x / taps);
                        weightSum += w;
                        int idx = i;
                        if (idx < 0)
                            idx = 0;
                        else if (idx >= srcFrames)
                            idx = srcFrames - 1;
                        sum += interleaved[idx * channels + ch] * w;
                    }
                    // Normalizing keeps the level flat even with the clamped edges.
                    output[n * channels + ch] = (float)(weightSum != 0 ? sum / weightSum : 0);
                }
            }

            return output;
        }

        private static double Sinc(double x)
        {
            if (Math.Abs(x) < 1e-12)
                return 1.0;
            double px = Math.PI * x;
            return Math.Sin(px) / px;
        }

        private static double Blackman(double x)
        {
            if (x < -1 || x > 1)
                return 0;
            double t = Math.PI * (x + 1);
            return 0.42 - 0.5 * Math.Cos(t) + 0.08 * Math.Cos(2 * t);
        }
    }
}
