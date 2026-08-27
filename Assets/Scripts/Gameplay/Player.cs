using UnityEngine;

namespace NeonVanguard
{
    /// <summary>
    /// 玩家角色。操作方案：WASD 移动 + 鼠标瞄准 + 左键射击 + Shift/空格冲刺 + 1/2/3 或滚轮换武器。
    ///
    /// 手感设计要点（这些是"可玩性"的真正来源，比堆数值重要）：
    ///  1) 冲刺给无敌帧 + 穿透伤害，让"走位"本身成为输出手段，而不是单纯逃命；
    ///  2) 射击带后坐力把玩家往后推一点，连射时会有明显的"压枪"体感；
    ///  3) 换弹/冷却用视觉+音效双重反馈，玩家永远知道"现在能不能打"。
    /// </summary>
    public class Player : MonoBehaviour
    {
        public PlayerStats Stats = new PlayerStats();
        public WeaponKind CurrentWeapon = WeaponKind.Pistol;

        public bool Alive { get; private set; } = true;

        /// <summary>无敌（仅自检使用：避免长时间自动对局提前死亡，影响统计稳定性）。</summary>
        public bool Invincible;
        public float Health { get; private set; }
        public float HealthNormalized { get { return Stats.MaxHealth <= 0f ? 0f : Mathf.Clamp01(Health / Stats.MaxHealth); } }
        public float DashCooldownNormalized
        {
            get
            {
                float cd = EffectiveDashCooldown;
                if (cd <= 0f) return 1f;
                // 注意方向：_dashTimer 是"已经充了多久"，
                // 所以进度 = 已充时间 / 总冷却。不要写成 1 - timer/cd（那是"还剩多少"）。
                return Mathf.Clamp01(_dashTimer / cd);
            }
        }

        public int DashCharges { get { return _dashCharges; } }
        public int MaxDashCharges { get { return 1 + Stats.ExtraDashes; } }

        // ---- 运行时状态 ----
        float _fireTimer;
        float _dashTimer;
        float _dashTimeLeft;
        Vector3 _dashDir;
        int _dashCharges;
        float _invulnTimer;
        float _regenBuffer;
        float _novaTimer;
        int _pickupChain;
        float _pickupChainTimer;
        Vector3 _aimDir = Vector3.forward;
        Vector3 _facing = Vector3.forward;
        float _moveAnimPhase;

        // ---- 视觉 ----
        Transform _body;
        Transform _ring;
        Renderer _bodyRenderer;
        Material _bodyMat;
        Material _ringMat;
        Material _shieldMat;
        GameObject _shieldGo;
        Transform _gunPivot;
        Camera _cam;
        Transform _camRig;

        SphereCollider _trigger;
        const float BodyRadius = 0.55f;
        readonly System.Collections.Generic.HashSet<Enemy> _dashHit = new System.Collections.Generic.HashSet<Enemy>();

        // ------------------------------------------------------------------
        //  构建
        // ------------------------------------------------------------------

