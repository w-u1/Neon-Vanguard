using UnityEngine;

namespace NeonVanguard
{
    /// <summary>
    /// 经验球。带磁吸逻辑 —— 磁吸半径是玩家可以升级的属性，
    /// 这是同类游戏里非常重要的"正向成长体感"来源：清完一波后一路吸过去很爽。
    /// </summary>
    public class XpOrb : MonoBehaviour
    {
        int _value;
        Transform _visual;
        Material _mat;
        Renderer _r;
        float _spawnTime;
        Vector3 _velocity;
        float _bobPhase;

        const float Lifetime = 70f;

        public static void Spawn(Vector3 pos, int value)
        {
            var go = new GameObject("XpOrb");
            go.transform.position = pos;
            var orb = go.AddComponent<XpOrb>();
            orb.Init(value);
        }

        void Init(int value)
        {
            _value = Mathf.Max(1, value);
            _spawnTime = Time.time;
            _bobPhase = Random.Range(0f, 10f);

            _mat = MeshLib.Emissive(Palette.Xp, 2.4f, 0.6f);
            var go = MeshLib.MakePrimitive(PrimitiveType.Sphere, "Visual", transform,
                Vector3.zero, Vector3.one * 0.3f, _mat);
            _visual = go.transform;
            _r = go.GetComponent<MeshRenderer>();

            // 生成时向外弹一下，视觉上"爆出来"而不是原地出现
            _velocity = new Vector3(Random.Range(-2.2f, 2.2f), 0f, Random.Range(-2.2f, 2.2f));
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null) return;

            float dt = Time.deltaTime;

            // 超时回收，避免场上堆积
            if (Time.time - _spawnTime > Lifetime)
            {
                float fade = Mathf.Repeat(Time.time, 0.3f);
                if (_visual != null) _visual.gameObject.SetActive(fade > 0.15f);
                if (Time.time - _spawnTime > Lifetime + 2.5f) { Destroy(gameObject); return; }
            }

            var player = gm.Player;
            if (player != null && player.Alive)
            {
                Vector3 toPlayer = player.transform.position - transform.position;
                toPlayer.y = 0f;
                float dist = toPlayer.magnitude;
                float magnet = player.Stats.MagnetRadius;

                if (dist < magnet && dist > 0.01f)
                {
                    // 越近吸得越快（指数加速），靠近瞬间有"被吸住"的手感
                    float pull = Mathf.Lerp(26f, 7f, dist / magnet);
                    _velocity += toPlayer.normalized * (pull * dt);
                }
                else
                {
                    _velocity *= 1f - 1.6f * dt;
                }

                if (dist < 1.1f)
                {
                    Collect(player);
                    return;
                }
            }

            transform.position += _velocity * dt;
            _velocity *= 1f - 1.8f * dt;

            // 悬浮与自转
            if (_visual != null)
            {
                float bob = 0.42f + Mathf.Sin(Time.time * 4f + _bobPhase) * 0.14f;
                _visual.localPosition = new Vector3(0f, bob, 0f);
                _visual.Rotate(0f, 120f * dt, 0f, Space.Self);
                _visual.localScale = Vector3.one * (0.32f + Mathf.Sin(Time.time * 7f + _bobPhase) * 0.03f);
            }

            var arena = gm.Arena;
            // 回收边界要留足余量：敌人常常死在场地边缘，如果一出去就销毁，
            // 玩家（尤其升级过磁力场之后）会白白丢掉大量经验。
            if (arena != null)
            {
                if (!arena.IsInside(transform.position, 14f)) Destroy(gameObject);
                return;
            }
            if (Mathf.Abs(transform.position.x) > 200f || Mathf.Abs(transform.position.z) > 200f)
                Destroy(gameObject);
        }

        void Collect(Player player)
        {
            int gained = RunState.AddXp(Mathf.Max(1, Mathf.RoundToInt(_value * player.Stats.XpMult)));
            player.AddPickupChain();

            // 连拾时音高递增，一路"叮"上去非常解压
            AudioKit2.PlayUi(AudioKit.Pickup(player.PickupChain), 0.32f);

            if (gained > 0 && GameManager.I != null)
            {
                AudioKit2.PlayUi(AudioKit.LevelUp(), 0.9f);
                GameManager.I.OfferUpgrade(RunState.Level);
                if (Juice.I != null)
                {
                    Juice.I.Shake(0.22f);
                    Juice.I.PopText("LEVEL " + RunState.Level, player.transform.position + Vector3.up * 2.4f,
                        Palette.Good, 1.8f, 1.3f);
                }
            }

            Destroy(gameObject);
        }
    }

    /// <summary>回血包。掉落概率低，用来制造"要不要冒险去捡"的决策点。</summary>
    public class HealthPickup : MonoBehaviour
    {
        float _amount;
        float _spawnTime;
        Transform _visual;
        Vector3 _velocity;

        public static void Spawn(Vector3 pos, float amount)
        {
            var go = new GameObject("HealthPickup");
            go.transform.position = pos;
            var hp = go.AddComponent<HealthPickup>();
            hp.Init(amount);
        }

        void Init(float amount)
        {
            _amount = amount;
            _spawnTime = Time.time;
            _velocity = new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1.5f, 1.5f));

            var mat = MeshLib.Emissive(Palette.Health, 2.4f, 0.6f);
            var go = MeshLib.MakePrimitive(PrimitiveType.Cube, "Visual", transform,
                new Vector3(0f, 0.45f, 0f), Vector3.one * 0.62f, mat);
            _visual = go.transform;

            var crossMat = MeshLib.Emissive(Color.white, 3f);
            MeshLib.MakePrimitive(PrimitiveType.Cube, "CrossA", _visual,
                Vector3.zero, new Vector3(1.15f, 0.28f, 1.2f), crossMat);
            MeshLib.MakePrimitive(PrimitiveType.Cube, "CrossB", _visual,
                Vector3.zero, new Vector3(0.28f, 1.15f, 1.2f), crossMat);
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            float dt = Time.deltaTime;

            if (Time.time - _spawnTime > 22f) { Destroy(gameObject); return; }

            var player = gm.Player;
            if (player != null && player.Alive)
            {
                Vector3 toPlayer = player.transform.position - transform.position;
                toPlayer.y = 0f;
                float dist = toPlayer.magnitude;
                if (dist < player.Stats.MagnetRadius + 1.5f && dist > 0.01f)
                {
                    _velocity += toPlayer.normalized * (22f * dt);
                }
                if (dist < 1.2f)
                {
                    player.Heal(_amount);
                    AudioKit2.PlayUi(AudioKit.Pickup(6), 0.7f);
                    if (Juice.I != null) Juice.I.Shake(0.1f);
                    Destroy(gameObject);
                    return;
                }
            }

            transform.position += _velocity * dt;
            _velocity *= 1f - 2.2f * dt;
            if (_visual != null)
            {
                _visual.Rotate(0f, 90f * dt, 0f, Space.Self);
                _visual.localPosition = new Vector3(0f, 0.45f + Mathf.Sin(Time.time * 3.4f) * 0.12f, 0f);
            }
        }
    }
}
