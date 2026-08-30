using UnityEngine;

namespace NeonVanguard
{
    /// <summary>子弹归属。同时决定碰撞层和命中目标。</summary>
    public enum BulletTeam { Player, Enemy }

    /// <summary>
    /// 子弹。用 SphereCast 做连续碰撞检测（CCD）：
    /// 轨道炮初速 78 单位/秒，单帧位移会超过敌人直径，普通的触发器检测会直接穿过去。
    /// 所以这里每帧从上一帧位置向本帧位置做扫掠检测，保证高速子弹不会漏判。
    /// </summary>
    public class Bullet : MonoBehaviour
    {
        public BulletTeam Team;

        float _damage;
        float _speed;
        float _life;
        float _size;
        int _pierceLeft;
        float _knockback;
        bool _crit;
        float _explosiveChance;
        Vector3 _dir;
        float _spin;
        Transform _visual;
        Renderer _renderer;
        TrailRenderer _trail;
        Vector3 _lastPos;
        int _hitMask;

        // 复用缓冲区，避免每帧分配
        static readonly RaycastHit[] HitBuf = new RaycastHit[16];

        public void Launch(Vector3 origin, Vector3 dir, float damage, float speed, float life,
            float size, int pierce, float knockback, bool crit, BulletTeam team,
            Material mat, float explosiveChance = 0f, float spin = 0f)
        {
            transform.position = origin;
            _lastPos = origin;
            _dir = dir.normalized;
            _damage = damage;
            _speed = speed;
            _life = life;
            _size = size;
            _pierceLeft = pierce;
            _knockback = knockback;
            _crit = crit;
            _explosiveChance = explosiveChance;
            Team = team;
            _spin = spin;

            transform.rotation = Quaternion.LookRotation(_dir, Vector3.up);

            // 层掩码只作为粗筛；真正的敌我判定在命中时按组件过滤。
            // 这样即使工程里没有配置自定义 Layer，也不会出现"子弹打不中"的问题。
            int targetLayer = LayerMask.NameToLayer(team == BulletTeam.Player ? "Enemy" : "Player");
            _hitMask = targetLayer >= 0 ? (1 << targetLayer) : ~0;

            if (_visual == null) BuildVisual();
            _visual.localScale = new Vector3(size * 1.6f, size * 1.6f, size * 1.6f);
            _renderer.sharedMaterial = mat;

            if (_trail != null)
            {
                _trail.Clear();
                _trail.widthMultiplier = size * 1.5f;
                _trail.startColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                _trail.endColor = new Color(1f, 1f, 1f, 0f);
            }
        }

        void BuildVisual()
        {
            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);

            var mf = _visual.gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = MeshLib.Disc;
            var mr = _visual.gameObject.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _renderer = mr;
            // Disc 是横躺的，转到朝向 Z 轴的"子弹"姿态
            _visual.localRotation = Quaternion.Euler(90f, 0f, 0f);

            _trail = gameObject.AddComponent<TrailRenderer>();
            _trail.time = 0.09f;
            _trail.minVertexDistance = 0.02f;
            _trail.material = MeshLib.Unlit(Color.white, true, true);
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.numCapVertices = 0;
            var curve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            _trail.widthCurve = curve;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _life -= dt;
            if (_life <= 0f)
            {
                Despawn();
                return;
            }

            if (_spin != 0f) _visual.Rotate(0f, 0f, _spin * dt, Space.Self);

            Vector3 next = transform.position + _dir * (_speed * dt);
            Vector3 delta = next - _lastPos;
            float dist = delta.magnitude;

            if (dist > 0.0001f)
            {
                Vector3 sweepDir = delta / dist;
                if (Sweep(_lastPos, sweepDir, dist))
                {
                    if (!gameObject.activeSelf) return;
                }
            }

            transform.position = next;
            _lastPos = next;

            // 出界即回收，避免子弹飞到天边
            var arena = GameManager.I != null && GameManager.I.Arena != null
                ? GameManager.I.Arena : null;
            if (arena != null && !arena.IsInside(next, 6f)) Despawn();
        }

        /// <summary>
        /// 扫掠命中。返回 true 表示本帧已消耗（命中且未穿透），子弹已被回收。
        /// 用 SphereCastNonAlloc 一次拿到所有候选，再按组件判断敌我，
        /// 这样只取"最近的有效目标"，被场景装饰物挡住时不会误判。
        /// </summary>
        bool Sweep(Vector3 origin, Vector3 dir, float dist)
        {
            int n = Physics.SphereCastNonAlloc(origin, _size * 0.5f, dir, HitBuf, dist,
                _hitMask, QueryTriggerInteraction.Collide);
            if (n <= 0) return false;

            // 按距离排序，取最近的有效目标
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = HitBuf[i];
                if (h.collider == null) continue;
                if (!IsValidTarget(h.collider)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = i; }
            }
            if (best < 0) return false;

