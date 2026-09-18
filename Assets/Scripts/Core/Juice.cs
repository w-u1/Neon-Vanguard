using System.Collections.Generic;
using UnityEngine;

namespace NeonVanguard
{
    /// <summary>
    /// 「打击感 / Juice」工具箱：屏幕震动、顿帧、命中闪白、飘字、伤害数字。
    /// 这部分是同类游戏好不好玩的关键 —— 同样的数值，有反馈和没反馈体验差一个量级。
    /// </summary>
    public class Juice : MonoBehaviour
    {
        public static Juice I;

        // ---- 屏幕震动 ----
        float _shakeAmp;
        float _shakeDecay = 4f;
        readonly List<float> _shakeStack = new List<float>();

        // ---- 顿帧（hitstop）----
        float _hitStopTimer;

        // ---- 飘字 ----
        readonly List<FloatingText> _floats = new List<FloatingText>();
        readonly List<FlashState> _flashes = new List<FlashState>();
        TextRenderer.Pool _textPool;

        class FloatingText
        {
            public GameObject Go;
            public float Life;
            public float MaxLife;
            public Vector3 Velocity;
            public float Scale;
        }

        class FlashState
        {
            public Renderer R;
            public Material Original;
            public Material FlashMat;
            public float Life;
            public float MaxLife;
        }

        public Camera Cam { get; private set; }

        // 相机的基础位姿。震动必须以"相对偏移"的方式叠加，
        // 否则每帧写绝对旋转会把相机的俯角拉平 —— 这个 bug 会让整个场景被挤到画面边缘。
        Vector3 _camBasePos;
        Quaternion _camBaseRot;

        public void Init(Camera cam)
        {
            I = this;
            Cam = cam;
            _textPool = new TextRenderer.Pool();
            if (Cam != null)
            {
                _camBasePos = Cam.transform.localPosition;
                _camBaseRot = Cam.transform.localRotation;
            }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // 震动的叠加与衰减
            float sum = 0f;
            for (int i = _shakeStack.Count - 1; i >= 0; i--)
            {
                _shakeStack[i] -= dt * _shakeDecay * (1f + i * 0.1f);
                if (_shakeStack[i] <= 0f) _shakeStack.RemoveAt(i);
                else sum += _shakeStack[i];
            }
            _shakeAmp = Mathf.Lerp(_shakeAmp, sum, 1f - Mathf.Exp(-22f * dt));

            if (Cam != null)
            {
                if (_camBaseRot == default(Quaternion))
                {
                    _camBasePos = Cam.transform.localPosition;
                    _camBaseRot = Cam.transform.localRotation;
                }

                // 位置偏移（默认值即基础位置，没有震动时相机不会被移动）
                Vector2 off = Random.insideUnitCircle * _shakeAmp;
                Cam.transform.localPosition = _camBasePos + new Vector3(off.x, off.y, 0f);

                // 旋转 = 基础俯角 + 微小滚转，绝不能用绝对角度覆盖基础姿态
                float roll = Random.Range(-1f, 1f) * _shakeAmp * 2.2f;
                Cam.transform.localRotation = _camBaseRot * Quaternion.Euler(0f, 0f, roll);
            }

            // 顿帧：短暂把时间缩放拉到很低，靠"卡一下"放大打击感
            if (_hitStopTimer > 0f)
            {
                _hitStopTimer -= dt;
                if (_hitStopTimer <= 0f && GameManager.I != null && GameManager.I.State == GameState.Playing)
                    Time.timeScale = 1f;
            }

            // 飘字
            for (int i = _floats.Count - 1; i >= 0; i--)
            {
                var f = _floats[i];
                f.Life -= dt;
                if (f.Life <= 0f)
                {
                    _textPool.Release(f.Go);
                    _floats.RemoveAt(i);
                    continue;
                }
                float k = f.Life / f.MaxLife;
                f.Go.transform.position += f.Velocity * dt;
                f.Velocity += new Vector3(0f, 3.2f, 0f) * dt;   // 轻微上浮加速
                f.Velocity *= 1f - 2.2f * dt;
                _textPool.SetScale(f.Go, f.Scale * Mathf.Lerp(0.65f, 1.05f, k));
                _textPool.SetAlpha(f.Go, Mathf.Clamp01(k * 1.6f));
            }

            // 命中闪白
            for (int i = _flashes.Count - 1; i >= 0; i--)
            {
                var fl = _flashes[i];
                fl.Life -= dt;
                if (fl.Life <= 0f || fl.R == null)
                {
                    if (fl.R != null) fl.R.sharedMaterial = fl.Original;
                    _flashes.RemoveAt(i);
                    continue;
                }
                if (fl.R != null) fl.R.sharedMaterial = fl.FlashMat;
            }
        }

