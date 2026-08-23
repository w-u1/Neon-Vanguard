using UnityEngine;

namespace NeonVanguard
{
    /// <summary>
    /// 音频播放服务。用少量 AudioSource 轮转复用，避免每次播放都新建对象。
    /// 音源本身由 AudioKit 程序化合成，这里只负责调度与音量平衡。
    /// </summary>
    public class AudioKit2 : MonoBehaviour
    {
        public static AudioKit2 I;

        const int Voices = 16;
        AudioSource[] _voices;
        int _next;
        AudioSource _music;

        public float SfxVolume = 0.75f;
        public float MusicVolume = 0.32f;

        public void Init()
        {
            I = this;
            _voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;      // 2D：俯视角小场景里 2D 更清晰
                src.volume = SfxVolume;
                _voices[i] = src;
            }

            var mgo = new GameObject("Music");
            mgo.transform.SetParent(transform, false);
            _music = mgo.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.loop = true;
            _music.spatialBlend = 0f;
            _music.volume = MusicVolume;
            _music.clip = GenerateAmbientLoop();
            _music.Play();
        }

        /// <summary>播放音效。pitch Jitter 让重复音效不呆板。</summary>
        public void Play(AudioClip clip, float volume = 1f, float pitchJitter = 0.06f)
        {
            if (clip == null || _voices == null) return;

            // 找一个没在播的声道；都在播就抢占最老的
            AudioSource src = null;
            for (int i = 0; i < _voices.Length; i++)
            {
                int idx = (_next + i) % _voices.Length;
                if (!_voices[idx].isPlaying) { src = _voices[idx]; _next = (idx + 1) % _voices.Length; break; }
            }
            if (src == null)
            {
                src = _voices[_next];
                _next = (_next + 1) % _voices.Length;
            }

            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.PlayOneShot(clip, Mathf.Clamp01(volume * SfxVolume));
        }

        /// <summary>UI 音效统一入口（音量略低，避免吵闹）。</summary>
        public static void PlayUi(AudioClip clip, float volume = 0.8f)
        {
            if (I != null) I.Play(clip, volume, 0.02f);
        }

        /// <summary>
        /// 生成一段可循环的低频氛围垫底音乐：两个相差几音分的三角波 + 缓慢的 LFO 滤波感。
        /// 刻意做得很"底噪"，不抢音效。
        /// </summary>
        AudioClip GenerateAmbientLoop()
        {
            int seconds = 8;
            int count = 44100 * seconds;
            var data = new float[count];

            // 和弦音（A2 / E3 / A3），做成缓慢起伏的 pad
            float[] freqs = { 110.0f, 164.81f, 220.0f, 277.18f };
            float[] amps = { 0.42f, 0.26f, 0.18f, 0.10f };

            for (int i = 0; i < count; i++)
            {
                float t = i / 44100f;
                float v = 0f;
                for (int k = 0; k < freqs.Length; k++)
                {
                    // 每个声部用不同相位的 LFO 做音量起伏，产生不断变化的织体
                    float lfo = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * (0.055f + k * 0.017f) * t + k * 1.7f);
                    v += Mathf.Sin(2f * Mathf.PI * freqs[k] * t) * amps[k] * lfo;
                }
                // 极轻的噪声层，增加"空间感"
                v += (Mathf.PerlinNoise(t * 0.35f, 0f) - 0.5f) * 0.05f;
                data[i] = v * 0.5f;
            }

            // 首尾交叉淡化，保证 loop 点无爆音
            int fade = 44100 / 2;
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                data[i] *= k;
                data[count - 1 - i] *= k;
            }

            var clip = AudioClip.Create("bgm_ambient", count, 1, 44100, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
