using System.Collections.Generic;
using UnityEngine;

namespace NeonVanguard
{
    /// <summary>
    /// 波次导演：负责刷什么、刷多少、什么时候算清完。
    ///
    /// 难度曲线设计：
    ///  - 每波总敌人数随波次线性增长（封顶，避免后期变成幻灯片）；
    ///  - 场上同屏上限逐步提高，保证"压迫感"上升但不是一次性洪流；
    ///  - 每 5 波出一个首领，首领波同时有少量杂兵，避免变成纯单挑；
    ///  - 敌人类型按波次解锁，保证玩家每几波就会遇到新东西。
    /// </summary>
    public class WaveDirector : MonoBehaviour
    {
        public const int BossEveryNWaves = 5;

        public int CurrentWave { get; private set; }
        public bool WaveActive { get; private set; }
        public int AliveCount { get { return _alive.Count; } }
        public int RemainingToSpawn { get { return _spawnQueue.Count; } }

        readonly List<Enemy> _alive = new List<Enemy>();
        readonly Queue<EnemyDef> _spawnQueue = new Queue<EnemyDef>();
        readonly List<Enemy> _scratch = new List<Enemy>();

        float _spawnTimer;
        float _spawnInterval = 0.35f;
        int _maxConcurrent = 10;
        float _intermissionTimer;
        bool _inIntermission;

        public bool IsBossWave { get { return CurrentWave > 0 && CurrentWave % BossEveryNWaves == 0; } }

        public void ResetForNewRun()
        {
            StopAll();
            CurrentWave = 0;
        }

        /// <summary>清空场上所有敌人与待刷队列。</summary>
        public void StopAll()
        {
            for (int i = 0; i < _alive.Count; i++)
            {
                if (_alive[i] != null) Destroy(_alive[i].gameObject);
            }
            _alive.Clear();
            _spawnQueue.Clear();
            WaveActive = false;
            _inIntermission = false;
            RunState.Wave = 0;

            // 清掉残留的敌方子弹与掉落物
            var bullets = FindObjectsOfType<Bullet>();
            for (int i = 0; i < bullets.Length; i++)
            {
                if (bullets[i].Team == BulletTeam.Enemy) Destroy(bullets[i].gameObject);
            }
            var orbs = FindObjectsOfType<XpOrb>();
            for (int i = 0; i < orbs.Length; i++) Destroy(orbs[i].gameObject);
            var packs = FindObjectsOfType<HealthPickup>();
            for (int i = 0; i < packs.Length; i++) Destroy(packs[i].gameObject);
        }

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || gm.State != GameState.Playing) return;

            float dt = Time.deltaTime;

            // 清理已被销毁的引用
            _scratch.Clear();
            for (int i = 0; i < _alive.Count; i++)
            {
                if (_alive[i] == null) _scratch.Add(_alive[i]);
            }
            for (int i = 0; i < _scratch.Count; i++) _alive.Remove(_scratch[i]);

            if (_inIntermission)
            {
                _intermissionTimer -= dt;
                if (_intermissionTimer <= 0f)
                {
                    _inIntermission = false;
                    BeginNextWave();
                }
                return;
            }

            if (!WaveActive) return;

            // 持续刷怪，直到队列耗尽
            if (_spawnQueue.Count > 0)
            {
                _spawnTimer -= dt;
                if (_spawnTimer <= 0f && _alive.Count < _maxConcurrent)
                {
                    _spawnTimer = _spawnInterval * Random.Range(0.7f, 1.3f);
                    SpawnOne(_spawnQueue.Dequeue());
                }
            }