        // ---------------- 对外接口 ----------------

        /// <summary>叠加一次屏幕震动。strength 约 0.05(轻) ~ 0.6(重)。</summary>
        public void Shake(float strength)
        {
            if (strength <= 0f) return;
            if (_shakeStack.Count > 14) _shakeStack.RemoveAt(0);
            _shakeStack.Add(Mathf.Min(strength, 1.2f));
        }

        /// <summary>顿帧。duration 单位为「真实秒」。</summary>
        public void HitStop(float duration, float scale = 0.06f)
        {
            if (GameManager.I == null || GameManager.I.State != GameState.Playing) return;
            if (duration <= _hitStopTimer) return;
            _hitStopTimer = duration;
            Time.timeScale = scale;
        }

        /// <summary>
        /// 伤害数字 / 提示文字。世界坐标生成，自动上浮淡出。
        /// text 支持 TextMeshPro 富文本。
        /// </summary>
        public GameObject PopText(string text, Vector3 worldPos, Color color, float scale = 1f, float life = 0.85f)
        {
            var go = _textPool.Get();
            _textPool.SetText(go, text);
            _textPool.SetColor(go, color);
            _textPool.SetScale(go, scale);
            _textPool.SetAlpha(go, 1f);
            go.transform.position = worldPos;
            go.transform.rotation = Cam != null ? Cam.transform.rotation : Quaternion.identity;

            _floats.Add(new FloatingText
            {
                Go = go,
                Life = life,
                MaxLife = life,
                Velocity = new Vector3(Random.Range(-0.7f, 0.7f), 2.6f, Random.Range(-0.5f, 0.5f)),
                Scale = scale
            });
            return go;
        }

        /// <summary>伤害数字（自动按伤害大小调字号和颜色）。</summary>
        public void PopDamage(float amount, Vector3 worldPos, bool crit = false)
        {
            int dmg = Mathf.Max(1, Mathf.RoundToInt(amount));
            float t = Mathf.InverseLerp(1f, 90f, dmg);
            float scale = Mathf.Lerp(0.85f, 1.9f, t) * (crit ? 1.35f : 1f);
            Color c = crit ? Palette.Warning : Color.Lerp(Color.white, Palette.Warning, t * 0.8f);
            PopText(dmg.ToString(), worldPos, c, scale, 0.8f);
        }

        /// <summary>让一个渲染器短暂闪白后再恢复原材质。</summary>
        public void Flash(Renderer r, Color color, float duration = 0.09f)
        {
            if (r == null) return;
            var mat = FlashMaterial(color);
            _flashes.Add(new FlashState
            {
                R = r,
                Original = r.sharedMaterial,
                FlashMat = mat,
                Life = duration,
                MaxLife = duration
            });
            r.sharedMaterial = mat;
        }

        // 闪白材质按颜色缓存：命中反馈每帧都可能触发，不能每次都 new Material
        static readonly Dictionary<Color, Material> FlashMats = new Dictionary<Color, Material>();

        static Material FlashMaterial(Color c)
        {
            Material m;
            if (FlashMats.TryGetValue(c, out m) && m != null) return m;
            m = MeshLib.Unlit(c, false, false);
            FlashMats[c] = m;
            return m;
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }
    }
}
