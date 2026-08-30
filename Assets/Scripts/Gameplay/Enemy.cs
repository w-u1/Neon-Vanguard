using UnityEngine;

namespace NeonVanguard
{
    /// <summary>
    /// 敌人。一个类里用状态 + 类型分支处理 4 种行为，
    /// 好处是共享同一套受击/死亡/掉落逻辑，避免复制粘贴出 bug。
    ///
    /// 关键反馈：受击会"缩一下" + 闪白 + 被推退，死亡会爆散并飘伤害数字。
    /// 这些细节决定了打起来"爽不爽"。
    /// </summary>
    public class Enemy : MonoBehaviour
    {
        public EnemyDef Def;
        public bool Alive { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }

        enum AiState { Spawning, Active, Dying }
        AiState _state;

        float _spawnTimer;
        float _fireTimer;
        float _telegraphTimer;
        float _burstTimer;
        float _chargeTimer;
        float _summonTimer;
        float _chargeTimeLeft;
        Vector3 _chargeDir;
        float _hitFlashTimer;
        float _hitScaleTimer;
        float _strafeDir = 1f;
        float _strafeTimer;
        float _wobblePhase;
        float _lifetime;
        bool _telegraphing;
        // 冲锋者（Dasher）专用状态
        float _dashTimer;
        float _dashWindupLeft;
        float _dashActiveLeft;
        Vector3 _dashDir;

        Transform _body;
        Transform _accent;
        Renderer _bodyRenderer;
        Renderer _accentRenderer;
        Material _bodyMat;
        Material _accentMat;
        Material _flashMat;
        Material _telegraphMat;
        CapsuleCollider _collider;

        const float SpawnDuration = 0.55f;

        // ------------------------------------------------------------------
        //  生成
        // ------------------------------------------------------------------

        public static Enemy Spawn(EnemyDef def, Vector3 position)
        {
            var go = new GameObject("Enemy_" + def.Name);
            go.transform.position = position;
            int layer = LayerMask.NameToLayer("Enemy");
            go.layer = layer >= 0 ? layer : 0;

            var e = go.AddComponent<Enemy>();
            e.Init(def);
            return e;
        }

        void Init(EnemyDef def)
        {
            Def = def;
            MaxHealth = def.Health;
            Health = def.Health;
            Alive = true;
            _state = AiState.Spawning;
            _spawnTimer = SpawnDuration;
            _lifetime = 0f;
            _fireTimer = Random.Range(0.4f, def.FireInterval);
            _burstTimer = def.BossBurstInterval;
            _chargeTimer = def.BossChargeInterval;
            _summonTimer = def.BossSummonInterval;
            _strafeDir = Random.value < 0.5f ? -1f : 1f;
            _strafeTimer = Random.Range(1.5f, 3.5f);
            _wobblePhase = Random.Range(0f, 10f);
            _dashTimer = def.DashInterval * Random.Range(0.6f, 1.1f);

            BuildVisual();
            BuildPhysics();

            // 出场动画：从 0 迅速弹到 1，同时短暂无敌，避免"贴脸生成瞬间被秒"
            transform.localScale = Vector3.one * 0.05f;
        }

        void BuildPhysics()
        {
            _collider = gameObject.AddComponent<CapsuleCollider>();
            _collider.radius = Def.Radius;
            _collider.height = Def.Radius * 2.2f + 0.6f;
            _collider.center = new Vector3(0f, Def.Radius + 0.2f, 0f);

            // 敌人之间需要互相推挤才不会完全重叠，所以用刚体；
            // 关掉重力、冻结旋转，靠 transform 驱动移动（简单可控）。
            var rb = gameObject.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
        }

