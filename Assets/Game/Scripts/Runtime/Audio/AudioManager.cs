using System;
using UnityEngine;

namespace PocketOrbits.Audio
{
    public enum Sfx { Click, Select, Place, Invalid, Launch, Capture, Waypoint, Fail, Complete, Star, Hint, Undo }

    /// <summary>
    /// Music, sound effects and haptics. All clips are synthesised at startup (no audio assets needed),
    /// volumes come from the saved settings, and haptics are optional.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        const int Rate = 44100;
        AudioSource _music;
        AudioSource[] _sfx;
        int _next;
        AudioClip[] _clips;
        float _musicVolume = 0.6f, _sfxVolume = 0.8f;
        bool _haptics = true;

        public void Init()
        {
            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;
            _music.spatialBlend = 0;
            _sfx = new AudioSource[6];
            for (int i = 0; i < _sfx.Length; i++)
            {
                _sfx[i] = gameObject.AddComponent<AudioSource>();
                _sfx[i].playOnAwake = false;
                _sfx[i].spatialBlend = 0;
            }
            var names = Enum.GetValues(typeof(Sfx));
            _clips = new AudioClip[names.Length];
            _clips[(int)Sfx.Click] = Tone("click", 0.05f, 880, 880, 0.25f, Wave.Sine);
            _clips[(int)Sfx.Select] = Tone("select", 0.09f, 520, 780, 0.3f, Wave.Sine);
            _clips[(int)Sfx.Place] = Chord("place", 0.25f, new[] { 523.3f, 784f }, 0.25f);
            _clips[(int)Sfx.Invalid] = Tone("invalid", 0.16f, 220, 180, 0.3f, Wave.Square);
            _clips[(int)Sfx.Launch] = Noise("launch", 0.5f, 0.25f);
            _clips[(int)Sfx.Capture] = Arpeggio("capture", new[] { 659.3f, 784f, 987.8f, 1318.5f }, 0.09f, 0.3f);
            _clips[(int)Sfx.Waypoint] = Chord("waypoint", 0.3f, new[] { 880f, 1318.5f }, 0.22f);
            _clips[(int)Sfx.Fail] = Tone("fail", 0.45f, 196, 110, 0.35f, Wave.Triangle);
            _clips[(int)Sfx.Complete] = Arpeggio("complete", new[] { 523.3f, 659.3f, 784f, 1046.5f, 1318.5f }, 0.12f, 0.3f);
            _clips[(int)Sfx.Star] = Tone("star", 0.18f, 1318.5f, 1568f, 0.25f, Wave.Sine);
            _clips[(int)Sfx.Hint] = Chord("hint", 0.35f, new[] { 587.3f, 740f, 880f }, 0.18f);
            _clips[(int)Sfx.Undo] = Tone("undo", 0.12f, 700, 450, 0.25f, Wave.Sine);
            _music.clip = AmbientLoop();
        }

        public void Apply(float music, float sfx, bool haptics)
        {
            _musicVolume = music;
            _sfxVolume = sfx;
            _haptics = haptics;
            if (_music == null) return;
            _music.volume = 0.35f * music;
            if (music > 0.001f && !_music.isPlaying) _music.Play();
            else if (music <= 0.001f && _music.isPlaying) _music.Stop();
        }

        public void Play(Sfx s, float pitch = 1f)
        {
            if (_sfx == null || _sfxVolume <= 0.001f) return;
            var src = _sfx[_next];
            _next = (_next + 1) % _sfx.Length;
            src.pitch = pitch;
            src.PlayOneShot(_clips[(int)s], _sfxVolume);
        }

