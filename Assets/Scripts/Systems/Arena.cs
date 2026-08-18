using UnityEngine;
using UnityEngine.Rendering;

namespace NeonVanguard
{
    /// <summary>
    /// 竞技场。用地板 + 程序化网格 + 边界光环搭出一个"霓虹角斗场"。
    /// 网格几何直接生成成网格（而不是贴图），避免 mipmap 在中远景糊掉，
    /// 也省掉了导入设置。
    /// </summary>
    public class Arena : MonoBehaviour
    {
        /// <summary>玩法边界：玩家和敌人的活动半径。</summary>
        public float PlayRadius = 27f;

        /// <summary>地板可视化半径（比玩法边界略大，边缘更像"场地"）。</summary>
        public float VisualRadius = 30f;

        Material _floorMat;
        Material _gridMat;
        Material _gridMajorMat;
        Material _ringMat;

        public void Build(Transform parent, float radius)
        {
            VisualRadius = radius;
            PlayRadius = radius - 3f;

            // 地面用"受光"材质而不是 Unlit：
            // Unlit 在暗场景里只能靠自身颜色，容易和背景糊在一起看不出边界；
            // 受光材质能吃到方向光与自发光，地面才有可读的明暗与反光。
            _floorMat = MeshLib.Emissive(new Color(0.10f, 0.125f, 0.20f), 0.55f, 0.25f);
            _gridMat = MeshLib.Unlit(new Color(Palette.BgGrid.r, Palette.BgGrid.g, Palette.BgGrid.b, 1f));
            _gridMajorMat = MeshLib.Unlit(new Color(Palette.BgGridMajor.r, Palette.BgGridMajor.g, Palette.BgGridMajor.b, 1f));
            _ringMat = MeshLib.Unlit(Palette.Player * 0.9f);

            BuildFloor(parent);
            BuildGrid(parent, 2.5f, 0.045f);
            BuildBoundary(parent);
            BuildPylons(parent);
            BuildScenery(parent);
        }

        /// <summary>
        /// 场外景观：用 Kenney Space Kit 的模型（CC0）围出"基地"的感觉。
        /// 这些物件全部落在玩法边界之外，只有视觉作用，不影响战斗判定
        /// （它们的碰撞体在导入时已被 MeshLib 之外的路径保留，所以这里显式移除）。
        /// </summary>
        void BuildScenery(Transform parent)
        {
            var root = new GameObject("Scenery");
            root.transform.SetParent(parent, false);

            var rng = new System.Random(20240517);

            // 1) 环形分布的岩石与水晶，构成天然围栏
            string[] rocks = { "rock_largeA", "rock_largeB", "rock_crystals", "rock_crystalsLargeA", "rocks_smallA", "bones", "craterLarge" };
            int ringCount = 44;
            for (int i = 0; i < ringCount; i++)
            {
                float ang = (i / (float)ringCount) * Mathf.PI * 2f + (float)rng.NextDouble() * 0.09f;
                float dist = VisualRadius + 1.2f + (float)rng.NextDouble() * 6.5f;
                var pos = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                string pick = rocks[rng.Next(rocks.Length)];
                float h = 1.1f + (float)rng.NextDouble() * 3.4f;
                var go = ModelLibrary.Instantiate(pick, root.transform, h);
                if (go == null) break;
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                StripColliders(go);
            }

            // 2) 四个方向的机库 / 大门 / 卫星天线，作为地标
            string[] landmarks = { "structure_detailed", "gate_complex", "satelliteDish_large", "structure", "rocket_baseA" };
            for (int i = 0; i < landmarks.Length; i++)
            {
                float ang = (i / (float)landmarks.Length) * Mathf.PI * 2f + 0.6f;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var go = ModelLibrary.Instantiate(landmarks[i], root.transform, 5.5f + (i % 2) * 2.5f);
                if (go == null) continue;
                go.transform.localPosition = dir * (VisualRadius + 8.5f);
                // 面向场地中心
                go.transform.localRotation = Quaternion.LookRotation(-dir, Vector3.up);
                StripColliders(go);
            }

            // 3) 场内的掩体装饰：制造"这是个基地"的叙事感。
            //    刻意压在 r≈20 之外的环形带里，中心 20 单位直径保持空旷，
            //    避免道具挡住走位与弹道（它们也没有碰撞体，见 StripColliders）。
            string[] props = { "barrel", "machine_generator", "machine_wireless", "chimney_detailed" };
            for (int i = 0; i < 16; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = 21.5f + (float)rng.NextDouble() * 6f;
                // 尺寸刻意压小：掩体是用来"提示这是基地"的，不能盖住战斗区
                var go = ModelLibrary.Instantiate(props[rng.Next(props.Length)], root.transform,
                    0.5f + (float)rng.NextDouble() * 0.35f);
                if (go == null) break;
                go.transform.localPosition = new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                StripColliders(go);
            }
        }