        void BuildVisual()
        {
            _bodyMat = MeshLib.Emissive(Def.BodyColor, 1.5f, 0.3f);
            _flashMat = MeshLib.Unlit(Color.white);
            _telegraphMat = MeshLib.Unlit(Palette.Danger, false, true);

            // 优先使用 Kenney 模型（CC0），取不到则用程序化几何体兜底
            var model = !string.IsNullOrEmpty(Def.ModelName)
                ? ModelLibrary.Instantiate(Def.ModelName, transform, Def.ModelHeight, Def.BodyColor, 1.7f)
                : null;

            if (model != null)
            {
                _body = model.transform;
                _bodyRenderer = model.GetComponentInChildren<MeshRenderer>();
            }
            else
            {
                var shape = MeshLib.MakePrimitive(Def.Shape, "Body", transform,
                    new Vector3(0f, Def.Radius * 0.9f + 0.1f, 0f), Def.ShapeScale * (Def.Radius * 1.8f), _bodyMat);
                _body = shape.transform;
                _bodyRenderer = shape.GetComponent<MeshRenderer>();
            }

            // 亮点：用颜色区分类型，也让人一眼看清"哪个是远程"
            _accentMat = MeshLib.Emissive(Def.AccentColor, 2.6f);
            var accent = MeshLib.MakePrimitive(PrimitiveType.Sphere, "Accent", transform,
                new Vector3(0f, Def.Radius * 1.5f + 0.15f, 0f),
                Vector3.one * (Def.Radius * (Def.IsBoss ? 1.6f : 0.85f)), _accentMat);
            _accent = accent.transform;
            _accentRenderer = accent.GetComponent<MeshRenderer>();

            // 首领额外加一圈装饰环，突出体型
            if (Def.IsBoss)
            {
                var ringMat = MeshLib.Unlit(new Color(Def.AccentColor.r, Def.AccentColor.g, Def.AccentColor.b, 0.9f));
                MeshLib.MakeOverlayMesh(MeshLib.Ring, "BossRing", transform,
                    new Vector3(0f, 0.05f, 0f), Vector3.one * (Def.Radius * 1.35f), ringMat);
                var ringMat2 = MeshLib.Unlit(new Color(Def.BodyColor.r, Def.BodyColor.g, Def.BodyColor.b, 0.7f));
                MeshLib.MakeOverlayMesh(MeshLib.Ring, "BossRing2", transform,
                    new Vector3(0f, 0.03f, 0f), Vector3.one * (Def.Radius * 1.7f), ringMat2);
            }

            // 精英标记：地面加一圈琥珀色光环，让"这个更危险"一眼可辨
            if (Def.IsElite)
            {
                var eliteMat = MeshLib.Unlit(new Color(1f, 0.78f, 0.25f, 0.85f));
                MeshLib.MakeOverlayMesh(MeshLib.Ring, "EliteRing", transform,
                    new Vector3(0f, 0.04f, 0f), Vector3.one * (Def.Radius * 1.5f), eliteMat);
            }
        }

        // ------------------------------------------------------------------
        //  每帧
        // ------------------------------------------------------------------

        void Update()
        {
            if (!Alive) return;
            float dt = Time.deltaTime;
            if (GameManager.I == null || GameManager.I.State == GameState.Paused) return;

            _lifetime += dt;
            UpdateTimers(dt);
            UpdateVisualFeedback(dt);

            switch (_state)
            {
                case AiState.Spawning:
                    UpdateSpawning(dt);
                    break;
                case AiState.Active:
                    UpdateBehaviour(dt);
                    break;
            }
        }

        void UpdateTimers(float dt)
        {
            if (_hitFlashTimer > 0f) _hitFlashTimer -= dt;
            if (_hitScaleTimer > 0f) _hitScaleTimer -= dt;
            _strafeTimer -= dt;
            if (_strafeTimer <= 0f)
            {
                _strafeDir *= -1f;
                _strafeTimer = Random.Range(1.4f, 3.2f);
            }
        }

        void UpdateSpawning(float dt)
        {
            _spawnTimer -= dt;
            float k = 1f - Mathf.Clamp01(_spawnTimer / SpawnDuration);
            // 轻微的过冲（Back Out），出场更有力度
            float s = 1f + 0.35f * Mathf.Sin(k * Mathf.PI) * (1f - k);
            transform.localScale = Vector3.one * Mathf.Lerp(0.05f, s, k);
            if (_spawnTimer <= 0f)
            {
                _state = AiState.Active;
                transform.localScale = Vector3.one;
            }
        }

        void UpdateVisualFeedback(float dt)
        {
            if (_bodyRenderer == null) return;

            // 受击闪白
            _bodyRenderer.sharedMaterial = _hitFlashTimer > 0f ? _flashMat : _bodyMat;

            // 受击缩放（"打中是有反馈的"）
            float target = _hitScaleTimer > 0f ? 0.86f : 1f;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * target,
                1f - Mathf.Exp(-22f * dt));

            // 远程敌人蓄力时闪烁红色，给玩家反应时间
            if (_accentRenderer != null)
            {
                if (!_accentRenderer.enabled) _accentRenderer.enabled = true;
                bool blink = _telegraphing && Mathf.Repeat(Time.time, 0.14f) <= 0.07f;
                var want = blink ? _telegraphMat : _accentMat;
                if (_accentRenderer.sharedMaterial != want) _accentRenderer.sharedMaterial = want;
            }

            // 首领的光环自转
            if (Def.IsBoss) _accent.Rotate(0f, 60f * dt, 0f, Space.Self);
        }