            // 判定本波结束
            if (_spawnQueue.Count == 0 && _alive.Count == 0)
            {
                CompleteWave();
            }
        }

        // ------------------------------------------------------------------
        //  波次推进
        // ------------------------------------------------------------------

        public void BeginNextWave()
        {
            CurrentWave++;
            // 注意：RunState.Wave 只在这里（以及波次事件）更新，
            // 不要从 HUD 回调里反推，否则一旦 UI 订阅失效，存档与结算的波次就会变成 0。
            RunState.Wave = CurrentWave;
            // 幸存奖励：活过一波给分，鼓励推进而不是苟着
            RunState.AddScore(20 + CurrentWave * 8);

            BuildQueue(CurrentWave);

            WaveActive = true;
            _spawnTimer = 0.35f;
            _spawnInterval = Mathf.Max(0.075f, 0.42f - CurrentWave * 0.012f);
            _maxConcurrent = Mathf.Clamp(8 + CurrentWave * 2, 8, 36);

            bool boss = IsBossWave;

            if (boss) AudioKit2.PlayUi(AudioKit.BossWarn(), 0.85f);
            else AudioKit2.PlayUi(AudioKit.WaveStart(), 0.55f);

            EventBus.Publish(new WaveEvent { Wave = CurrentWave, TotalWaves = 0, IsBossWave = boss });
            EventBus.Publish(new ToastEvent
            {
                Message = boss ? ("第 " + CurrentWave + " 波 · 首领来袭") : ("第 " + CurrentWave + " 波"),
                Color = boss ? Palette.Danger : Palette.Player
            });

            if (Juice.I != null) Juice.I.Shake(boss ? 0.55f : 0.18f);
        }

        /// <summary>根据波次构成敌群。</summary>
        void BuildQueue(int wave)
        {
            _spawnQueue.Clear();

            bool bossWave = wave % BossEveryNWaves == 0;

            if (bossWave)
            {
                _spawnQueue.Enqueue(EnemyCatalog.Boss(wave));
                // 首领波配少量杂兵，制造"边躲首领边清小怪"的复合压力
                int adds = 4 + wave / 2;
                for (int i = 0; i < adds; i++)
                {
                    var d = (i % 3 == 2 && wave >= 3) ? EnemyCatalog.Ranged() : EnemyCatalog.Chaser();
                    EnemyCatalog.ApplyWaveScaling(d, wave);
                    _spawnQueue.Enqueue(d);
                }
                return;
            }

            int total = Mathf.Clamp(6 + Mathf.RoundToInt(wave * 3.2f), 6, 58);

            // 类型权重随波次变化：
            //   第 1 波            纯追猎者（教会基础操作）
            //   第 2 波起          加入暴君（重装锚点）
            //   第 3 波起          加入织网者（远程，逼玩家进攻）
            //   第 4 波起          加入突进者（蓄力冲锋，考横向走位）
            //   第 5 波起          加入孢裂体（死亡分裂，清场要补刀）
            for (int i = 0; i < total; i++)
            {
                float r = Random.value;
                EnemyDef def;

                if (wave >= 5 && r < 0.12f) def = EnemyCatalog.Splitter();
                else if (wave >= 4 && r < 0.30f) def = EnemyCatalog.Dasher();
                else if (wave >= 3 && r < 0.46f) def = EnemyCatalog.Ranged();
                else if (wave >= 2 && r < 0.62f) def = EnemyCatalog.Tank();
                else def = EnemyCatalog.Chaser();

                EnemyCatalog.ApplyWaveScaling(def, wave);
                _spawnQueue.Enqueue(def);
            }

            // 每波固定混入一只暴君作为"锚点"，让波次有节奏支点
            if (wave >= 2)
            {
                var anchor = EnemyCatalog.Tank();
                EnemyCatalog.ApplyWaveScaling(anchor, wave);
                anchor.Health *= 1.6f;
                _spawnQueue.Enqueue(anchor);
            }

            // 第 3 波起按概率混入精英：制造局部压力峰值，而不是整波均匀变强
            int elites = wave >= 9 ? 2 : (wave >= 6 ? 1 : 0);
            for (int i = 0; i < elites; i++)
            {
                var e = wave >= 4 && Random.value < 0.5f ? EnemyCatalog.Dasher() : EnemyCatalog.Chaser();
                EnemyCatalog.ApplyWaveScaling(e, wave);
                EnemyCatalog.MakeElite(e);
                _spawnQueue.Enqueue(e);
            }
        }

        void SpawnOne(EnemyDef def)
        {
            Vector3 pos;
            var arena = GameManager.I.Arena;

            if (def.IsBoss)
            {
                // 首领从场边出现，给玩家一点准备时间
                pos = arena != null ? arena.RandomSpawnPoint(arena.PlayRadius * 0.72f, arena.PlayRadius * 0.86f)
                    : new Vector3(0f, 0f, 16f);
                EventBus.Publish(new ToastEvent { Message = "首领降临", Color = Palette.Danger });
            }
            else
            {
                // 普通敌人：在玩家视野外的环形区域生成，避免"贴脸刷怪"
                var player = GameManager.I.Player;
                float minR, maxR;
                if (player != null)
                {
                    minR = 13f;
                    maxR = arena != null ? arena.PlayRadius * 0.96f : 26f;
                    if (maxR - minR < 4f) minR = Mathf.Max(6f, maxR - 6f);
                }
                else
                {
                    minR = 10f;
                    maxR = arena != null ? arena.PlayRadius * 0.9f : 24f;
                }
                pos = arena != null ? arena.RandomSpawnPoint(minR, maxR) : Random.insideUnitCircle.normalized * 18f;
            }

            pos.y = 0f;
            RegisterSpawned(Enemy.Spawn(def, pos));
        }

        /// <summary>注册一个敌人（外部召唤的也算，保证波次结算准确）。</summary>
        public void RegisterSpawned(Enemy e)
        {
            if (e != null) _alive.Add(e);
        }

        public void NotifyKilled(Enemy e)
        {
            _alive.Remove(e);
        }

        void CompleteWave()
        {
            WaveActive = false;
            EventBus.Publish(new WaveEvent { Wave = CurrentWave, TotalWaves = 0, IsBossWave = IsBossWave });

            // 每波结算奖励：回一点血 + 掉落回血包的概率
            var player = GameManager.I.Player;
            if (player != null && player.Alive)
            {
                player.Heal(player.Stats.MaxHealth * 0.06f);
                if (Random.value < 0.30f && GameManager.I.Arena != null)
                {
                    HealthPickup.Spawn(player.transform.position + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f)),
                        player.Stats.MaxHealth * 0.18f);
                }
            }

            RunState.AddScore(60 + CurrentWave * 15);

            _inIntermission = true;
            _intermissionTimer = IsBossWave ? 3.4f : 2.1f;

            EventBus.Publish(new ToastEvent
            {
                Message = "第 " + CurrentWave + " 波清除 · 准备下一波",
                Color = Palette.Good
            });
            if (Juice.I != null) Juice.I.PopText("WAVE CLEAR", Vector3.zero, Palette.Good, 2.2f, 1.4f);
        }
    }
}
