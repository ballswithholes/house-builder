using System.Collections.Generic;
using UnityEngine;

namespace Sanguo
{
    // 程序合成的音效与背景音乐（无需音频文件）
    public class Sfx : MonoBehaviour
    {
        static Sfx inst;
        AudioSource sfx, music;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        string musicKind;
        const int Rate = 22050;
        public static bool MusicOn = true, SoundOn = true;

        public static void Init()
        {
            if (inst != null) return;
            var go = new GameObject("Audio");
            DontDestroyOnLoad(go);
            inst = go.AddComponent<Sfx>();
            inst.sfx = go.AddComponent<AudioSource>();
            inst.music = go.AddComponent<AudioSource>();
            inst.music.loop = true; inst.music.volume = 0.32f;
        }

        public static void Click() { Play("click", 0.5f); }
        public static void Play(string name, float vol = 0.8f)
        {
            if (inst == null || !SoundOn) return;
            AudioClip c;
            if (!inst.clips.TryGetValue(name, out c)) { c = inst.Make(name); inst.clips[name] = c; }
            if (c != null) inst.sfx.PlayOneShot(c, vol);
        }
        public static void Music(string kind)
        {
            if (inst == null || inst.musicKind == kind) return;
            inst.musicKind = kind;
            AudioClip c;
            string key = "music_" + kind;
            if (!inst.clips.TryGetValue(key, out c)) { c = inst.MakeMusic(kind); inst.clips[key] = c; }
            inst.music.clip = c;
            if (MusicOn) inst.music.Play();
        }
        public static void SetMusic(bool on) { MusicOn = on; if (inst == null) return; if (on) inst.music.Play(); else inst.music.Stop(); }

        // ---------------------------------------------------------- 合成 --
        AudioClip Clip(string name, float[] data)
        {
            var c = AudioClip.Create(name, data.Length, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }
        static float Env(float t, float a, float d) { return t < a ? t / a : Mathf.Exp(-(t - a) / d); }

        AudioClip Make(string name)
        {
            var rnd = new System.Random(name.GetHashCode());
            float dur = 0.3f;
            switch (name)
            {
                case "click": dur = 0.06f; break;
                case "hit": dur = 0.35f; break;
                case "fire": dur = 1.0f; break;
                case "rock": dur = 0.8f; break;
                case "horn": dur = 1.6f; break;
                case "win": dur = 1.8f; break;
                case "lose": dur = 1.6f; break;
                case "coin": dur = 0.35f; break;
                case "duel": dur = 0.25f; break;
                case "magic": dur = 0.7f; break;
                case "march": dur = 0.9f; break;
            }
            int n = (int)(dur * Rate);
            var d = new float[n];
            float lp = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rnd.NextDouble() * 2 - 1);
                float s = 0;
                switch (name)
                {
                    case "click": s = Mathf.Sin(t * 2 * Mathf.PI * 1400) * Env(t, 0.002f, 0.015f) * 0.5f; break;
                    case "hit": lp += (noise - lp) * 0.25f; s = (lp * 1.4f + Mathf.Sin(t * 2 * Mathf.PI * (140 - t * 200)) * 0.6f) * Env(t, 0.003f, 0.08f); break;
                    case "duel": s = (Mathf.Sin(t * 2 * Mathf.PI * 2100) * 0.5f + Mathf.Sin(t * 2 * Mathf.PI * 3170) * 0.3f + noise * 0.3f) * Env(t, 0.001f, 0.05f); break;
                    case "fire": lp += (noise - lp) * 0.08f; s = lp * 1.6f * Env(t, 0.1f, 0.35f) * (0.7f + 0.3f * Mathf.Sin(t * 40)); break;
                    case "rock": lp += (noise - lp) * 0.04f; s = (lp * 2f + Mathf.Sin(t * 2 * Mathf.PI * 60) * 0.5f) * Env(t, 0.01f, 0.22f); break;
                    case "magic": s = Mathf.Sin(t * 2 * Mathf.PI * (600 + Mathf.Sin(t * 30) * 200)) * Env(t, 0.05f, 0.25f) * 0.35f; break;
                    case "coin": s = (Mathf.Sin(t * 2 * Mathf.PI * 1318) * (t < 0.08f ? 1 : 0) + Mathf.Sin(t * 2 * Mathf.PI * 1760) * (t >= 0.08f ? 1 : 0)) * Env(t, 0.002f, 0.12f) * 0.3f; break;
                    case "horn": { float f = 196; s = (Mathf.Sin(t * 2 * Mathf.PI * f) + 0.5f * Mathf.Sin(t * 4 * Mathf.PI * f) + 0.25f * Mathf.Sin(t * 6 * Mathf.PI * f)) * Mathf.Clamp01(t * 6) * Mathf.Clamp01((dur - t) * 3) * 0.25f; break; }
                    case "march": { float beat = Mathf.Repeat(t, 0.3f); lp += (noise - lp) * 0.1f; s = (Mathf.Sin(beat * 2 * Mathf.PI * 80) * 0.8f + lp) * Mathf.Exp(-beat * 18); break; }
                    case "win":
                    case "lose":
                        {
                            float[] notes = name == "win" ? new[] { 523f, 659f, 784f, 1046f } : new[] { 392f, 349f, 311f, 262f };
                            int k = Mathf.Min(3, (int)(t / 0.28f));
                            float lt = t - k * 0.28f;
                            s = Pluck(notes[k], lt) * 0.5f; break;
                        }
                }
                d[i] = Mathf.Clamp(s, -1, 1);
            }
            return Clip(name, d);
        }