        void UpdateBehaviour(float dt)
        {
            var player = GameManager.I.Player;
            if (player == null || !player.Alive)
            {
                Wander(dt);
                return;
            }

            Vector3 toPlayer = player.transform.position - transform.position;
            toPlayer.y = 0f;
            float dist = toPlayer.magnitude;
            Vector3 dir = dist > 0.001f ? toPlayer / dist : Vector3.forward;

            switch (Def.Kind)
            {
                case EnemyKind.Chaser: BehaviourChaser(dt, dir, dist); break;
                case EnemyKind.Tank: BehaviourTank(dt, dir, dist); break;
                case EnemyKind.Ranged: BehaviourRanged(dt, dir, dist); break;
                case EnemyKind.Boss: BehaviourBoss(dt, dir, dist, player); break;
                case EnemyKind.Dasher: BehaviourDasher(dt, dir, dist); break;
                case EnemyKind.Splitter: BehaviourSplitter(dt, dir, dist); break;
            }

            Face(dir, dt);
            ClampToArena();
        }

        // ---------- 各类型行为 ----------

        void BehaviourChaser(float dt, Vector3 dir, float dist)
        {
            // 侧向摆动让一群追猎者不会挤成一条线，视觉上更像"群"
            float wobble = Mathf.Sin(_lifetime * 3.4f + _wobblePhase) * 0.28f;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            Vector3 vel = (dir + side * wobble).normalized * Def.Speed;
            transform.position += vel * dt;
        }

        void BehaviourTank(float dt, Vector3 dir, float dist)
        {
            // 距离越近越"加速撞击"，制造压迫感
            float speed = Def.Speed * (dist < 6f ? 1.55f : 1f);
            transform.position += dir * (speed * dt);
        }

        void BehaviourRanged(float dt, Vector3 dir, float dist)
        {
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            Vector3 vel = Vector3.zero;

            // 保持理想射程：太近后退、太远靠近，中间距离左右横移
            if (dist < Def.PreferredRange - 2.5f) vel -= dir * Def.Speed;
            else if (dist > Def.PreferredRange + 2.5f) vel += dir * Def.Speed;
            vel += side * (_strafeDir * Def.Speed * 0.75f);

            transform.position += vel * dt;

            // 射击循环：先预警（闪烁红），再开火
            _fireTimer -= dt;
            if (_fireTimer <= Def.TelegraphTime && !_telegraphing && _fireTimer > 0f)
            {
                _telegraphing = true;
            }
            if (_fireTimer <= 0f)
            {
                _telegraphing = false;
                _fireTimer = Def.FireInterval;
                FireAtPlayer(dir, Def.BulletDamage, Def.BulletSpeed);
            }
        }

        void BehaviourBoss(float dt, Vector3 dir, float dist, Player player)
        {
            // 冲撞中
            if (_chargeTimeLeft > 0f)
            {
                _chargeTimeLeft -= dt;
                transform.position += _chargeDir * (Def.Speed * 6.5f * dt);
                return;
            }

            // 缓慢逼近
            transform.position += dir * (Def.Speed * dt);

            _burstTimer -= dt;
            _chargeTimer -= dt;
            _summonTimer -= dt;

            if (_chargeTimer <= 0f)
            {
                _chargeTimer = Def.BossChargeInterval;
                _chargeTimeLeft = 0.62f;
                _chargeDir = dir;
                BeginTelegraph(0.55f);
                AudioKit2.PlayUi(AudioKit.BossWarn(), 0.35f);
                Juice.I.Shake(0.25f);
                return;
            }

            if (_burstTimer <= 0f)
            {
                _burstTimer = Def.BossBurstInterval;
                RadialBurst(Def.BossBurstCount, Def.BulletDamage * 0.85f, Def.BulletSpeed * 0.8f);
            }

            if (_summonTimer <= 0f)
            {
                _summonTimer = Def.BossSummonInterval;
                SummonMinions();
            }
        }

