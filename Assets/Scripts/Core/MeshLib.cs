using UnityEngine;
using UnityEngine.Rendering;

namespace NeonVanguard
{
    /// <summary>
    /// 运行时几何库。整个游戏不依赖任何外部模型文件，所有可见物体都由这里
    /// 用程序化网格或 Unity 内建基础网格拼出来，保证工程「clone 下来就能跑」。
    /// 所有网格都做过程序化缓存，避免重复分配。
    /// </summary>
    public static class MeshLib
    {
        static Mesh _quadXZ;
        static Mesh _ring;
        static Mesh _disc;

        /// <summary>XZ 平面上的单位正方形（边长 2），UV 为 0..1。</summary>
        public static Mesh QuadXZ
        {
            get
            {
                if (_quadXZ != null) return _quadXZ;
                var m = new Mesh { name = "NV_QuadXZ" };
                m.vertices = new[]
                {
                    new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f),
                    new Vector3(1f, 0f,  1f),  new Vector3(-1f, 0f, 1f)
                };
                m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                m.RecalculateNormals();
                m.RecalculateBounds();
                _quadXZ = m;
                return _quadXZ;
            }
        }

        /// <summary>XZ 平面圆环，外径 1（再乘 lossyScale 使用）。</summary>
        public static Mesh Ring
        {
            get
            {
                if (_ring != null) return _ring;
                _ring = BuildRing(1f, 0.86f, 48);
                return _ring;
            }
        }

        /// <summary>XZ 平面实心圆盘，半径 1。</summary>
        public static Mesh Disc
        {
            get
            {
                if (_disc != null) return _disc;
                _disc = BuildDisc(1f, 32);
                return _disc;
            }
        }

        static Mesh BuildRing(float outer, float inner, int seg)
        {
            var m = new Mesh { name = "NV_Ring" };
            var verts = new Vector3[seg * 2];
            var tris = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts[i * 2] = new Vector3(c * outer, 0f, s * outer);
                verts[i * 2 + 1] = new Vector3(c * inner, 0f, s * inner);
            }
            for (int i = 0; i < seg; i++)
            {
                int n = (i + 1) % seg;
                int t = i * 6;
                tris[t] = i * 2; tris[t + 1] = n * 2; tris[t + 2] = i * 2 + 1;
                tris[t + 3] = n * 2; tris[t + 4] = n * 2 + 1; tris[t + 5] = i * 2 + 1;
            }
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh BuildDisc(float r, int seg)
        {
            var m = new Mesh { name = "NV_Disc" };
            var verts = new Vector3[seg + 1];
            var tris = new int[seg * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            for (int i = 0; i < seg; i++)
            {
                int n = (i + 1) % seg;
                int t = i * 3;
                tris[t] = 0; tris[t + 1] = n + 1; tris[t + 2] = i + 1;
            }
            m.vertices = verts;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ---------- 材质工厂 ----------

        static Shader _litShader;
        static Shader _unlitShader;

        /// <summary>
        /// 取标准光照 Shader。URP / 内建管线都能跑：
        /// 优先拿通用 Lit，拿不到就退回内建 Standard，再不行用无光照兜底。
        /// </summary>
        public static Shader LitShader
        {
            get
            {
                if (_litShader != null) return _litShader;
                _litShader = Shader.Find("Universal Render Pipeline/Lit");
                if (_litShader == null) _litShader = Shader.Find("Standard");
                if (_litShader == null) _litShader = Shader.Find("Legacy Shaders/Diffuse");
                if (_litShader == null) _litShader = UnlitShader;
                return _litShader;
            }
        }

        public static Shader UnlitShader
        {
            get
            {
                if (_unlitShader != null) return _unlitShader;
                _unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                if (_unlitShader == null) _unlitShader = Shader.Find("Unlit/Color");
                if (_unlitShader == null) _unlitShader = Shader.Find("Sprites/Default");
                return _unlitShader;
            }
        }

        /// <summary>
        /// 生成一个自发光材质的 Material。
        /// 由于 URP 和内建管线的属性名不同，这里把所有可能用到的属性都尝试设一遍，
        /// 不存在的属性会被 Unity 静默忽略，所以是安全的。
        /// </summary>
        public static Material Emissive(Color color, float emission = 1.6f, float smoothness = 0.35f)
        {
            var sh = LitShader;
            var mat = new Material(sh) { name = "NV_Mat_" + ColorUtility.ToHtmlStringRGB(color) };

            SetColorSafe(mat, "_BaseColor", color);
            SetColorSafe(mat, "_Color", color);
            SetColorSafe(mat, "_EmissionColor", color * emission);
            SetFloatSafe(mat, "_Smoothness", smoothness);
            SetFloatSafe(mat, "_Glossiness", smoothness);
            SetFloatSafe(mat, "_Metallic", 0.1f);

            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return mat;
        }

        /// <summary>生成无光照纯色材质，适合地板网格线、UI 装饰、粒子。</summary>
        public static Material Unlit(Color color, bool additive = false, bool transparent = false)
        {
            var sh = UnlitShader;
            var mat = new Material(sh) { name = "NV_Unlit_" + ColorUtility.ToHtmlStringRGB(color) };

            if (additive) color.a = Mathf.Max(color.a, 0.999f);
            SetColorSafe(mat, "_BaseColor", color);
            SetColorSafe(mat, "_Color", color);
            SetColorSafe(mat, "_RendererColor", color);
            SetFloatSafe(mat, "_Surface", transparent || additive ? 1f : 0f);
            SetFloatSafe(mat, "_Blend", additive ? 1f : 0f);
            SetFloatSafe(mat, "_ZWrite", transparent || additive ? 0f : 1f);

            if (transparent || additive)
            {
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
                if (additive)
                {
                    mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)BlendMode.One);
                }
                else
                {
                    mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                }
                mat.EnableKeyword("_ALPHABLEND_ON");
            }
            return mat;
        }

        public static void SetColorSafe(Material m, string prop, Color c)
        {
            if (m.HasProperty(prop)) m.SetColor(prop, c);
        }

        public static void SetFloatSafe(Material m, string prop, float v)
        {
            if (m.HasProperty(prop)) m.SetFloat(prop, v);
        }

        // ---------- 便捷构造 ----------

        /// <summary>创建一个带网格渲染器的子物体。</summary>
        public static GameObject MakePrimitive(PrimitiveType type, string name, Transform parent,
            Vector3 localPos, Vector3 localScale, Material mat, bool shadows = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            if (!shadows) mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>创建一个使用自定义网格的渲染子物体（不受光照影响，用于地面/光效）。</summary>
        public static GameObject MakeOverlayMesh(Mesh mesh, string name, Transform parent,
            Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }
    }
}
