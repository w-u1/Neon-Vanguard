using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NeonVanguard
{
    /// <summary>
    /// 文字系统。设计目标：**不依赖任何随工程提交的字体资源**，同时保证中文能显示。
    ///
    /// 做法：优先使用 TextMeshPro，并从操作系统字体目录里加载一份中文字体
    /// （黑体 → 微软雅黑 → 宋体），用 TMP 的运行时动态字体资产按需生成字形；
    /// 若环境中拿不到 TMP 或中文字体，则自动降级到内建 LegacyRuntime 字体。
    ///
    /// 这样工程 clone 下来在任何 Windows 机器上都能直接显示中文，
    /// 也不会把 10MB+ 的字体文件塞进仓库。
    /// </summary>
    public static class TextRenderer
    {
        public static bool UseTmp { get; private set; }

        static TMP_FontAsset _tmpFont;
        static Font _legacyFont;
        static Material _worldUnlitMat;
        static bool _probed;

        /// <summary>
        /// 系统字体名回退候选。仅在工程内字体不可用时使用。
        /// 顺序：开源字体优先，其次是 Windows 自带的中文字体。
        /// </summary>
        static readonly string[] CjkFontNames =
        {
            "Noto Sans SC", "Noto Sans CJK SC", "Source Han Sans SC",
            "SimHei", "Microsoft YaHei", "SimSun"
        };

        /// <summary>工程内自带的中文字体（Resources 路径）。</summary>
        const string ProjectFontPath = "Fonts/NotoSansSC-VF";

        /// <summary>探测运行环境，决定走 TMP 还是 Legacy 分支。</summary>
        public static void Probe()
        {
            if (_probed) return;
            _probed = true;

            // 1) 拿到一份 Font。优先级：工程内 Resources 的中文字体 → 系统字体 → 内建字体。
            //    之所以把字体放进 Resources，是因为 TMP 的 "按路径创建" 重载只在编辑器程序集里，
            //    运行时程序集必须拿到一个 Font 对象才行。
            Font source = LoadProjectFont();
            string sourceDesc = source != null ? ("Resources/" + ProjectFontPath) : null;

            if (source == null)
            {
                source = LoadOsFont();
                sourceDesc = source != null ? ("OS:" + source.name) : null;
            }

            if (source == null)
            {
                source = LoadBuiltinFont();
                sourceDesc = source != null ? "builtin" : null;
            }

            // 2) 用 Font 构建 TMP 动态字体资产（中文按需生成字形，不会把整个字库烘进图集）
            if (source != null) _tmpFont = TryCreateTmpFont(source);

            // 3) 兜底：TMP 自带默认字体（无中文字形，但至少英文正常）
            if (_tmpFont == null)
            {
                try { _tmpFont = TMP_Settings.defaultFontAsset; } catch { _tmpFont = null; }
            }

            UseTmp = _tmpFont != null;

            // 4) Legacy 兜底字体
            _legacyFont = source != null ? source : LoadBuiltinFont();
            if (_legacyFont == null)
            {
                var all = Resources.FindObjectsOfTypeAll<Font>();
                if (all != null && all.Length > 0) _legacyFont = all[0];
            }

            FontSourceDesc = sourceDesc ?? "(无)";
            Debug.Log(string.Format("[TextRenderer] TMP={0}, 字体来源={1}", UseTmp, FontSourceDesc));
        }

        /// <summary>
        /// 校验中文能否真正渲染出来（返回缺字列表，空数组表示全部可用）。
        ///
        /// 实现说明：这里刻意**不用** TMP 的 HasCharacters 做判定。
        /// TMP 3.0.6 里 `HasCharacters(string, out List&lt;char&gt;)` 这个重载
        /// **没有 tryAddCharacter 参数**，对动态字体它只查"已缓存字形"，
        /// 会把尚未按需生成的汉字全部误报为缺字（这个坑实际踩到过，一度以为换字体失败）。
        ///
        /// 改用 Unity 字体系统自己的 RequestCharactersInTexture：
        /// 它就是为了"确认这些字能不能画出来"而存在的，对动态字体语义正确。
        /// </summary>
        public static char[] FindMissingGlyphs(string probe)
        {
            var font = _legacyFont;
            if (font == null) return probe.ToCharArray();

            var missing = new System.Collections.Generic.List<char>();
            for (int i = 0; i < probe.Length; i++)
            {
                bool ok;
                try
                {
                    // RequestCharactersInTexture 返回 void：先请求生成字形，
                    // 再用 HasCharacter 判断是否真的拿到了（dynamic=true 允许运行时生成）
                    font.RequestCharactersInTexture(probe[i].ToString(), 24, FontStyle.Normal);
                    ok = font.HasCharacter(probe[i]);
                }
                catch
                {
                    ok = false;
                }
                if (!ok) missing.Add(probe[i]);
            }
            return missing.ToArray();
        }

        /// <summary>
        /// 工程自带字体（Assets/Resources/Fonts/NotoSansSC-VF.ttf）。
        ///
        /// 选 Noto Sans SC 而不是系统黑体：它是 SIL OFL 1.1 授权，
        /// **明确允许打包再分发**，而 Windows 自带字体不允许随工程分发。
        /// 另外它是真 TrueType（sfnt 头 0x00010000），TMP 对其支持最稳。
        /// </summary>
        static Font LoadProjectFont()
        {
            try
            {
                var f = Resources.Load<Font>(ProjectFontPath);
                if (f != null) return f;
            }
            catch { }
            return null;
        }

        /// <summary>供自检报告当前使用的字体来源。</summary>
        public static string FontSourceDesc { get; private set; }

        /// <summary>系统字体，按字体名尝试。</summary>
        static Font LoadOsFont()
        {
            for (int i = 0; i < CjkFontNames.Length; i++)
            {
                try
                {
                    var f = Font.CreateDynamicFontFromOSFont(CjkFontNames[i], 48);
                    if (f != null) return f;
                }
                catch { }
            }
            return null;
        }

        static Font LoadBuiltinFont()
        {
            Font f = null;
            try { f = Resources.GetBuiltinResource(typeof(Font), "LegacyRuntime.ttf") as Font; } catch { }
            if (f == null)
            {
                try { f = Resources.GetBuiltinResource(typeof(Font), "Arial.ttf") as Font; } catch { }
            }
            return f;
        }

        static TMP_FontAsset TryCreateTmpFont(Font source)
        {
            try
            {
                var fa = TMP_FontAsset.CreateFontAsset(source, 90, 9,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                    1024, 1024, AtlasPopulationMode.Dynamic);
                if (fa != null)
                {
                    fa.name = "NV_CJK_Font";
                    foreach (var tex in fa.atlasTextures)
                    {
                        if (tex != null) tex.filterMode = FilterMode.Bilinear;
                    }
                    return fa;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[TextRenderer] TMP 动态字体创建失败，降级到 Legacy： " + e.Message);
            }
            return null;
        }

        public static Font LegacyFont
        {
            get { if (_legacyFont == null) Probe(); return _legacyFont; }
        }

        public static TMP_FontAsset TmpFont
        {
            get { if (!_probed) Probe(); return _tmpFont; }
        }

        // ------------------------------------------------------------------
        //  世界空间文字（伤害数字 / 飘字）
        // ------------------------------------------------------------------

        public static GameObject WorldText(Transform parent, string name, float fontSize)
        {
            Probe();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            if (UseTmp)
            {
                var tmp = go.AddComponent<TextMeshPro>();
                tmp.font = _tmpFont;
                tmp.fontSize = fontSize;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.raycastTarget = false;
                tmp.extraPadding = true;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) { mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false; }
            }
            else
            {
                var tm = go.AddComponent<TextMesh>();
                tm.font = LegacyFont;
                tm.fontSize = Mathf.Max(16, Mathf.RoundToInt(fontSize * 5f));
                tm.characterSize = 0.08f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                tm.color = Color.white;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.sharedMaterial = LegacyFont.material;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }
            return go;
        }

        /// <summary>
        /// 取一份"永远画在最前面"的世界文字材质。
        /// TMP 默认材质是会写深度的，飘字很容易被地面切掉，所以这里克隆一份并把 ZWrite 关掉。
        /// </summary>
        public static Material UnlitWorldMaterial()
        {
            if (_worldUnlitMat != null) return _worldUnlitMat;
            var src = UseTmp && _tmpFont != null ? _tmpFont.material : null;
            if (src == null && LegacyFont != null) src = LegacyFont.material;
            if (src == null) return null;

            var m = new Material(src) { name = "NV_WorldText" };
            m.SetInt("_ZWrite", 0);
            m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            m.renderQueue = 4000;
            _worldUnlitMat = m;
            return m;
        }

        // ------------------------------------------------------------------
        //  UI 文字
        // ------------------------------------------------------------------

        public static GameObject UiText(Transform parent, string name, string content,
            float fontSize, Color color, TextAnchor anchor = TextAnchor.MiddleLeft,
            bool bold = false)
        {
            Probe();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            if (UseTmp)
            {
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.font = _tmpFont;
                tmp.text = content;
                tmp.fontSize = fontSize;
                tmp.color = color;
                tmp.raycastTarget = false;
                tmp.alignment = ToTmpAlign(anchor);
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.extraPadding = true;
                if (bold) tmp.fontStyle = FontStyles.Bold;
            }
            else
            {
                var t = go.AddComponent<Text>();
                t.font = LegacyFont;
                t.text = content;
                t.fontSize = Mathf.RoundToInt(fontSize);
                t.color = color;
                t.raycastTarget = false;
                t.alignment = anchor;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            }
            return go;
        }

        static TextAlignmentOptions ToTmpAlign(TextAnchor a)
        {
            switch (a)
            {
                case TextAnchor.UpperLeft: return TextAlignmentOptions.TopLeft;
                case TextAnchor.UpperCenter: return TextAlignmentOptions.Top;
                case TextAnchor.UpperRight: return TextAlignmentOptions.TopRight;
                case TextAnchor.MiddleLeft: return TextAlignmentOptions.Left;
                case TextAnchor.MiddleCenter: return TextAlignmentOptions.Center;
                case TextAnchor.MiddleRight: return TextAlignmentOptions.Right;
                case TextAnchor.LowerLeft: return TextAlignmentOptions.BottomLeft;
                case TextAnchor.LowerCenter: return TextAlignmentOptions.Bottom;
                case TextAnchor.LowerRight: return TextAlignmentOptions.BottomRight;
                default: return TextAlignmentOptions.Left;
            }
        }

        public static void SetText(GameObject go, string s)
        {
            if (go == null) return;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp != null) { tmp.text = s; return; }
            var tmpW = go.GetComponent<TextMeshPro>();
            if (tmpW != null) { tmpW.text = s; return; }
            var t = go.GetComponent<Text>();
            if (t != null) { t.text = s; return; }
            var tm = go.GetComponent<TextMesh>();
            if (tm != null) tm.text = s;
        }

        public static void SetColor(GameObject go, Color c)
        {
            if (go == null) return;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp != null) { tmp.color = c; return; }
            var tmpW = go.GetComponent<TextMeshPro>();
            if (tmpW != null) { tmpW.color = c; return; }
            var t = go.GetComponent<Text>();
            if (t != null) { t.color = c; return; }
            var tm = go.GetComponent<TextMesh>();
            if (tm != null) tm.color = c;
        }

        public static void SetScale(GameObject go, float s)
        {
            if (go == null) return;
            go.transform.localScale = Vector3.one * s;
        }

        /// <summary>加粗（TMP 与 Legacy 两条路径都支持）。</summary>
        public static void SetBold(GameObject go, bool bold)
        {
            if (go == null) return;
            var tmp = go.GetComponent<TextMeshProUGUI>();
            if (tmp != null) { tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal; return; }
            var tmpW = go.GetComponent<TextMeshPro>();
            if (tmpW != null) { tmpW.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal; return; }
            var t = go.GetComponent<Text>();
            if (t != null) t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        }

        public static RectTransform SetupRect(GameObject go, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPos, Vector2 size)
        {
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2((anchorMin.x + anchorMax.x) * 0.5f, (anchorMin.y + anchorMax.y) * 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>世界飘字对象池（避免每帧 new GameObject）。</summary>
        public class Pool
        {
            readonly Stack<GameObject> _free = new Stack<GameObject>();
            Transform _root;

            Transform Root
            {
                get
                {
                    if (_root == null)
                    {
                        var go = new GameObject("Pool_WorldText");
                        _root = go.transform;
                    }
                    return _root;
                }
            }

            public GameObject Get()
            {
                GameObject go = null;
                while (_free.Count > 0 && go == null) go = _free.Pop();
                if (go == null)
                {
                    go = WorldText(Root, "Float", 7f);
                    var mat = UnlitWorldMaterial();
                    var mr = go.GetComponent<MeshRenderer>();
                    if (mr != null && mat != null) mr.sharedMaterial = mat;
                }
                go.SetActive(true);
                return go;
            }

            public void Release(GameObject go)
            {
                if (go == null) return;
                go.SetActive(false);
                go.transform.SetParent(Root, false);
                _free.Push(go);
            }

            public void SetText(GameObject go, string s) { TextRenderer.SetText(go, s); }
            public void SetColor(GameObject go, Color c) { TextRenderer.SetColor(go, c); }
            public void SetAlpha(GameObject go, float a)
            {
                if (go == null) return;
                var c = GetColor(go);
                c.a = a;
                SetColor(go, c);
            }
            public void SetScale(GameObject go, float s) { TextRenderer.SetScale(go, s); }

            static Color GetColor(GameObject go)
            {
                if (go == null) return Color.white;
                var a = go.GetComponent<TextMeshProUGUI>(); if (a != null) return a.color;
                var b = go.GetComponent<TextMeshPro>(); if (b != null) return b.color;
                var c = go.GetComponent<Text>(); if (c != null) return c.color;
                var d = go.GetComponent<TextMesh>(); if (d != null) return d.color;
                return Color.white;
            }
        }
    }
}