        public void Build()
        {
            Health = Stats.MaxHealth;
            Alive = true;

            // 身体：优先用 Kenney 的宇航员模型（CC0），取不到则用程序化胶囊兜底
            _bodyMat = MeshLib.Emissive(Palette.Player, 1.8f, 0.55f);
            _ringMat = MeshLib.Unlit(Palette.Player * 1.1f, true, true);
            _shieldMat = MeshLib.Unlit(new Color(Palette.Shield.r, Palette.Shield.g, Palette.Shield.b, 0.30f), false, true);

            var model = ModelLibrary.Instantiate("astronautA", transform, 1.35f, Palette.Player, 1.9f);
            if (model != null)
            {
                _body = model.transform;
                _bodyRenderer = model.GetComponentInChildren<MeshRenderer>();
            }
            else
            {
                var bodyGo = MeshLib.MakePrimitive(PrimitiveType.Capsule, "Body", transform,
                    new Vector3(0f, 0.62f, 0f), new Vector3(0.92f, 0.55f, 0.92f), _bodyMat);
                _body = bodyGo.transform;
                _bodyRenderer = bodyGo.GetComponent<MeshRenderer>();
            }

            // 内核：亮到发白的小球，提供"能量核心"的视觉焦点
            var coreMat = MeshLib.Emissive(Palette.PlayerCore, 3f);
            MeshLib.MakePrimitive(PrimitiveType.Sphere, "Core", transform,
                new Vector3(0f, 0.72f, 0f), Vector3.one * 0.42f, coreMat);

            // 底部光环：始终贴地，让玩家位置在任何背景上都清晰
            var ringGo = MeshLib.MakeOverlayMesh(MeshLib.Ring, "Ring", transform,
                new Vector3(0f, 0.04f, 0f), Vector3.one * 1.0f, _ringMat);
            _ring = ringGo.transform;

            // 护盾球：只在受击/无敌帧期间可见
            var shieldSphere = MeshLib.MakePrimitive(PrimitiveType.Sphere, "Shield", transform,
                new Vector3(0f, 0.75f, 0f), Vector3.one * 2.0f, _shieldMat);
            _shieldGo = shieldSphere;
            _shieldGo.SetActive(false);

            // 瞄准指示器：一个短小的箭头，帮助玩家判断朝向
            _gunPivot = new GameObject("GunPivot").transform;
            _gunPivot.SetParent(transform, false);
            var barrelMat = MeshLib.Emissive(new Color(0.9f, 0.98f, 1f), 2.2f);
            var barrel = MeshLib.MakePrimitive(PrimitiveType.Cube, "Barrel", _gunPivot,
                new Vector3(0f, 0.68f, 0.62f), new Vector3(0.16f, 0.16f, 0.9f), barrelMat);

            // 触发器：用于拾取经验球和接收敌人接触伤害
            _trigger = gameObject.AddComponent<SphereCollider>();
            _trigger.radius = BodyRadius * 1.9f;
            _trigger.isTrigger = true;
            _trigger.center = new Vector3(0f, 0.6f, 0f);

            gameObject.layer = LayerMask.NameToLayer("Player") >= 0
                ? LayerMask.NameToLayer("Player") : 0;

            _dashCharges = MaxDashCharges;
        }

        public void AttachCameraRig(Transform rig, Camera cam)
        {
            _camRig = rig;
            _cam = cam;
            if (_camRig != null)
            {
                _camRig.position = new Vector3(transform.position.x, 0f, transform.position.z);
            }
        }

        // ------------------------------------------------------------------
        //  生命周期
        // ------------------------------------------------------------------

        public void ResetForNewRun()
        {
            Stats = new PlayerStats();
            CurrentWeapon = WeaponKind.Pistol;
            Health = Stats.MaxHealth;
            Alive = true;
            _fireTimer = 0f;
            _dashTimer = 0f;
            _dashTimeLeft = 0f;
            _dashCharges = MaxDashCharges;
            _dashTimer = EffectiveDashCooldown;
            _invulnTimer = 1.2f;
            _novaTimer = 0f;
            _pickupChain = 0;
            transform.position = Vector3.zero;
            gameObject.SetActive(true);

            if (_body != null) _body.gameObject.SetActive(true);
            if (_ring != null) _ring.gameObject.SetActive(true);
            if (_shieldGo != null) _shieldGo.SetActive(false);
        }

        void Update()
        {
            if (GameManager.I == null) return;

            var state = GameManager.I.State;
            bool canAct = state == GameState.Playing && Alive;

            HandleCooldowns(Time.deltaTime);
            UpdateInput(canAct);

            if (canAct)
            {
                UpdateAim();
                UpdateMovement();
                UpdateWeapon();
            }
            else
            {
                // 非战斗状态也保持瞄准朝向，避免看着"卡住"
                UpdateAim();
            }

            UpdateVisuals(Time.deltaTime);
            UpdateCamera(Time.deltaTime);
            UpdateAura(Time.deltaTime, canAct);
            UpdatePickupChain(Time.deltaTime);
        }

