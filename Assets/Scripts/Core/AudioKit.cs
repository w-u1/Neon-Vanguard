using System.Collections.Generic;
using UnityEngine;

namespace NeonVanguard
{
    /// <summary>
    /// 程序化音频合成器。工程里没有任何 .wav 资源，所有音效都在运行时用数学公式合成，
    /// 这样既保证工程体积接近零、又保证「clone 即跑」。
    ///
    /// 合成思路：包络(ADSR 简化版) × 振荡器(正弦/方波/三角/噪声) × 可选频率扫频。
    /// </summary>
    public static class AudioKit
    {
        const int SampleRate = 44100;

        static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();

        static AudioClip Get(string key, System.Func<AudioClip> factory)
        {
            AudioClip c;
            if (Cache.TryGetValue(key, out c) && c != null) return c;
            c = factory();
            Cache[key] = c;
            return c;
        }

        // ---------------- 具体音效 ----------------

        /// <summary>玩家射击：短促、干、带一点快速下扫的方波，听起来"哒"。</summary>
        public static AudioClip Shoot() => Get("shoot", () => Synth("sfx_shoot", 0.09f, (t, n) =>
        {
            float f = Mathf.Lerp(900f, 260f, t / 0.09f);
            float env = Mathf.Exp(-28f * t);
            return Square(t * f) * 0.32f * env;
        }));

        /// <summary>霰弹枪：低频噪声爆开，厚重。</summary>
        public static AudioClip Shotgun() => Get("shotgun", () => Synth("sfx_shotgun", 0.22f, (t, n) =>
        {
            float env = Mathf.Exp(-14f * t);
            float body = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(160f, 60f, t / 0.22f) * t) * 0.5f;
            return (n * 0.55f + body) * env * 0.42f;
        }));