        /// <summary>
        /// 移除装饰物的碰撞体。子弹用 SphereCast 检测，如果这些装饰物带碰撞体，
        /// 会挡住子弹 —— 装饰不应该影响战斗，所以统一剥掉。
        /// </summary>
        static void StripColliders(GameObject go)
        {
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null) Object.Destroy(cols[i]);
            }
        }

        void BuildFloor(Transform parent)
        {
            // 圆盘地板 + 极暗的方形底座，给场地一点"厚度感"
            MeshLib.MakeOverlayMesh(MeshLib.Disc, "FloorDisc", parent,
                new Vector3(0f, -0.02f, 0f), Vector3.one * (VisualRadius + 1.5f), _floorMat);

            var baseMat = MeshLib.Unlit(new Color(0.030f, 0.038f, 0.062f));
            MeshLib.MakePrimitive(PrimitiveType.Cube, "FloorBase", parent,
                new Vector3(0f, -0.65f, 0f), new Vector3(VisualRadius * 2f + 3f, 1.2f, VisualRadius * 2f + 3f), baseMat);
        }

        /// <summary>
        /// 生成地面网格。每 N 条线用更亮的材质（major line），
        /// 这是很通用的"读距离"手段 —— 玩家能借此判断走位距离。
        /// </summary>
        void BuildGrid(Transform parent, float spacing, float width)
        {
            var minor = new GameObject("GridMinor");
            minor.transform.SetParent(parent, false);
            var major = new GameObject("GridMajor");
            major.transform.SetParent(parent, false);

            int half = Mathf.CeilToInt(VisualRadius / spacing);
            var mfMinor = minor.AddComponent<MeshFilter>();
            var mrMinor = minor.AddComponent<MeshRenderer>();
            mrMinor.sharedMaterial = _gridMat;
            mrMinor.shadowCastingMode = ShadowCastingMode.Off;
            mrMinor.receiveShadows = false;

            var mfMajor = major.AddComponent<MeshFilter>();
            var mrMajor = major.AddComponent<MeshRenderer>();
            mrMajor.sharedMaterial = _gridMajorMat;
            mrMajor.shadowCastingMode = ShadowCastingMode.Off;
            mrMajor.receiveShadows = false;

            var minorVerts = new System.Collections.Generic.List<Vector3>();
            var minorTris = new System.Collections.Generic.List<int>();
            var majorVerts = new System.Collections.Generic.List<Vector3>();
            var majorTris = new System.Collections.Generic.List<int>();

            for (int i = -half; i <= half; i++)
            {
                float c = i * spacing;
                if (Mathf.Abs(c) > VisualRadius) continue;

                // 弦长：保证线不超出圆盘
                float halfLen = Mathf.Sqrt(Mathf.Max(0f, VisualRadius * VisualRadius - c * c));
                bool isMajor = (i % 4 == 0);
                var verts = isMajor ? majorVerts : minorVerts;
                var tris = isMajor ? majorTris : minorTris;
                float w = isMajor ? width * 1.8f : width;

                AddLine(verts, tris, new Vector3(c, 0f, -halfLen), new Vector3(c, 0f, halfLen), w, 0.012f);
                AddLine(verts, tris, new Vector3(-halfLen, 0f, c), new Vector3(halfLen, 0f, c), w, 0.012f);
            }

            CacheMesh(mfMinor, minorVerts, minorTris);
            CacheMesh(mfMajor, majorVerts, majorTris);
        }

        static void AddLine(System.Collections.Generic.List<Vector3> verts,
            System.Collections.Generic.List<int> tris, Vector3 a, Vector3 b, float width, float y)
        {
            Vector3 dir = (b - a).normalized;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x) * (width * 0.5f);
            a.y = y; b.y = y;
            int baseIdx = verts.Count;
            verts.Add(a - side);
            verts.Add(a + side);
            verts.Add(b - side);
            verts.Add(b + side);
            tris.Add(baseIdx); tris.Add(baseIdx + 2); tris.Add(baseIdx + 1);
            tris.Add(baseIdx + 1); tris.Add(baseIdx + 2); tris.Add(baseIdx + 3);
        }

        static void CacheMesh(MeshFilter mf, System.Collections.Generic.List<Vector3> verts,
            System.Collections.Generic.List<int> tris)
        {
            var m = new Mesh { name = "NV_Grid" };
            m.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            mf.sharedMesh = m;
        }

        /// <summary>边界光环：两道圆环 + 一圈短柱，强调"出界就危险"。</summary>
        void BuildBoundary(Transform parent)
        {
            var ringRoot = new GameObject("Boundary");
            ringRoot.transform.SetParent(parent, false);

            MeshLib.MakeOverlayMesh(MeshLib.Ring, "RingOuter", ringRoot.transform,
                new Vector3(0f, 0.03f, 0f), Vector3.one * PlayRadius, _ringMat);

            var dimMat = MeshLib.Unlit(Palette.Player * 0.35f);
            MeshLib.MakeOverlayMesh(MeshLib.Ring, "RingInner", ringRoot.transform,
                new Vector3(0f, 0.03f, 0f), Vector3.one * (PlayRadius - 0.55f), dimMat);
        }

        /// <summary>四角立柱，纯装饰，用来给场地提供空间参照物。</summary>
        void BuildPylons(Transform parent)
        {
            var pylonMat = MeshLib.Emissive(new Color(0.16f, 0.28f, 0.42f), 0.7f);
            var capMat = MeshLib.Emissive(Palette.Player * 0.8f, 1.4f);

            var root = new GameObject("Pylons");
            root.transform.SetParent(parent, false);

            for (int i = 0; i < 4; i++)
            {
                float ang = Mathf.PI * 0.25f + i * Mathf.PI * 0.5f;
                var pos = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (PlayRadius + 1.6f);

                MeshLib.MakePrimitive(PrimitiveType.Cube, "Pylon" + i, root.transform,
                    pos + new Vector3(0f, 1.5f, 0f), new Vector3(0.9f, 3f, 0.9f), pylonMat);

                MeshLib.MakePrimitive(PrimitiveType.Cube, "PylonCap" + i, root.transform,
                    pos + new Vector3(0f, 3.15f, 0f), new Vector3(1.15f, 0.3f, 1.15f), capMat);
            }
        }

        /// <summary>把一个位置限制在玩法边界内（圆形夹具）。</summary>
        public Vector3 Clamp(Vector3 pos)
        {
            Vector2 flat = new Vector2(pos.x, pos.z);
            float d = flat.magnitude;
            if (d > PlayRadius)
            {
                flat = flat / d * PlayRadius;
                pos.x = flat.x;
                pos.z = flat.y;
            }
            return pos;
        }

        public bool IsInside(Vector3 pos, float margin = 0f)
        {
            float r = PlayRadius + margin;
            return pos.x * pos.x + pos.z * pos.z <= r * r;
        }

        /// <summary>在圆内取一个随机点（用于刷怪）。</summary>
        public Vector3 RandomSpawnPoint(float minRadius, float maxRadius)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Random.Range(minRadius * minRadius, maxRadius * maxRadius));
            return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }
    }
}