        void HandleCooldowns(float dt)
        {
            if (_fireTimer > 0f) _fireTimer -= dt;
            if (_invulnTimer > 0f) _invulnTimer -= dt;
            if (_novaTimer > 0f) _novaTimer -= dt;

            // 冲刺充能：独立恢复，支持多段冲刺。
            // 语义约定（很重要，HUD 的冷却条依赖它）：
            //   _dashTimer = 0                      → 刚刚消耗掉一层，正在从零充能
            //   _dashTimer = EffectiveDashCooldown  → 下一层已充满
            // 因此 DashCooldownNormalized = _dashTimer / cd 就是"这一层的充能进度"，
            // HUD 用 (已就绪层数 + 该进度) / 总层数 得到整条进度。
            // 早期版本把这个比例写反（用 1 - timer/cd），导致条子方向与冷却相反：
            // 满充能时显示空、刚用掉反而显示满 —— 这个 bug 是靠自检的逐帧采样抓出来的。
            if (_dashCharges < MaxDashCharges)
            {
                _dashTimer += dt;
                if (_dashTimer >= EffectiveDashCooldown)
                {
                    _dashTimer = EffectiveDashCooldown;
                    _dashCharges++;
                }
            }

            // 生命回复
            if (Stats.RegenPerSecond > 0f && Alive && Health < Stats.MaxHealth)
            {
                _regenBuffer += Stats.RegenPerSecond * dt;
                if (_regenBuffer >= 1f)
                {
                    float heal = Mathf.Floor(_regenBuffer);
                    _regenBuffer -= heal;
                    Health = Mathf.Min(Stats.MaxHealth, Health + heal);
                }
            }
        }

        float EffectiveDashCooldown
        {
            get { return Mathf.Max(0.15f, Stats.DashCooldown / (1f + Stats.ExtraDashes * 0.15f)); }
        }

        // ------------------------------------------------------------------
        //  输入
        // ------------------------------------------------------------------

        void UpdateInput(bool canAct)
        {
            if (!canAct) return;

            // 武器切换
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SwitchWeapon(0);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SwitchWeapon(1);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SwitchWeapon(2);
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                int dir = scroll > 0f ? 1 : -1;
                int idx = System.Array.IndexOf(WeaponLibrary.Order, CurrentWeapon);
                SwitchWeapon(idx + dir);
            }