        /// <summary>充能炮：先升调再"轰"，强调蓄力感。</summary>
        public static AudioClip Railgun() => Get("railgun", () => Synth("sfx_railgun", 0.45f, (t, n) =>
        {
            float env = Mathf.Exp(-7f * t);
            float sweep = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(1800f, 120f, Mathf.Pow(t / 0.45f, 0.5f)) * t);
            return (sweep * 0.45f + n * 0.2f) * env * 0.4f;
        }));

        /// <summary>命中：极短的高频噪声"咔"。</summary>
        public static AudioClip Hit() => Get("hit", () => Synth("sfx_hit", 0.06f, (t, n) =>
            n * Mathf.Exp(-52f * t) * 0.3f));

        /// <summary>击杀爆炸：噪声 + 低频推力。</summary>
        public static AudioClip Explode() => Get("explode", () => Synth("sfx_explode", 0.38f, (t, n) =>
        {
            float env = Mathf.Exp(-9f * t);
            float sub = Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.6f;
            return (n * 0.6f + sub) * env * 0.45f;
        }));

        /// <summary>受伤：下行的刺耳锯齿，明确"负反馈"。</summary>
        public static AudioClip Hurt() => Get("hurt", () => Synth("sfx_hurt", 0.28f, (t, n) =>
        {
            float f = Mathf.Lerp(420f, 90f, t / 0.28f);
            float env = Mathf.Exp(-11f * t);
            return (Saw(t * f) * 0.4f + n * 0.15f) * env * 0.5f;
        }));

        /// <summary>冲刺：快速上行的呼啸，配合屏幕表现很"爽"。</summary>
        public static AudioClip Dash() => Get("dash", () => Synth("sfx_dash", 0.18f, (t, n) =>
        {
            float f = Mathf.Lerp(300f, 1400f, t / 0.18f);
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.18f));
            return (n * 0.5f + Mathf.Sin(2f * Mathf.PI * f * t) * 0.3f) * env * 0.3f;
        }));

        /// <summary>拾取：干净的上行正弦"叮"，频率随连拾递增。</summary>
        public static AudioClip Pickup(int chain)
        {
            int step = Mathf.Clamp(chain, 0, 12);
            return Get("pickup" + step, () => Synth("sfx_pickup" + step, 0.12f, (t, n) =>
            {
                float f = 660f * Mathf.Pow(1.0595f, step * 2f);
                float env = Mathf.Exp(-24f * t);
                return (Mathf.Sin(2f * Mathf.PI * f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * f * 2f * t) * 0.2f) * env * 0.34f;
            }));
        }

        /// <summary>升级选择：三音上行琶音，正反馈。</summary>
        public static AudioClip LevelUp() => Get("levelup", () => Synth("sfx_levelup", 0.55f, (t, n) =>
        {
            float[] freqs = { 523.25f, 659.25f, 783.99f, 1046.5f };
            int idx = Mathf.Clamp((int)(t / 0.11f), 0, freqs.Length - 1);
            float local = t - idx * 0.11f;
            float env = Mathf.Exp(-11f * local) * Mathf.Exp(-1.6f * t);
            return Mathf.Sin(2f * Mathf.PI * freqs[idx] * t) * env * 0.34f;
        }));

        /// <summary>选择音：短促的确认音。</summary>
        public static AudioClip Select() => Get("select", () => Synth("sfx_select", 0.08f, (t, n) =>
            (Square(t * 880f) * 0.6f + Square(t * 1320f) * 0.4f) * Mathf.Exp(-40f * t) * 0.26f));

        /// <summary>新一波开始：低音预警。</summary>
        public static AudioClip WaveStart() => Get("wavestart", () => Synth("sfx_wave", 0.7f, (t, n) =>
        {
            float env = Mathf.Exp(-2.4f * t);
            float f = 110f + 40f * Mathf.Sin(2f * Mathf.PI * 3f * t);
            return (Saw(t * f) * 0.35f + Mathf.Sin(2f * Mathf.PI * f * 0.5f * t) * 0.4f) * env * 0.34f;
        }));

        /// <summary>Boss 出场：不谐和低音轰。</summary>
        public static AudioClip BossWarn() => Get("bosswarn", () => Synth("sfx_boss", 1.3f, (t, n) =>
        {
            float env = Mathf.Exp(-1.5f * t);
            float a = Mathf.Sin(2f * Mathf.PI * 58f * t);
            float b = Mathf.Sin(2f * Mathf.PI * 61.7f * t); // 拍频制造压迫感
            return (a + b) * 0.28f * env + n * 0.08f * env;
        }));

        /// <summary>敌人开火：低沉的"啵"。</summary>
        public static AudioClip EnemyShoot() => Get("enemyshoot", () => Synth("sfx_eshoot", 0.12f, (t, n) =>
        {
            float f = Mathf.Lerp(420f, 150f, t / 0.12f);
            return Triangle(t * f) * Mathf.Exp(-22f * t) * 0.24f;
        }));

        /// <summary>死亡：长时间的下坠音。</summary>
        public static AudioClip Death() => Get("death", () => Synth("sfx_death", 1.1f, (t, n) =>
        {
            float f = Mathf.Lerp(420f, 42f, Mathf.Pow(t / 1.1f, 0.6f));
            float env = Mathf.Exp(-3.2f * t);
            return (Saw(t * f) * 0.4f + n * 0.25f) * env * 0.42f;
        }));

        /// <summary>新星冲击波：大范围上升啸叫 + 低频。</summary>
        public static AudioClip Nova() => Get("nova", () => Synth("sfx_nova", 0.6f, (t, n) =>
        {
            float f = Mathf.Lerp(120f, 1600f, t / 0.6f);
            float env = Mathf.Exp(-4f * t) * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.6f));
            return (Mathf.Sin(2f * Mathf.PI * f * t) * 0.35f + n * 0.3f) * env * 0.4f;
        }));

        // ---------------- 波形 ----------------

        static float Square(float p) => Mathf.Sin(2f * Mathf.PI * p) >= 0f ? 1f : -1f;

        static float Saw(float p)
        {
            float x = p - Mathf.Floor(p);
            return x * 2f - 1f;
        }

        static float Triangle(float p)
        {
            float x = p - Mathf.Floor(p);
            return 4f * Mathf.Abs(x - 0.5f) - 1f;
        }

        /// <summary>
        /// 通用合成入口：generator 收到 (时间秒, 白噪声样本) 返回 [-1,1] 的振幅。
        /// 结尾做 3ms 淡出，避免爆音（click）。
        /// </summary>
        static AudioClip Synth(string name, float seconds, System.Func<float, float, float> generator)
        {
            int count = Mathf.Max(64, Mathf.RoundToInt(seconds * SampleRate));
            var data = new float[count];
            var rng = new System.Random(name.GetHashCode());

            float peak = 0.0001f;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float v = generator(t, noise);
                data[i] = v;
                float a = Mathf.Abs(v);
                if (a > peak) peak = a;
            }

            // 归一化到 0.9，保证音量一致且不削波
            float gain = 0.9f / peak;
            int fade = Mathf.Min(96, count / 8);
            for (int i = 0; i < count; i++)
            {
                float v = data[i] * gain;
                if (i < fade) v *= i / (float)fade;                     // 淡入防爆
                if (i > count - fade) v *= (count - i) / (float)fade;    // 淡出防爆
                data[i] = Mathf.Clamp(v, -1f, 1f);
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
