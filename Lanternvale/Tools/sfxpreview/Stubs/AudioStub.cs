// The one Unity audio type the synthesis code touches (SynthBuffer.ToClip). preview3d's stand-in has no AudioClip;
// if it ever gains one, delete this file (both tools compile ../preview3d/Stubs/UnityEngine.cs).
namespace UnityEngine
{
    public sealed class AudioClip : Object
    {
        public string Name;
        public int Samples, Channels, Frequency;
        public float[] Data;

        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream)
            => new AudioClip { Name = name, Samples = lengthSamples, Channels = channels, Frequency = frequency, Data = new float[lengthSamples * channels] };

        public bool SetData(float[] data, int offsetSamples)
        {
            System.Array.Copy(data, 0, Data, offsetSamples, System.Math.Min(data.Length, Data.Length - offsetSamples));
            return true;
        }
    }
}
