using System;

namespace GDMENUCardManager.Core.Audio
{
    /// <summary>
    /// Yamaha AICA 4-bit ADPCM encoder. The AICA decodes this in hardware,
    /// same algorithm as ffmpeg's adpcm_yamaha/adpcm_aica.
    /// </summary>
    public class AicaAdpcmEncoder
    {
        private static readonly int[] IndexScale = new[]
        {
            230, 230, 230, 230, 307, 409, 512, 614,
            230, 230, 230, 230, 307, 409, 512, 614
        };

        private static readonly int[] DiffLookup = new[]
        {
            1, 3, 5, 7, 9, 11, 13, 15,
            -1, -3, -5, -7, -9, -11, -13, -15
        };

        private int predictor;
        private int step = 127;

        public int EncodeSample(int sample)
        {
            int delta = sample - predictor;
            int nibble = Math.Min(7, Math.Abs(delta) * 4 / step);
            if (delta < 0)
                nibble += 8;
            Update(nibble);
            return nibble;
        }

        private void Update(int nibble)
        {
            predictor += step * DiffLookup[nibble] / 8;
            if (predictor > short.MaxValue)
                predictor = short.MaxValue;
            else if (predictor < short.MinValue)
                predictor = short.MinValue;

            step = (step * IndexScale[nibble]) >> 8;
            if (step < 127)
                step = 127;
            else if (step > 24576)
                step = 24576;
        }
    }
}