        // 拨弦（古筝风格）
        static float Pluck(float f, float t)
        {
            if (t < 0) return 0;
            float e = Mathf.Exp(-t * 3.2f) * Mathf.Clamp01(t * 400);
            return (Mathf.Sin(t * 2 * Mathf.PI * f) + 0.45f * Mathf.Sin(t * 4 * Mathf.PI * f) * Mathf.Exp(-t * 6) + 0.2f * Mathf.Sin(t * 6 * Mathf.PI * f) * Mathf.Exp(-t * 9)) * e * 0.4f;
        }

        AudioClip MakeMusic(string kind)
        {
            bool battle = kind == "battle";
            float beat = battle ? 0.2f : kind == "title" ? 0.42f : 0.32f;
            int bars = 16, stepsPerBar = 8;
            float dur = bars * stepsPerBar * beat;
            int n = (int)(dur * Rate);
            var d = new float[n];
            // 五声音阶：宫商角徵羽
            float root = battle ? 220f : kind == "title" ? 196f : 261.6f;
            int[] scale = battle ? new[] { 0, 3, 5, 7, 10, 12, 15 } : new[] { 0, 2, 4, 7, 9, 12, 14, 16 };
            var rnd = new System.Random(kind.GetHashCode());
            int idx = 3;
            var notes = new List<KeyValuePair<float, float>>();
            for (int s = 0; s < bars * stepsPerBar; s++)
            {
                if (rnd.NextDouble() < (battle ? 0.22 : 0.38)) continue;
                idx = Mathf.Clamp(idx + rnd.Next(-2, 3), 0, scale.Length - 1);
                notes.Add(new KeyValuePair<float, float>(s * beat, root * 2 * Mathf.Pow(2, scale[idx] / 12f)));
            }
            int[] bass = battle ? new[] { 0, 0, 3, 5 } : new[] { 0, 5, 7, 4 };
            for (int b = 0; b < bars * 2; b++) notes.Add(new KeyValuePair<float, float>(b * stepsPerBar / 2 * beat, root * 0.5f * Mathf.Pow(2, bass[(b / 2) % 4] / 12f)));
            foreach (var nt in notes)
            {
                int start = (int)(nt.Key * Rate);
                int len = Mathf.Min(n - start, (int)(Rate * 1.6f));
                for (int i = 0; i < len; i++) d[start + i] += Pluck(nt.Value, i / (float)Rate);
            }
            if (battle)
            {
                // 战鼓
                for (int b = 0; b < bars * stepsPerBar; b += 2)
                {
                    int start = (int)(b * beat * Rate); float lp = 0;
                    for (int i = 0; i < Rate * 0.3f && start + i < n; i++)
                    {
                        float t = i / (float)Rate; lp += ((float)(rnd.NextDouble() * 2 - 1) - lp) * 0.06f;
                        d[start + i] += (Mathf.Sin(t * 2 * Mathf.PI * (90 - t * 120)) * 0.7f + lp) * Mathf.Exp(-t * 14) * (b % 8 == 0 ? 0.9f : 0.45f);
                    }
                }
            }
            for (int i = 0; i < n; i++) d[i] = Mathf.Clamp(d[i] * 0.6f, -1, 1);
            return Clip("music_" + kind, d);
        }
    }
}
