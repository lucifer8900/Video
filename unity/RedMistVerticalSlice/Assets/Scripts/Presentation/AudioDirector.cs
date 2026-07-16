using UnityEngine;

namespace Lingmai.RedMist
{
    public sealed class AudioDirector : MonoBehaviour
    {
        private AudioSource _ambience;
        private AudioSource _effects;
        private AudioClip _click;
        private AudioClip _success;
        private AudioClip _danger;
        private AudioClip _ambientClip;
        private bool _cinematicDucked;

        public void Initialize(float volume)
        {
            _ambience = gameObject.AddComponent<AudioSource>();
            _effects = gameObject.AddComponent<AudioSource>();
            _ambience.loop = true;
            _ambience.playOnAwake = false;
            _effects.playOnAwake = false;
            _click = CreateTone("Click", 660f, 0.07f, 0.18f);
            _success = CreateSweep("Success", 420f, 780f, 0.35f, 0.25f);
            _danger = CreateSweep("Danger", 180f, 90f, 0.45f, 0.35f);
            _ambientClip = CreateAmbience();
            _ambience.clip = _ambientClip;
            SetVolume(volume);
            _ambience.Play();
        }

        public void SetVolume(float volume)
        {
            AudioListener.volume = Mathf.Clamp01(volume);
            if (_ambience != null) _ambience.volume = _cinematicDucked ? 0.035f : 0.22f;
            if (_effects != null) _effects.volume = 0.8f;
        }

        public void SetCinematicDuck(bool ducked)
        {
            _cinematicDucked = ducked;
            if (_ambience != null) _ambience.volume = ducked ? 0.035f : 0.22f;
            if (_effects != null) _effects.volume = ducked ? 0.25f : 0.8f;
        }

        public void Click()
        {
            if (_effects != null) _effects.PlayOneShot(_click);
        }

        public void Success()
        {
            if (_effects != null) _effects.PlayOneShot(_success);
        }

        public void Danger()
        {
            if (_effects != null) _effects.PlayOneShot(_danger);
        }

        private static AudioClip CreateTone(string name, float frequency, float duration, float amplitude)
        {
            int sampleRate = 44100;
            int length = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[length];
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Sin(Mathf.PI * i / Mathf.Max(1, length - 1));
                samples[i] = Mathf.Sin(t * frequency * Mathf.PI * 2f) * amplitude * envelope;
            }
            AudioClip clip = AudioClip.Create(name, length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip CreateSweep(string name, float start, float end, float duration, float amplitude)
        {
            int sampleRate = 44100;
            int length = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[length];
            float phase = 0f;
            for (int i = 0; i < length; i++)
            {
                float p = i / (float)Mathf.Max(1, length - 1);
                float frequency = Mathf.Lerp(start, end, p);
                phase += frequency / sampleRate;
                float envelope = Mathf.Sin(Mathf.PI * p);
                samples[i] = Mathf.Sin(phase * Mathf.PI * 2f) * amplitude * envelope;
            }
            AudioClip clip = AudioClip.Create(name, length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static AudioClip CreateAmbience()
        {
            int sampleRate = 22050;
            int length = sampleRate * 8;
            float[] samples = new float[length];
            float filtered = 0f;
            System.Random random = new System.Random(172204);
            for (int i = 0; i < length; i++)
            {
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                filtered = Mathf.Lerp(filtered, noise, 0.012f);
                float t = i / (float)sampleRate;
                float wind = Mathf.Sin(t * 0.34f) * 0.035f + Mathf.Sin(t * 0.083f) * 0.025f;
                samples[i] = filtered * 0.11f + wind;
            }
            AudioClip clip = AudioClip.Create("MountainMistAmbience", length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