        /// <summary>
        /// 冲锋者：缓慢逼近 → 蓄力预警 → 高速突进。
        /// 蓄力阶段会闪烁并短暂停住，给玩家足够的反应窗口；突进结束进入冷却。
        /// 这是"读招 + 横向闪避"的节奏型敌人，和追猎者的持续压迫形成对比。
        /// </summary>
        void BehaviourDasher(float dt, Vector3 dir, float dist)
        {
            // 突进中
            if (_dashActiveLeft > 0f)
            {
                _dashActiveLeft -= dt;
                transform.position += _dashDir * (Def.DashSpeed * dt);
                return;
            }

            // 蓄力中：原地微退，让玩家看清方向
            if (_dashWindupLeft > 0f)
            {
                _dashWindupLeft -= dt;
                _telegraphing = true;
                transform.position -= dir * (Def.Speed * 0.45f * dt);
                if (_dashWindupLeft <= 0f)
                {
                    _telegraphing = false;
                    _dashActiveLeft = Def.DashDuration;
                    _dashDir = dir;
                    if (Juice.I != null) Juice.I.Shake(0.05f);
                }
                return;
            }

            // 冷却中缓慢逼近
            transform.position += dir * (Def.Speed * dt);

            _dashTimer -= dt;
            // 距离太远不冲，避免冲进空气
            if (_dashTimer <= 0f && dist < 16f)
            {
                _dashTimer = Def.DashInterval;
                _dashWindupLeft = Def.DashWindup;
            }
        }

        /// <summary>分裂体：缓慢逼近，威胁来自死亡时的分裂，而不是它本身。</summary>
        void BehaviourSplitter(float dt, Vector3 dir, float dist)
        {
            float wobble = Mathf.Sin(_lifetime * 2.2f + _wobblePhase) * 0.18f;
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            transform.position += (dir + side * wobble).normalized * (Def.Speed * dt);
        }

        void Wander(float dt)        {
            float a = Time.time * 0.6f + _wobblePhase;
            transform.position += new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (Def.Speed * 0.4f * dt);
        }

        void Face(Vector3 dir, float dt)
        {
            if (_body == null) return;
            if (dir.sqrMagnitude < 0.0001f) return;
            _body.rotation = Quaternion.Slerp(_body.rotation,
                Quaternion.LookRotation(dir, Vector3.up), 1f - Mathf.Exp(-Def.TurnRate * dt));
        }

        void ClampToArena()
        {
            var arena = GameManager.I != null ? GameManager.I.Arena : null;
            if (arena != null) transform.position = arena.Clamp(transform.position);
        }

        // ---------- 攻击 ----------

        void BeginTelegraph(float time)
        {
            _telegraphing = true;
            CancelInvoke(nameof(EndTelegraph));
            Invoke(nameof(EndTelegraph), time);
        }

        void EndTelegraph() { _telegraphing = false; }

        void FireAtPlayer(Vector3 dir, float damage, float speed)
        {
            var origin = transform.position + dir * (Def.Radius + 0.5f) + Vector3.up * 0.7f;
            var go = Pool.Spawn("bullet_enemy", EnemyBulletFactory, origin, Quaternion.identity);
            go.GetComponent<Bullet>().Launch(origin, dir, damage, speed, 3.2f,
                Def.IsBoss ? 0.34f : 0.26f, 0, 2f, false, BulletTeam.Enemy,
                MeshLib.Unlit(new Color(1f, 0.35f, 0.55f), true, true));
            AudioKit2.PlayUi(AudioKit.EnemyShoot(), 0.35f);
        }