        /// <summary>Short haptic tick where supported. Android uses a 20-40 ms vibration via the Vibrator service.</summary>
        public void Haptic(bool strong = false)
        {
            if (!_haptics) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator"))
                {
                    vibrator?.Call("vibrate", (long)(strong ? 40 : 18));
                }
            }
            catch (Exception)
            {
                Handheld.Vibrate();
            }
#elif UNITY_IOS && !UNITY_EDITOR
            if (strong) Handheld.Vibrate();
#endif
        }

        void OnApplicationPause(bool paused)
        {
            if (_music == null) return;
            if (paused) _music.Pause();
            else if (_musicVolume > 0.001f) _music.UnPause();
        }

        // ---- Synthesis ------------------------------------------------------------------------

        enum Wave { Sine, Square, Triangle }

        static float Osc(Wave w, float phase)
        {
            switch (w)
            {
                case Wave.Square: return Mathf.Sin(phase) >= 0 ? 0.5f : -0.5f;
                case Wave.Triangle: return Mathf.PingPong(phase / Mathf.PI, 2f) - 1f;
                default: return Mathf.Sin(phase);
            }
        }

        static float Envelope(float t, float duration)
        {
            float attack = Mathf.Min(0.01f, duration * 0.2f);
            if (t < attack) return t / attack;
            return Mathf.Exp(-4f * (t - attack) / duration);
        }

        static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip Tone(string name, float duration, float f0, float f1, float gain, Wave wave)
        {
            int n = (int)(duration * Rate);
            var data = new float[n];
            float phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(f0, f1, t / duration);
                phase += 2 * Mathf.PI * f / Rate;
                data[i] = Osc(wave, phase) * Envelope(t, duration) * gain;
            }
            return Make(name, data);
        }

        static AudioClip Chord(string name, float duration, float[] freqs, float gain)
        {
            int n = (int)(duration * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float v = 0;
                foreach (var f in freqs) v += Mathf.Sin(2 * Mathf.PI * f * t) + 0.3f * Mathf.Sin(4 * Mathf.PI * f * t);
                data[i] = v / freqs.Length * Envelope(t, duration) * gain;
            }
            return Make(name, data);
        }

        static AudioClip Arpeggio(string name, float[] notes, float step, float gain)
        {
            float duration = notes.Length * step + 0.5f;
            int n = (int)(duration * Rate);
            var data = new float[n];
            for (int k = 0; k < notes.Length; k++)
            {
                int start = (int)(k * step * Rate);
                for (int i = start; i < n; i++)
                {
                    float t = (i - start) / (float)Rate;
                    data[i] += Mathf.Sin(2 * Mathf.PI * notes[k] * t) * Envelope(t, 0.6f) * gain * 0.6f;
                }
            }
            return Make(name, data);
        }

        static AudioClip Noise(string name, float duration, float gain)
        {
            int n = (int)(duration * Rate);
            var data = new float[n];
            var rng = new System.Random(3);
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                // Rising filtered noise "whoosh".
                float cutoff = Mathf.Lerp(0.02f, 0.25f, t / duration);
                lp += ((float)rng.NextDouble() * 2 - 1 - lp) * cutoff;
                float env = Mathf.Sin(Mathf.PI * t / duration);
                data[i] = lp * env * gain * 2f;
            }
            return Make(name, data);
        }

        /// <summary>Soft 16 second pad loop (I-vi-IV-V in C), generated once.</summary>
        static AudioClip AmbientLoop()
        {
            const float seconds = 16f;
            int n = (int)(seconds * Rate);
            var data = new float[n];
            float[][] chords =
            {
                new[] { 130.8f, 196f, 261.6f, 329.6f },
                new[] { 110f, 164.8f, 220f, 261.6f },
                new[] { 87.3f, 130.8f, 174.6f, 220f },
                new[] { 98f, 146.8f, 196f, 246.9f },
            };
            float chordLen = seconds / chords.Length;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                int c = Mathf.Min(chords.Length - 1, (int)(t / chordLen));
                float local = t - c * chordLen;
                // Crossfade at chord edges so the loop is seamless.
                float fade = Mathf.Clamp01(local / 1.5f) * Mathf.Clamp01((chordLen - local) / 1.5f);
                float v = 0;
                foreach (var f in chords[c]) v += Mathf.Sin(2 * Mathf.PI * f * t) * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 0.2f * t + f));
                data[i] = v * 0.06f * (0.35f + 0.65f * fade);
            }
            return Make("ambient", data);
        }
    }
}