            var hit = HitBuf[best];
            if (Team == BulletTeam.Player)
            {
                var enemy = hit.collider.GetComponentInParent<Enemy>();
                if (enemy != null && enemy.Alive)
                {
                    enemy.TakeDamage(_damage, hit.point, dir, _crit, _knockback);
                    if (_explosiveChance > 0f && Random.value < _explosiveChance) Explode(hit.point);
                    SpawnFx.Spray(hit.point, -dir, _crit ? Palette.Warning : Color.white, 4, 7f, 0.25f);
                }
            }
            else
            {
                var player = GameManager.I != null ? GameManager.I.Player : null;
                if (player != null && player.Alive) player.TakeDamage(_damage, hit.point);
                SpawnFx.Burst(hit.point, Palette.EnemyChaser, 5, 6f, 0.3f);
            }

            if (_pierceLeft > 0)
            {
                _pierceLeft--;
                // 穿透：把起点推到命中点之外，避免同一目标被反复命中
                _lastPos = hit.point + dir * (_size + 0.06f);
                transform.position = _lastPos;
                return false;
            }

            if (Juice.I != null) Juice.I.Shake(Team == BulletTeam.Player ? 0.045f : 0.03f);
            Despawn();
            return true;
        }

        bool IsValidTarget(Collider c)
        {
            if (Team == BulletTeam.Player) return c.GetComponentInParent<Enemy>() != null;
            return c.GetComponentInParent<Player>() != null;
        }

        void Explode(Vector3 center)
        {
            const float radius = 2.6f;
            var hits = Physics.OverlapSphere(center, radius, LayerMask.GetMask("Enemy"));
            for (int i = 0; i < hits.Length; i++)
            {
                var e = hits[i].GetComponentInParent<Enemy>();
                if (e != null && e.Alive)
                {
                    Vector3 d = (e.transform.position - center);
                    d.y = 0f;
                    e.TakeDamage(_damage * 0.6f, e.transform.position, d.normalized, false, 6f);
                }
            }
            if (Juice.I != null)
            {
                Juice.I.Shake(0.22f);
                Juice.I.PopText("爆", center, Palette.Warning, 1.3f, 0.5f);
            }
            SpawnFx.Burst(center, Palette.Warning, 14, 9f, 0.5f);
        }

        void Despawn()
        {
            if (_trail != null) _trail.Clear();
            Pool.Despawn(gameObject);
        }
    }

    /// <summary>
    /// 轻量特效：命中火花、击杀碎裂、冲刺残影。
    /// 用极简的"小方块爆散"表现，不需要任何粒子资源，也不需要粒子系统开销。
    /// </summary>
    public static class SpawnFx
    {
        const string Key = "fx_spark";

        static GameObject Factory()
        {
            var go = new GameObject("FxSpark");
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = MeshLib.QuadXZ;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MeshLib.Unlit(Color.white, true, true);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.AddComponent<FxSpark>();
            return go;
        }

        /// <summary>向四周爆散一圈火花。</summary>
        public static void Burst(Vector3 pos, Color color, int count, float speed, float life)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float sp = speed * Random.Range(0.6f, 1.4f);
                var go = Pool.Spawn(Key, Factory, pos, Quaternion.Euler(90f, 0f, 0f));
                go.GetComponent<FxSpark>().Init(dir * sp, color, life * Random.Range(0.7f, 1.3f),
                    Random.Range(0.12f, 0.26f));
            }
        }

        /// <summary>定向喷溅（命中方向）。</summary>
        public static void Spray(Vector3 pos, Vector3 dir, Color color, int count, float speed, float life)
        {
            for (int i = 0; i < count; i++)
            {
                float a = Random.Range(-0.9f, 0.9f);
                var d = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f) * dir.normalized;
                var go = Pool.Spawn(Key, Factory, pos, Quaternion.Euler(90f, 0f, 0f));
                go.GetComponent<FxSpark>().Init(d.normalized * speed * Random.Range(0.5f, 1.5f),
                    color, life * Random.Range(0.6f, 1.2f), Random.Range(0.1f, 0.2f));
            }
        }
    }

    /// <summary>一个会飞散并淡出的火花片。</summary>
    public class FxSpark : MonoBehaviour
    {
        Vector3 _vel;
        float _life;
        float _maxLife;
        float _size;
        Renderer _r;
        Material _mat;

        public void Init(Vector3 vel, Color color, float life, float size)
        {
            _vel = vel;
            _life = life;
            _maxLife = life;
            _size = size;
            if (_r == null) _r = GetComponent<MeshRenderer>();
            _mat = MeshLib.Unlit(color, true, true);
            _r.sharedMaterial = _mat;
            transform.localScale = Vector3.one * size;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _life -= dt;
            if (_life <= 0f) { Pool.Despawn(gameObject); return; }

            transform.position += _vel * dt;
            _vel *= 1f - 4.5f * dt;
            _vel.y = 0f;

            float k = _life / _maxLife;
            transform.localScale = Vector3.one * _size * Mathf.Lerp(0.2f, 1f, k);
            if (_mat != null && _mat.HasProperty("_BaseColor"))
            {
                var c = _mat.GetColor("_BaseColor");
                c.a = k;
                _mat.SetColor("_BaseColor", c);
                _mat.SetColor("_Color", c);
            }
        }
    }
}