        void RadialBurst(int count, float damage, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (i / (float)count) * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var origin = transform.position + dir * (Def.Radius + 0.4f) + Vector3.up * 0.7f;
                var go = Pool.Spawn("bullet_enemy", EnemyBulletFactory, origin, Quaternion.identity);
                go.GetComponent<Bullet>().Launch(origin, dir, damage, speed, 4f, 0.32f, 0,
                    1.5f, false, BulletTeam.Enemy,
                    MeshLib.Unlit(new Color(1f, 0.45f, 0.2f), true, true));
            }
            AudioKit2.PlayUi(AudioKit.Shotgun(), 0.55f);
            if (Juice.I != null) Juice.I.Shake(0.3f);
        }

        void SummonMinions()
        {
            if (GameManager.I == null || GameManager.I.Waves == null) return;
            int n = 3 + RunState.Wave / 3;
            for (int i = 0; i < n; i++)
            {
                var def = EnemyCatalog.Chaser();
                EnemyCatalog.ApplyWaveScaling(def, RunState.Wave);
                def.Health *= 0.6f;
                var pos = transform.position + new Vector3(
                    Random.Range(-3.5f, 3.5f), 0f, Random.Range(-3.5f, 3.5f));
                GameManager.I.Waves.RegisterSpawned(Enemy.Spawn(def, pos));
            }
            if (Juice.I != null)
            {
                Juice.I.PopText("召唤！", transform.position + Vector3.up * 3f, Palette.EnemyChaser, 1.4f, 1f);
                Juice.I.Shake(0.2f);
            }
        }

        static GameObject EnemyBulletFactory()
        {
            var go = new GameObject("EnemyBullet");
            int layer = LayerMask.NameToLayer("EnemyBullet");
            go.layer = layer >= 0 ? layer : 0;
            go.AddComponent<Bullet>();
            return go;
        }

        // ------------------------------------------------------------------
        //  受伤 / 死亡
        // ------------------------------------------------------------------

        /// <summary>
        /// 受击。damageOverTime=true 时使用持续伤害路径（不触发击退/闪白，避免每帧刷屏）。
        /// </summary>
        public void TakeDamage(float amount, Vector3 hitPoint, Vector3 dir, bool crit,
            float knockback, bool damageOverTime = false)
        {
            if (!Alive) return;

            float mitigated = amount * (1f - Def.Armor);
            Health -= mitigated;

            if (!damageOverTime)
            {
                _hitFlashTimer = 0.075f;
                _hitScaleTimer = 0.09f;
            }

            // 击退：质量越大抗性越高；首领几乎免疫
            if (knockback > 0.01f && Def.Mass < 30f)
            {
                Vector3 push = dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward;
                transform.position += push * (knockback / Def.Mass);
            }

            if (!damageOverTime && Juice.I != null)
            {
                Juice.I.PopDamage(mitigated, hitPoint + Vector3.up * 0.4f, crit);
            }

            if (Health <= 0f) Die(hitPoint);
        }

        void Die(Vector3 hitPoint)
        {
            if (!Alive) return;
            Alive = false;
            _state = AiState.Dying;

            AudioKit2.PlayUi(AudioKit.Explode(), Def.IsBoss ? 1f : 0.42f);

            // 掉落经验
            int orbs = Mathf.Clamp(Def.XpDrop, 1, 10);
            for (int i = 0; i < orbs; i++)
            {
                Vector2 c = Random.insideUnitCircle * (Def.Radius + 0.4f);
                XpOrb.Spawn(transform.position + new Vector3(c.x, 0.4f, c.y),
                    Mathf.Max(1, Def.XpDrop / orbs));
            }

            // 分裂体的死亡分裂：在尸体周围炸出几只小型追猎者
            if (Def.SplitCount > 0 && GameManager.I != null && GameManager.I.Waves != null)
            {
                for (int i = 0; i < Def.SplitCount; i++)
                {
                    float a = (i / (float)Def.SplitCount) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                    var offset = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (Def.Radius + 0.6f);

                    var child = EnemyCatalog.Chaser();
                    child.Health *= Def.SplitHealthScale * (1f + RunState.Wave * 0.08f);
                    child.Speed *= 1.25f;
                    child.ContactDamage *= 0.55f;
                    child.Score = Mathf.RoundToInt(child.Score * 0.4f);
                    child.XpDrop = 1;
                    child.BodyColor = Def.BodyColor;
                    child.ModelHeight *= 0.7f;

                    GameManager.I.Waves.RegisterSpawned(
                        Enemy.Spawn(child, transform.position + offset));
                }
                if (Juice.I != null)
                    Juice.I.PopText("分裂！", transform.position + Vector3.up * 1.6f,
                        Def.BodyColor, 1.1f, 0.8f);
                SpawnFx.Burst(transform.position + Vector3.up * 0.5f, Def.BodyColor, 16, 13f, 0.55f);
            }

            // 特效与反馈
            SpawnFx.Burst(transform.position + Vector3.up * 0.5f, Def.BodyColor, Def.IsBoss ? 44 : 12,
                Def.IsBoss ? 18f : 10f, Def.IsBoss ? 1.1f : 0.45f);

            if (GameManager.I != null)
            {
                GameManager.I.AddKill(Def.Score);
                GameManager.I.Shake(Def.IsBoss ? 1.0f : 0.12f);
                if (Def.IsBoss) Juice.I.HitStop(0.35f, 0.05f);
            }

            if (GameManager.I != null && GameManager.I.Waves != null)
            {
                GameManager.I.Waves.NotifyKilled(this);
            }

            EventBus.Publish(new EnemyKilledEvent
            {
                Position = transform.position,
                Score = Def.Score,
                IsBoss = Def.IsBoss,
                Color = Def.BodyColor
            });

            if (Def.IsBoss)
            {
                if (Juice.I != null)
                {
                    Juice.I.PopText("首领已击破", transform.position + Vector3.up * 4f, Palette.Warning, 2.2f, 1.8f);
                }
                EventBus.Publish(new ToastEvent { Message = "首领已击破！", Color = Palette.Warning });
            }

            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (_bodyMat != null) Destroy(_bodyMat);
            if (_accentMat != null) Destroy(_accentMat);
            if (_flashMat != null) Destroy(_flashMat);
            if (_telegraphMat != null) Destroy(_telegraphMat);
        }
    }
}