            // 冲刺
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)
                || Input.GetKeyDown(KeyCode.Space))
            {
                TryDash();
            }
        }

        public void SwitchWeapon(int index)
        {
            int n = WeaponLibrary.Order.Length;
            index = ((index % n) + n) % n;
            var next = WeaponLibrary.Order[index];
            if (next == CurrentWeapon) return;
            CurrentWeapon = next;
            var def = WeaponLibrary.Get(CurrentWeapon);
            _fireTimer = Mathf.Max(_fireTimer, 0.10f);   // 切枪小硬直
            AudioKit2.PlayUi(AudioKit.Select(), 0.5f);
            if (Juice.I != null)
            {
                Juice.I.PopText(def.Name, transform.position + Vector3.up * 1.8f, def.Tint, 0.85f, 0.7f);
            }
            EventBus.Publish(new ToastEvent { Message = def.Name, Color = def.Tint });
        }

        // ------------------------------------------------------------------
        //  移动
        // ------------------------------------------------------------------

        void ReadMoveInput(out Vector2 input)
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            input = new Vector2(h, v);
            if (input.sqrMagnitude > 1f) input.Normalize();
        }

        void UpdateMovement()
        {
            var dt = Time.deltaTime;

            if (_dashTimeLeft > 0f)
            {
                // 冲刺期间：恒定速度直线冲，忽略普通输入
                _dashTimeLeft -= dt;
                float speed = Stats.DashSpeed;
                transform.position += _dashDir * (speed * dt);

                // 冲刺伤害：路径上的敌人吃一次伤害
                if (Stats.DashDamage > 0f)
                {
                    var hits = Physics.OverlapSphere(transform.position, 1.5f, ~0, QueryTriggerInteraction.Collide);
                    for (int i = 0; i < hits.Length; i++)
                    {
                        var e = hits[i].GetComponentInParent<Enemy>();
                        if (e != null && e.Alive && !_dashHit.Contains(e))
                        {
                            _dashHit.Add(e);
                            e.TakeDamage(Stats.DashDamage, e.transform.position, _dashDir, false, 12f);
                        }
                    }
                }

                if (_dashTimeLeft <= 0f) _dashHit.Clear();
            }
            else
            {
                Vector2 input;
                ReadMoveInput(out input);
                Vector3 move = new Vector3(input.x, 0f, input.y);
                transform.position += move * (Stats.MoveSpeed * dt);
                _moveAnimPhase += move.magnitude * dt * 9f;
            }

            // 边界约束
            var arena = GameManager.I != null ? GameManager.I.Arena : null;
            if (arena != null) transform.position = arena.Clamp(transform.position);
        }

        public void TryDash()
        {
            if (_dashCharges <= 0) return;

            Vector2 input;
            ReadMoveInput(out input);
            Vector3 dir = new Vector3(input.x, 0f, input.y);
            if (dir.sqrMagnitude < 0.01f) dir = _facing;      // 没输入就朝当前朝向冲
            dir.y = 0f;
            _dashDir = dir.normalized;

            _dashCharges--;
            _dashTimer = 0f;                  // 从零开始充能（HUD 冷却条据此从空涨到满）
            _dashTimeLeft = Stats.DashDuration;
            _invulnTimer = Mathf.Max(_invulnTimer, Stats.DashDuration + 0.08f);
            _dashHit.Clear();

            AudioKit2.PlayUi(AudioKit.Dash(), 0.7f);
            if (Juice.I != null) Juice.I.Shake(0.16f);
            SpawnFx.Burst(transform.position, Palette.Player, 10, 11f, 0.32f);

            // 冲刺新星：解锁后每次冲刺都对周围造成一次范围伤害
            if (Stats.NovaRadius > 0f && _novaTimer <= 0f)
            {
                _novaTimer = Stats.NovaCooldown;
                NovaBlast();
            }
        }

        void NovaBlast()
        {
            float r = Stats.NovaRadius;
            var hits = Physics.OverlapSphere(transform.position, r, ~0, QueryTriggerInteraction.Collide);

            // 可视化：一圈快速扩散的光环
            NovaVisual.Spawn(transform.position, r, Palette.Nova);

            for (int i = 0; i < hits.Length; i++)
            {
                var e = hits[i].GetComponentInParent<Enemy>();
                if (e != null && e.Alive)
                {
                    Vector3 d = e.transform.position - transform.position;
                    d.y = 0f;
                    e.TakeDamage(Stats.DashDamage + 18f, e.transform.position, d.normalized, true, 18f);
                }
            }

            AudioKit2.PlayUi(AudioKit.Nova(), 0.75f);
            if (Juice.I != null) Juice.I.Shake(0.42f);
        }

        // ------------------------------------------------------------------
        //  瞄准与射击
        // ------------------------------------------------------------------

        void UpdateAim()
        {
            if (SelfTest.Enabled) return;   // 自检模式由 SelfTest 直接指定瞄准方向
            if (_cam == null) return;

            // 用一条水平面射线把鼠标位置投到世界中，得到真实的瞄准方向
            Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, 0.7f, 0f));
            float enter;
            if (plane.Raycast(ray, out enter))
            {
                Vector3 world = ray.GetPoint(enter);
                Vector3 d = world - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.0004f) _aimDir = d.normalized;
            }
            _facing = _aimDir;
        }

        /// <summary>供无头自检使用的外部瞄准（无头模式下没有鼠标位置可读）。</summary>
        public void SetAimOverride(Vector3 aimDir)
        {
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude > 0.0001f) _aimDir = aimDir.normalized;
        }

        void UpdateWeapon()
        {
            var def = WeaponLibrary.Get(CurrentWeapon);
            float cooldown = def.Cooldown / Mathf.Max(0.05f, Stats.FireRateMult);

            bool wantsFire = def.AutoFire ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0);
            // 自检模式下自动开火，用于无头集成测试
            if (SelfTest.Enabled) wantsFire = true;
            if (!wantsFire || _fireTimer > 0f) return;

            _fireTimer = cooldown;
            Fire(def);
        }

        void Fire(WeaponDef def)
        {
            int pellets = def.Pellets + Stats.ExtraProjectiles;

            float speed = def.Speed * Stats.BulletSpeedMult;
            int pierce = def.Pierce + Stats.ExtraPierce;
            var mat = BulletMaterial(def);

            for (int i = 0; i < pellets; i++)
            {
                float spread;
                if (pellets > 1 && def.Pellets > 1)
                {
                    // 霰弹：均匀铺开 + 少量随机抖动，弹道分布可控
                    float t = pellets == 1 ? 0.5f : i / (float)(pellets - 1);
                    spread = Mathf.Lerp(-def.SpreadDeg, def.SpreadDeg, t) + Random.Range(-3f, 3f);
                }
                else if (pellets > 1)
                {
                    spread = Mathf.Lerp(-9f, 9f, i / (float)(pellets - 1)) * 0.6f + Random.Range(-2f, 2f);
                }
                else
                {
                    spread = Random.Range(-def.SpreadDeg, def.SpreadDeg);
                }

                Vector3 dir = Quaternion.Euler(0f, spread, 0f) * _aimDir;
                bool crit = Random.value < Stats.CritChance;
                float dmg = def.Damage * Stats.DamageMult * (crit ? Stats.CritMult : 1f);

                var muzzle = transform.position + _aimDir * 0.85f + Vector3.up * 0.68f;
                var go = Pool.Spawn("bullet_player", PlayerBulletFactory, muzzle, Quaternion.identity);
                go.GetComponent<Bullet>().Launch(muzzle, dir, dmg, speed, def.Life,
                    def.Size * Stats.BulletSizeMult, pierce, def.Knockback, crit,
                    BulletTeam.Player, mat, Stats.ExplosiveChance,
                    def.Kind == WeaponKind.Railgun ? 900f : 0f);
            }

            // 后坐力：把玩家轻轻往后推，重型武器推得更明显
            transform.position -= _aimDir * (def.Recoil * 0.055f);

            // 反馈
            float shake = def.Kind == WeaponKind.Shotgun ? 0.20f
                : def.Kind == WeaponKind.Railgun ? 0.30f : 0.07f;
            if (Juice.I != null) Juice.I.Shake(shake);

            AudioClip clip = def.Kind == WeaponKind.Shotgun ? AudioKit.Shotgun()
                : def.Kind == WeaponKind.Railgun ? AudioKit.Railgun() : AudioKit.Shoot();
            AudioKit2.PlayUi(clip, def.Kind == WeaponKind.Pistol ? 0.5f : 0.75f);

            var muzzlePos = transform.position + _aimDir * 0.9f + Vector3.up * 0.7f;
            SpawnFx.Spray(muzzlePos, _aimDir, def.Tint, def.Kind == WeaponKind.Shotgun ? 8 : 3, 8f, 0.18f);

            if (def.Kind == WeaponKind.Railgun && Juice.I != null)
            {
                Juice.I.HitStop(0.045f);
            }
        }

        static GameObject PlayerBulletFactory()
        {
            var go = new GameObject("PlayerBullet");
            go.layer = LayerMask.NameToLayer("PlayerBullet") >= 0
                ? LayerMask.NameToLayer("PlayerBullet") : 0;
            go.AddComponent<Bullet>();
            return go;
        }

        // 每种武器的子弹材质只建一次：连射时每秒会开火 7 次以上，
        // 每发都 new Material 会造成持续的材质泄漏。
        static readonly System.Collections.Generic.Dictionary<WeaponKind, Material> BulletMats =
            new System.Collections.Generic.Dictionary<WeaponKind, Material>();

        static Material BulletMaterial(WeaponDef def)
        {
            Material m;
            if (BulletMats.TryGetValue(def.Kind, out m) && m != null) return m;
            m = MeshLib.Unlit(def.Tint, true, true);
            BulletMats[def.Kind] = m;
            return m;
        }

        // ------------------------------------------------------------------
        //  受伤 / 拾取
        // ------------------------------------------------------------------

        /// <summary>前置条件，仅用于自检诊断。</summary>
        public bool DamageBlockedByInvuln { get { return _invulnTimer > 0f; } }

        // ---- 以下三个仅供自检驱动状态，不参与正常玩法 ----

        /// <summary>直接设置生命值（绕过无敌帧/减伤），用于验证血条动画。</summary>
        public void DebugSetHealth(float value)
        {
            Health = Mathf.Clamp(value, 0f, Stats.MaxHealth);
        }

        /// <summary>把冲刺充能补满并清空冷却计时。</summary>
        public void DebugResetDash()
        {
            _dashCharges = MaxDashCharges;
            _dashTimer = EffectiveDashCooldown;
        }

        /// <summary>消耗一层冲刺，用于验证冷却条从空涨到满。</summary>
        public void DebugConsumeDash()
        {
            if (_dashCharges > 0) _dashCharges--;
            _dashTimer = 0f;
        }

        public void TakeDamage(float amount, Vector3 from)
        {
            if (!Alive || _invulnTimer > 0f) return;
            if (Invincible) return;

            float mitigated = amount * (1f - Stats.Armor);
            Health -= mitigated;
            _invulnTimer = 0.42f;

            AudioKit2.PlayUi(AudioKit.Hurt(), 0.85f);
            if (Juice.I != null)
            {
                Juice.I.Shake(0.34f);
                Juice.I.HitStop(0.07f, 0.15f);
                Juice.I.PopDamage(mitigated, transform.position + Vector3.up * 1.6f);
            }
            SpawnFx.Burst(transform.position + Vector3.up * 0.6f, Palette.Health, 12, 9f, 0.4f);

            // 被击中时把玩家推开一点，制造"被撞击"的体感
            Vector3 push = transform.position - from;
            push.y = 0f;
            if (push.sqrMagnitude > 0.0001f) transform.position += push.normalized * 0.5f;

            if (Health <= 0f) Die();
        }

        /// <summary>
        /// 无视无敌帧直接致死。
        /// 用于自检里确定性地验证「死亡 → 结算」流程 —— 走 TakeDamage 会被无敌帧挡住，
        /// 取决于调用时机，不适合做断言。
        /// </summary>
        public void Kill()
        {
            if (!Alive) return;
            Health = 0f;
            Die();
        }

        public void Heal(float amount)
        {
            Health = Mathf.Min(Stats.MaxHealth, Health + amount);
            if (Juice.I != null)
                Juice.I.PopText("+" + Mathf.RoundToInt(amount), transform.position + Vector3.up * 1.9f, Palette.Good, 1.1f, 0.8f);
        }

        void Die()
        {
            Alive = false;
            Health = 0f;
            if (_body != null) _body.gameObject.SetActive(false);
            if (_shieldGo != null) _shieldGo.SetActive(false);

            SpawnFx.Burst(transform.position + Vector3.up * 0.6f, Palette.Player, 30, 15f, 0.9f);
            SpawnFx.Burst(transform.position + Vector3.up * 0.6f, Color.white, 16, 9f, 0.7f);
            if (Juice.I != null) Juice.I.Shake(1.0f);

            gameObject.SetActive(false);
            if (GameManager.I != null) GameManager.I.OnPlayerDied();
        }

        public void AddPickupChain()
        {
            _pickupChain++;
            _pickupChainTimer = 1.4f;
        }

        public int PickupChain { get { return _pickupChain; } }

        void UpdatePickupChain(float dt)
        {
            if (_pickupChainTimer > 0f)
            {
                _pickupChainTimer -= dt;
                if (_pickupChainTimer <= 0f) _pickupChain = 0;
            }
        }

        // ------------------------------------------------------------------
        //  光环 / 视觉 / 相机
        // ------------------------------------------------------------------

        void UpdateAura(float dt, bool active)
        {
            if (!active || Stats.ThornsDamage <= 0f) return;

            const float radius = 3.4f;
            var hits = Physics.OverlapSphere(transform.position, radius, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < hits.Length; i++)
            {
                var e = hits[i].GetComponentInParent<Enemy>();
                if (e != null && e.Alive)
                {
                    e.TakeDamage(Stats.ThornsDamage * dt, e.transform.position, Vector3.zero, false, 0f, true);
                }
            }
        }

        void UpdateVisuals(float dt)
        {
            if (!Alive) return;

            // 身体朝向瞄准方向；移动时轻微上下起伏，避免"贴图感"
            if (_body != null)
            {
                _body.localRotation = Quaternion.Slerp(_body.localRotation,
                    Quaternion.LookRotation(_facing, Vector3.up), 1f - Mathf.Exp(-18f * dt));
                float bob = Mathf.Sin(_moveAnimPhase) * 0.06f;
                _body.localPosition = new Vector3(0f, bob, 0f);
            }

            if (_gunPivot != null)
            {
                _gunPivot.rotation = Quaternion.LookRotation(_facing, Vector3.up);
            }

            // 光环缓慢自转 + 无敌帧闪烁
            if (_ring != null)
            {
                _ring.Rotate(0f, 42f * dt, 0f, Space.Self);
                float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.05f;
                _ring.localScale = Vector3.one * pulse;
            }

            bool invuln = _invulnTimer > 0f;
            if (_bodyRenderer != null)
            {
                _bodyRenderer.enabled = !invuln || Mathf.Repeat(Time.time, 0.12f) > 0.06f;
            }
            if (_shieldGo != null && _shieldGo.activeSelf != invuln)
            {
                _shieldGo.SetActive(invuln);
            }
        }

        void UpdateCamera(float dt)
        {
            if (_camRig == null) return;

            // 自检模式：相机固定在场地中心上方俯视，保证截图能看到竞技场全貌
            if (SelfTest.Enabled)
            {
                _camRig.position = Vector3.Lerp(_camRig.position, Vector3.zero,
                    1f - Mathf.Exp(-4f * dt));
                return;
            }

            // 相机跟随 + 朝移动/瞄准方向的前瞻，让玩家能提前看到"要打的方向"
            Vector2 input;
            ReadMoveInput(out input);
            Vector3 moveDir = new Vector3(input.x, 0f, input.y);
            Vector3 look = moveDir.sqrMagnitude > 0.01f ? moveDir.normalized : _aimDir;
            Vector3 target = transform.position + look * 3.4f;
            target.y = 0f;

            // 用指数平滑而不是 Lerp(t)，保证不同帧率下手感一致
            float k = 1f - Mathf.Exp(-6.5f * dt);
            _camRig.position = Vector3.Lerp(_camRig.position, target, k);
        }

        void OnTriggerStay(Collider other)
        {
            if (!Alive) return;

            // 敌人贴身：持续伤害（由敌人自己的碰撞伤害逻辑处理更合理，这里做兜底）
            var enemy = other.GetComponentInParent<Enemy>();
            if (enemy != null && enemy.Alive && enemy.Def != null && enemy.Def.ContactDamage > 0f)
            {
                TakeDamage(enemy.Def.ContactDamage * Time.deltaTime * 2.2f, other.transform.position);
            }
        }

        public void ApplyUpgrade(UpgradeCard card)
        {
            if (card != null && card.Apply != null) card.Apply(this);
            Stats.Clamp();
            Health = Mathf.Min(Health, Stats.MaxHealth);
            _dashCharges = Mathf.Min(_dashCharges, MaxDashCharges);
        }
    }

    /// <summary>新星冲击波的可视化：一圈快速扩散并淡出的光环。</summary>
    public class NovaVisual : MonoBehaviour
    {
        float _life;
        float _maxLife;
        float _radius;
        Renderer _r;
        Material _mat;

        public static void Spawn(Vector3 pos, float radius, Color color)
        {
            var go = new GameObject("NovaVisual");
            go.transform.position = pos + Vector3.up * 0.1f;
            var nv = go.AddComponent<NovaVisual>();
            nv.Setup(radius, color);
        }

        void Setup(float radius, Color color)
        {
            _radius = radius;
            _maxLife = 0.42f;
            _life = _maxLife;

            var mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = MeshLib.Ring;
            _r = gameObject.AddComponent<MeshRenderer>();
            _mat = MeshLib.Unlit(new Color(color.r, color.g, color.b, 0.85f), false, true);
            _r.sharedMaterial = _mat;
            _r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _r.receiveShadows = false;
            transform.localScale = Vector3.one * 0.2f;
        }

        void Update()
        {
            _life -= Time.deltaTime;
            if (_life <= 0f) { Destroy(gameObject); return; }
            float k = 1f - _life / _maxLife;
            transform.localScale = Vector3.one * Mathf.Lerp(0.2f, _radius, k);
            if (_mat != null)
            {
                var c = _mat.GetColor("_BaseColor");
                c.a = 1f - k;
                _mat.SetColor("_BaseColor", c);
                _mat.SetColor("_Color", c);
            }
        }
    }
}
