using System.Collections.Generic;
using UnityEngine;

namespace NeonVanguard
{
    /// <summary>
    /// 中央对象池。子弹每秒可能生成上百发，用池化避免 GC 抖动 —— 这也是面试里
    /// 最常被问到的 Unity 性能优化点，工程里刻意把它独立出来方便讲解。
    /// </summary>
    public static class Pool
    {
        class Bucket
        {
            public readonly Stack<GameObject> Free = new Stack<GameObject>();
            public Transform Root;
        }

        static readonly Dictionary<string, Bucket> Buckets = new Dictionary<string, Bucket>();

        public static GameObject Spawn(string key, System.Func<GameObject> factory,
            Vector3 pos, Quaternion rot, Transform parent = null)
        {
            Bucket b;
            if (!Buckets.TryGetValue(key, out b))
            {
                b = new Bucket
                {
                    Root = new GameObject("Pool_" + key).transform
                };
                Buckets[key] = b;
            }

            GameObject go = null;
            while (b.Free.Count > 0 && go == null) go = b.Free.Pop();
            if (go == null)
            {
                go = factory();
                var tag = go.AddComponent<PooledObject>();
                tag.Key = key;
            }

            go.transform.SetParent(parent != null ? parent : b.Root, false);
            go.transform.position = pos;
            go.transform.rotation = rot;
            go.SetActive(true);
            return go;
        }

        public static void Despawn(GameObject go)
        {
            if (go == null) return;
            var tag = go.GetComponent<PooledObject>();
            if (tag == null)
            {
                Object.Destroy(go);
                return;
            }
            go.SetActive(false);
            Bucket b;
            if (Buckets.TryGetValue(tag.Key, out b))
            {
                go.transform.SetParent(b.Root, false);
                b.Free.Push(go);
            }
            else
            {
                Object.Destroy(go);
            }
        }

        /// <summary>切场景 / 重开一局时清空所有池，避免残留引用。</summary>
        public static void ClearAll()
        {
            // 回收所有仍然激活的池化对象（否则它们会留在场上变成"幽灵实体"）
            var active = Object.FindObjectsOfType<PooledObject>();
            for (int i = 0; i < active.Length; i++)
            {
                if (active[i] != null && active[i].gameObject.activeSelf)
                {
                    Object.Destroy(active[i].gameObject);
                }
            }

            foreach (var kv in Buckets)
            {
                if (kv.Value.Root != null) Object.Destroy(kv.Value.Root.gameObject);
            }
            Buckets.Clear();
        }
    }

    /// <summary>池化对象标记，由 Pool 自动挂载。</summary>
    public class PooledObject : MonoBehaviour
    {
        public string Key;
    }
}
