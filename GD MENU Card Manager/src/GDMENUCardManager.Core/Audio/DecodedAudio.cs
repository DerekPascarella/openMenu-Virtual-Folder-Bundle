namespace GDMENUCardManager.Core.Audio
{
    /// <summary>
    /// Interleaved float PCM in the -1..1 range, straight from a decoder.
    /// </summary>
    public class DecodedAudio
    {
        public float[] Samples { get; }
        public int SampleRate { get; }
        public int Channels { get; }

        public int FrameCount => Channels == 0 ? 0 : Samples.Length / Channels;

        public DecodedAudio(float[] samples, int sampleRate, int channels)
        {
            Samples = samples;
            SampleRate = sampleRate;
            Channels = channels;
        }
    }
}
