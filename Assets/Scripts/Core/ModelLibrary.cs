using System.Collections.Generic;
using UnityEngine;

namespace NeonVanguard
{
    /// <summary>
    /// 模型资源层。
    ///
    /// 美术资源来自 Kenney 的 Space Kit（CC0 公共领域，见 Docs/ART_CREDITS.md），
    /// 以 OBJ + MTL 形式放在 Assets/Resources/Models 下。
    ///
    /// 设计要点：
    ///  1) **所有模型都有程序化兜底**：取不到模型时回落到基础几何体，
    ///     保证工程在任何情况下都能跑起来（也方便把美术资源整体换掉）。
    ///  2) **换材质不改资源**：Kenney 的 OBJ 在导入时生成的材质是工程内的共享资产，
    ///     直接改会污染其它模型。这里对每个材质做 `new Material(src)` 克隆后再染色，
    ///     并让克隆体跟随实例一起销毁。
    ///  3) 模型朝向 +Z、脚底在 y=0，与 gameplay 代码的约定一致，不需要额外旋转。
    /// </summary>
    public static class ModelLibrary
    {
        static readonly Dictionary<string, GameObject> Cache = new Dictionary<string, GameObject>();

        /// <summary>按名字加载模型（已缓存）。找不到返回 null。</summary>
        public static GameObject Load(string name)
        {
            GameObject go;
            if (Cache.TryGetValue(name, out go)) return go;

            go = Resources.Load<GameObject>("Models/" + name);
            if (go == null) Debug.LogWarning("[ModelLibrary] 未找到模型：" + name + "（将使用程序化几何体兜底）");
            Cache[name] = go;
            return go;
        }

        public static bool Has(string name)
        {
            return Load(name) != null;
        }

        /// <summary>模型在本地空间的高度（用于自动缩放到目标体型）。</summary>
        public static float HeightOf(string name)
        {
            var src = Load(name);
            if (src == null) return 1f;

            var filters = src.GetComponentsInChildren<MeshFilter>(true);
            bool first = true;
            Bounds b = new Bounds();
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh == null) continue;
                var wb = filters[i].sharedMesh.bounds;
                if (first) { b = wb; first = false; }
                else b.Encapsulate(wb);
            }
            return first ? 1f : Mathf.Max(0.01f, b.size.y);
        }

        /// <summary>
        /// 实例化模型并按目标高度自动缩放。返回实例（已设好 localScale），失败返回 null。
        /// </summary>
        public static GameObject Instantiate(string name, Transform parent, float targetHeight,
            Color? tint = null, float emission = 1.35f, bool shadows = false)
        {
            var src = Load(name);
            if (src == null) return null;

            var go = Object.Instantiate(src);
            go.name = "Model_" + name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            float h = HeightOf(name);
            float s = targetHeight > 0f ? targetHeight / h : 1f;
            // 上限：低矮模型（比如扁平的岩石）若只按"高度归一化"，宽度会被放大到离谱。
            // 这里给缩放封顶，宁可让矮模型偏小，也不要出现几层楼高的石头。
            s = Mathf.Min(s, 6f);
            go.transform.localScale = Vector3.one * s;

            ApplyMaterials(go, tint, emission, shadows);
            return go;
        }

        /// <summary>
        /// 处理材质。为避免污染工程内的共享材质资产，每个用到的材质都克隆一份，
        /// 再用 DestroyOnLoad 让它跟随场景销毁。
        /// </summary>
        public static void ApplyMaterials(GameObject go, Color? tint, float emission, bool shadows)
        {
            var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                var mr = renderers[r];
                mr.shadowCastingMode = shadows
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                var src = mr.sharedMaterials;
                var dst = new Material[src.Length];
                for (int m = 0; m < src.Length; m++)
                {
                    if (src[m] == null) { dst[m] = null; continue; }

                    Color baseColor = src[m].HasProperty("_Color") ? src[m].GetColor("_Color")
                        : (src[m].HasProperty("_BaseColor") ? src[m].GetColor("_BaseColor") : Color.white);

                    Color final = baseColor;
                    float em = 0.35f;

                    if (tint.HasValue && IsTintable(src[m].name))
                    {
                        // 主结构件染色（保留明暗层次：亮件更亮、暗件更暗）
                        float lum = Mathf.Max(0.25f, baseColor.grayscale);
                        final = tint.Value * Mathf.Lerp(0.55f, 1.25f, lum);
                        em = emission;
                    }
                    else if (IsSelfGlow(src[m].name))
                    {
                        // 水晶等本身就发光的部件：保持原色但加强自发光
                        final = baseColor * 1.6f;
                        em = 2.2f;
                    }

                    final.a = 1f;
                    var mat = MeshLib.Emissive(final, em, 0.3f);
                    mat.name = "NV_" + go.name + "_" + src[m].name;
                    dst[m] = mat;
                }
                mr.sharedMaterials = dst;
            }
        }

        /// <summary>这些材质槽是"结构主体"，可以按敌人类型染色。</summary>
        static bool IsTintable(string matName)
        {
            if (string.IsNullOrEmpty(matName)) return false;
            string n = matName.ToLowerInvariant();
            // Kenney Space Kit 的主结构材质
            if (n == "metal" || n == "metaldark" || n == "metalred") return true;
            return false;
        }

        /// <summary>这些材质槽是装饰/发光件，保持原色。</summary>
        static bool IsSelfGlow(string matName)
        {
            if (string.IsNullOrEmpty(matName)) return false;
            string n = matName.ToLowerInvariant();
            return n == "crystal" || n == "skin" || n == "rock" || n == "rockdark" || n == "rocktrack";
        }
    }
}
