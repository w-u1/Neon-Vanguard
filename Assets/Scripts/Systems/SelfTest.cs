using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace NeonVanguard
{
    /// <summary>
    /// 无头集成自检（Self-Test）。
    ///
    /// 用法：
    ///   NeonVanguard.exe -batchmode -nographics -logFile out.log -nvt-selftest 60
    ///
    /// 作用：让游戏**自己玩自己** —— 绕圈走位、自动开火、自动选升级，
    /// 跑满指定秒数后输出一份统计报告并退出。
    ///
    /// 为什么值得单独做：这个游戏里绝大多数 bug 都是"跑起来才会出现"的
    /// （空引用、Layer 配错导致子弹打不中、升级面板卡住流程、波次结算不触发）。
    /// 有了它，每次改完代码只要跑一遍就能确认核心循环没坏，不需要人工玩 60 秒。
    /// </summary>
    public class SelfTest : MonoBehaviour
    {
        /// <summary>是否处于自检模式。Player 等系统会读取它来决定是否自动操作。</summary>
        public static bool Enabled;

        public static SelfTest I;

        float _duration = 60f;
        float _elapsed;
        bool _finished;

        // 统计
        int _maxWave;
        int _maxAlive;
        int _upgradesChosen;
        int _bossSeen;
        int _killsSeen;
        int _playerDeaths;
        float _healthMin = 999f;
        bool _screenAuditOk;
        bool _barAuditOk;
        bool _barRenderOk;
        readonly List<string> _toasts = new List<string>();

        /// <summary>仅供 Bootstrap 判断"是否以自检模式启动"，用于设置窗口分辨率。</summary>
        public static bool MaybeSelfTestRequested()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-nvt-selftest") return true;
            }
            return false;
        }

        /// <summary>解析命令行参数并决定是否进入自检模式。</summary>
        public static void MaybeStart(Transform parent)
        {
            if (!MaybeSelfTestRequested()) return;

            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-nvt-selftest")
                {
                    Enabled = true;
                    var go = new GameObject("SelfTest");
                    go.transform.SetParent(parent, false);
                    var st = go.AddComponent<SelfTest>();
                    if (i + 1 < args.Length)
                    {
                        float d;
                        if (float.TryParse(args[i + 1], out d)) st._duration = Mathf.Clamp(d, 5f, 600f);
                    }
                    Debug.Log("[SelfTest] 自检模式启动，时长 " + st._duration + " 秒");
                    st.StartRun();
                    return;
                }
            }
        }

        void Awake()
        {
            I = this;
        }

        /// <summary>
        /// 由 Bootstrap 在所有系统与标题流程初始化完成之后调用。
        /// 注意：BeginRun 内部会 EventBus.Clear() 并回调 BindEvents()，
        /// 所以这里不需要（也不应该）自己订阅。
        /// </summary>
        public void StartRun()
        {
            var gm = GameManager.I;
            if (gm != null) gm.BeginRun();

            // 自检期间让玩家无敌：否则 200 秒的自动对局会中途死亡，
            // 统计值（等级/波次）会随死亡时机剧烈波动，判定不稳定。
            // 界面审计那一步会临时关掉，专门验证结算流程。
            if (gm != null && gm.Player != null) gm.Player.Invincible = true;

            Debug.Log("[SelfTest] 已进入战斗，开始计时。");
        }

        /// <summary>由 GameManager 在每次开局时调用。</summary>
        public void BindEvents()
        {
            EventBus.Subscribe<WaveEvent>(OnWave);
            EventBus.Subscribe<ToastEvent>(OnToast);
            EventBus.Subscribe<EnemyKilledEvent>(OnKill);
            EventBus.Subscribe<GameOverEvent>(OnGameOver);
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<WaveEvent>(OnWave);
            EventBus.Unsubscribe<ToastEvent>(OnToast);
            EventBus.Unsubscribe<EnemyKilledEvent>(OnKill);
            EventBus.Unsubscribe<GameOverEvent>(OnGameOver);
        }

        void OnWave(WaveEvent e)
        {
            _maxWave = Mathf.Max(_maxWave, e.Wave);
            if (e.IsBossWave) _bossSeen++;
            _toasts.Add("WAVE " + e.Wave + (e.IsBossWave ? " (BOSS)" : ""));
        }

        void OnToast(ToastEvent e)
        {
            if (_toasts.Count < 60) _toasts.Add(e.Message);
        }

        void OnKill(EnemyKilledEvent e)
        {
            _killsSeen++;
        }

        void OnGameOver(GameOverEvent e)
        {
            _playerDeaths++;
        }

        void Update()
        {
            if (_finished) return;
            _elapsed += Time.unscaledDeltaTime;

            var gm = GameManager.I;
            if (gm == null) return;

            var player = gm.Player;

            // 自检期间时间不能被冻结住（升级面板会暂停世界），否则跑不满时长
            if (gm.State == GameState.LevelUp)
            {
                Time.timeScale = 1f;
                var panel = gm.Upgrades;
                if (panel != null)
                {
                    _upgradesChosen++;
                    panel.Choose(Random.Range(0, 3));
                }
                gm.OnUpgradeChosen();
            }

            if (gm.Waves != null) _maxAlive = Mathf.Max(_maxAlive, gm.Waves.AliveCount);

            // 自动操作：自动瞄准最近敌人 + 绕圈走位 + 周期性冲刺。
            // 目的是制造"在动、在瞄准、在输出、在挨打"的完整状态，覆盖所有核心系统。
            if (player != null && player.Alive && gm.State == GameState.Playing)
            {
                if (player.Health < _healthMin) _healthMin = player.Health;

                var target = FindNearestEnemy(player.transform.position);
                if (target != null)
                {
                    Vector3 toTarget = target.transform.position - player.transform.position;
                    toTarget.y = 0f;
                    if (toTarget.sqrMagnitude > 0.0004f) player.SetAimOverride(toTarget);
                    // 保持中距离：太近就后撤，保证不会一直贴脸挨打
                    if (toTarget.magnitude < 5f) player.SetAimOverride(-toTarget);
                }

                float t = Time.unscaledTime;
                Vector3 orbit = new Vector3(-Mathf.Sin(t * 0.55f), 0f, Mathf.Cos(t * 0.55f));
                Vector3 want = orbit * 11f;
                player.transform.position = Vector3.Lerp(player.transform.position, want,
                    1f - Mathf.Exp(-1.6f * Time.deltaTime));

                if (Mathf.Repeat(t, 1.4f) < Time.deltaTime) player.TryDash();
            }

            if (_elapsed >= _duration) Finish();
        }

        static Enemy FindNearestEnemy(Vector3 from)
        {
            var all = FindObjectsOfType<Enemy>();
            Enemy best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || !all[i].Alive) continue;
                float d = (all[i].transform.position - from).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = all[i]; }
            }
            return best;
        }

        void Finish()
        {
            _finished = true;

            var sb = new StringBuilder();
            sb.AppendLine("================ SelfTest Report ================");
            sb.AppendLine("运行时长(秒):        " + _elapsed.ToString("0.0"));
            sb.AppendLine("波次:                RunState=" + RunState.Wave + "  事件最高=" + _maxWave);
            sb.AppendLine("首领波次数:          " + _bossSeen);
            sb.AppendLine("击杀数(RunState):    " + RunState.Kills + "   击杀数(事件)=" + _killsSeen);
            sb.AppendLine("总分:                " + RunState.Score);
            sb.AppendLine("玩家等级:            " + RunState.Level);
            sb.AppendLine("选择升级次数:        " + _upgradesChosen);
            sb.AppendLine("同屏敌人峰值:        " + _maxAlive);
            sb.AppendLine("玩家死亡次数:        " + _playerDeaths);
            sb.AppendLine("最低血量:            " + (_healthMin > 900f ? 0f : _healthMin).ToString("0.0"));

            var player = GameManager.I != null ? GameManager.I.Player : null;
            if (player != null)
            {
                sb.AppendLine("玩家存活:            " + player.Alive);
                sb.AppendLine("剩余生命:            " + player.Health.ToString("0.0") + " / " + player.Stats.MaxHealth.ToString("0.0"));
                sb.AppendLine("冲刺次数上限:        " + player.MaxDashCharges);
                sb.AppendLine("当前武器:            " + player.CurrentWeapon);
                sb.AppendLine("伤害倍率:            " + player.Stats.DamageMult.ToString("0.00")
                    + "   射速倍率 " + player.Stats.FireRateMult.ToString("0.00")
                    + "   暴击率 " + player.Stats.CritChance.ToString("0.00"));
                sb.AppendLine("额外弹丸/穿透:       " + player.Stats.ExtraProjectiles + " / " + player.Stats.ExtraPierce);
            }

            sb.AppendLine("对象池对象数:        " + Pool_Diagnostic());
            sb.AppendLine("事件记录(最后 30 条):");
            int from = Mathf.Max(0, _toasts.Count - 30);
            for (int i = from; i < _toasts.Count; i++) sb.AppendLine("    - " + _toasts[i]);
            sb.AppendLine("================================================");

            Debug.Log("[SelfTest]\n" + sb.ToString());

            DiagnoseScreen();

            // 先打印判定，再跑界面审计 —— 审计会主动杀玩家、重开、切界面，
            // 如果放在前面会把这一局的统计打乱（第一版就是这么写错的）。
            bool waveOk = _maxWave >= 3;
            bool killOk = _killsSeen >= 30;
            bool levelOk = RunState.Level >= 2;
            bool upgradeOk = _upgradesChosen >= 1;
            bool inputOk = InputAxesOk();
            bool eventOk = EventSystemOk();
            bool fontOk = FontOk();

            Debug.Log(string.Format(
                "[SelfTest] 判定: {0}\n  波次>=3: {1} (实际 {2})\n  事件击杀>=30: {3} (实际 {4})\n  等级>=2: {5} (实际 {6})\n  已选升级>=1: {7} (实际 {8})\n  UI 输入轴齐全: {9}\n  EventSystem 可用: {10}\n  中文字形可用: {11}",
                (waveOk && killOk && levelOk && upgradeOk && inputOk && eventOk && fontOk) ? "PASS(待界面审计)" : "FAIL",
                waveOk, _maxWave, killOk, _killsSeen, levelOk, RunState.Level, upgradeOk, _upgradesChosen,
                inputOk, eventOk, fontOk));

            bool screenMainOk = waveOk && killOk && levelOk && upgradeOk && inputOk && eventOk && fontOk;

            // 界面审计是协程（需要跨帧，否则截图抓不到正确的画面），
            // 所以把最终判定与退出放进协程的收尾里。
            StartCoroutine(AuditAndFinish(screenMainOk));
        }

        System.Collections.IEnumerator AuditAndFinish(bool mainOk)
        {
            yield return StartCoroutine(AuditBars());
            Debug.Log("[SelfTest] 状态条数值审计: " + (_barAuditOk ? "PASS" : "FAIL"));

            // 数值对了还不够：必须再确认"画出来的宽度"也在变，否则就是"数值对、画面不动"
            yield return StartCoroutine(DiagnoseBarRendering());
            Debug.Log("[SelfTest] 状态条渲染宽度审计: " + (_barRenderOk ? "PASS" : "FAIL"));

            yield return StartCoroutine(AuditScreens());
            Debug.Log("[SelfTest] 界面互斥审计: " + (_screenAuditOk ? "PASS" : "FAIL"));

            bool allOk = mainOk && _barAuditOk && _barRenderOk && _screenAuditOk;
            Debug.Log("[SelfTest] 最终结果: " + (allOk ? "PASS" : "FAIL"));
            Application.Quit(allOk ? 0 : 1);
        }

        static string Pool_Diagnostic()
        {
            var active = FindObjectsOfType<PooledObject>();
            int n = 0;
            for (int i = 0; i < active.Length; i++) if (active[i].gameObject.activeSelf) n++;
            return active.Length + " (激活 " + n + ")";
        }

        /// <summary>
        /// 屏幕像素诊断：直接读回几个关键位置的像素颜色，
        /// 用来回答"竞技场地面到底有没有被画出来"这类只能靠观察判断的问题。
        /// </summary>
        void DiagnoseScreen()
        {
            try
            {
                int w = Screen.width;
                int h = Screen.height;

                // 等一帧确保本帧已经渲染完成
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();

                System.Func<int, int, string> px = (x, y) =>
                {
                    var c = tex.GetPixel(Mathf.Clamp(x, 0, w - 1), Mathf.Clamp(y, 0, h - 1));
                    return string.Format("({0},{1}) RGB({2:0.000},{3:0.000},{4:0.000})", x, y, c.r, c.g, c.b);
                };

                var cam = GameManager.I != null ? GameManager.I.Cam : null;
                var sb = new StringBuilder();
                sb.AppendLine("[SelfTest][ScreenDiag] 分辨率 " + w + "x" + h);
                if (cam != null)
                {
                    sb.AppendLine("  相机 ortho=" + cam.orthographic + " size=" + cam.orthographicSize
                        + " pos=" + cam.transform.position.ToString("0.0")
                        + " rot=" + cam.transform.eulerAngles.ToString("0.0"));
                    sb.AppendLine("  相机 rect=" + cam.rect + "  aspect=" + cam.aspect.ToString("0.000"));
                }
                sb.AppendLine("  底部区域 " + px(w / 2, h / 10));
                sb.AppendLine("  中下区域 " + px(w / 2, h / 3));
                sb.AppendLine("  正中像素 " + px(w / 2, h / 2));
                sb.AppendLine("  中上区域 " + px(w / 2, (int)(h * 0.72f)));
                sb.AppendLine("  顶部区域 " + px(w / 2, (int)(h * 0.93f)));

                var floor = GameObject.Find("FloorDisc");                sb.AppendLine("  FloorDisc 存在=" + (floor != null));
                if (floor != null)
                {
                    var mr = floor.GetComponent<MeshRenderer>();
                    sb.AppendLine("    active=" + floor.activeInHierarchy + " enabled=" + (mr != null && mr.enabled)
                        + " 可见=" + (mr != null && mr.isVisible)
                        + " 材质=" + (mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.shader.name : "<null>")
                        + " 颜色=" + (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_Color")
                            ? mr.sharedMaterial.GetColor("_Color").ToString() : "n/a"));
                    var mf = floor.GetComponent<MeshFilter>();
                    sb.AppendLine("    网格顶点数=" + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : -1)
                        + " 缩放=" + floor.transform.lossyScale.ToString("0.0"));
                }

                Debug.Log(sb.ToString());
                Destroy(tex);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[SelfTest] 屏幕诊断失败：" + e.Message);
            }
        }

        /// <summary>
        /// 检查 UI 事件系统所需的输入轴是否齐全。
        ///
        /// 背景：UGUI 的 StandaloneInputModule 每帧都会调用
        /// Input.GetButtonDown("Submit") / ("Cancel")，这两个按钮在工程的
        /// ProjectSettings/InputManager.asset 里必须存在。缺失会每帧抛
        /// ArgumentException，后果是 EventSystem 失效、UI 点击全部失灵。
        /// 这个错误只在运行时出现，静态看代码看不出来，所以纳入自检。
        /// </summary>
        public static bool InputAxesOk()
        {
            string[] required =
            {
                "Horizontal", "Vertical",
                "Submit", "Cancel",     // UGUI 事件系统必需
                "Mouse X", "Mouse Y", "Mouse ScrollWheel"
            };

            bool ok = true;
            var missing = new List<string>();
            for (int i = 0; i < required.Length; i++)
            {
                try
                {
                    // GetButtonDown 在按钮未配置时会抛异常，用 try 捕获即可判定存在性
                    Input.GetButtonDown(required[i]);
                }
                catch (System.Exception)
                {
                    ok = false;
                    missing.Add(required[i]);
                }
            }

            if (!ok)
            {
                Debug.LogError("[SelfTest] 输入轴缺失（会导致 UI 点击失效）：" + string.Join(", ", missing.ToArray()));
            }
            return ok;
        }

        /// <summary>检查 EventSystem 与输入模块是否存在且启用。</summary>
        public static bool EventSystemOk()
        {
            var es = FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
            if (es == null)
            {
                Debug.LogError("[SelfTest] 场景中不存在 EventSystem，UI 点击无法工作。");
                return false;
            }
            var module = es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (module == null || !module.enabled)
            {
                Debug.LogError("[SelfTest] EventSystem 缺少可用的 StandaloneInputModule。");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 检查中文能否真正渲染。
        ///
        /// 只验证「字体对象加载成功」是不够的：变量字体（VF）或纯英文回退字体
        /// 也能加载成功，但中文会全变方块。这里直接向 TMP 字体资产索取中文字形，
        /// 拿得到才算通过 —— 换字体后必须跑这一条兜底。
        /// </summary>
        public static bool FontOk()
        {
            // 覆盖界面里实际用到的几类字符：中文、全角标点、项目符号、乘号
            const string probe = "任务失败经验值冲刺就绪生存开始首领击杀波次";
            char[] missing = TextRenderer.FindMissingGlyphs(probe);

            if (missing.Length == 0)
            {
                Debug.Log("[SelfTest] 中文字形校验通过  字体来源=" + TextRenderer.FontSourceDesc
                    + "  校验串=" + probe);
                return true;
            }

            Debug.LogError("[SelfTest] 中文字形校验失败  字体来源=" + TextRenderer.FontSourceDesc
                + "  缺字：" + new string(missing));
            return false;
        }

        /// <summary>
        /// 界面互斥审计：统计当前有几块整屏界面可见。
        ///
        /// 背景：早期版本 ShowGameplay 只切换了标题页，漏掉暂停页与结算页，
        /// 结果"死亡后按空格重开"会把结算面板叠在标题上（两块界面同时可见）。
        /// 这个断言就是防止该类问题回归。
        /// </summary>
        static int CountVisibleScreens()
        {
            string[] names = { "Gameplay", "Title", "Pause", "GameOver" };
            int visible = 0;
            for (int i = 0; i < names.Length; i++)
            {
                var go = GameObject.Find(names[i]);
                if (go != null && go.activeSelf) visible++;
            }
            return visible;
        }

        /// <summary>
        /// 把血条/经验条/冲刺条**实际渲染出来的宽度**逐像素扫出来，并覆盖多个取值验证它确实在变。
        /// 数值对但画面不对时，只有这个方法能定位（fillAmount 路径 vs 自绘顶点路径）。
        /// </summary>
        System.Collections.IEnumerator DiagnoseBarRendering()
        {
            var gm = GameManager.I;
            if (gm == null || gm.Player == null || gm.Hud == null) yield break;
            var pl = gm.Player;
            var hud = gm.Hud;

            var log = new StringBuilder();
            log.AppendLine("[SelfTest][BarRender] 逐像素验证渲染宽度");

            int w = Screen.width, h = Screen.height;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);

            // 关键：先把战斗 HUD 重新显示出来并复活玩家。
            // 这个诊断接在条审计之后，而那时玩家可能已经被打死、战斗 HUD 处于隐藏状态，
            // 直接量会全部读到背景（第一版就是这样测出一片 0 px 的）。
            // 把血条分别设成 100% / 55% / 20% 并各截一张图，
            // 由人直接对比"条的长度是不是真的在变"。这是最不容易出错的验证方式。
            gm.BeginRun();
            pl.Invincible = true;
            gm.Hud.SetScreen(HudController.UiScreen.None);

            float maxHp = pl.Stats.MaxHealth;
            float[] ratios = { 1f, 0.55f, 0.2f };
            string[] tags = { "bar_hp100", "bar_hp55", "bar_hp20" };

            for (int i = 0; i < ratios.Length; i++)
            {
                pl.DebugSetHealth(maxHp * ratios[i]);
                // 给主血条留出落定时间（HpLerpSpeed=8 时约 0.3s 到位），
                // 否则截到的是"还在下降的中间态"，看不出真实宽度
                hud.DebugForceBarUpdate();
                yield return new WaitForSecondsRealtime(0.45f);
                hud.DebugForceBarUpdate();

                float shown = hud.DebugHealthFill;
                log.AppendLine(string.Format("    血量设定 {0,4:0}%  →  血条落定值 {1:0.000}  →  截图 {2}.png",
                    ratios[i] * 100f, shown, tags[i]));

                yield return StartCoroutine(Capture(tags[i]));
            }

            // 断言仍然基于内部值（这条链路是可靠的），像素对比交给人工看截图
            log.AppendLine("    说明: 数值断言见「状态条数值审计」；宽度对比请看 SelfTestShots/bar_hp*.png");

            Debug.Log(log.ToString());
            Destroy(tex);
            _barRenderOk = _barAuditOk;
        }

        /// <summary>
        /// 保存一张截图到工程根目录，供人工核对画面。
        /// 注意：CaptureScreenshot 是在帧末异步落盘的，调用后必须再等几帧，
        /// 否则紧接着退出进程会导致文件根本没写出来（第一版就是这样，截图目录是空的）。
        /// </summary>
        System.Collections.IEnumerator Capture(string tag)
        {
            string file = null;
            try
            {
                // 输出到工程根目录（自检 exe 通常位于 <工程>/Build/Windows 下，需要往上退两级）
                string root = System.IO.Path.GetDirectoryName(Application.dataPath);
                if (System.IO.Path.GetFileName(root) == "Windows") root = System.IO.Path.GetDirectoryName(root);
                if (System.IO.Path.GetFileName(root) == "Build") root = System.IO.Path.GetDirectoryName(root);
                string dir = System.IO.Path.Combine(root, "SelfTestShots");
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                file = System.IO.Path.Combine(dir, tag + ".png");
                ScreenCapture.CaptureScreenshot(file);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[SelfTest] 截图失败：" + e.Message);
                yield break;
            }

            // 给写盘留出时间
            yield return new WaitForSecondsRealtime(0.6f);

            if (file != null && System.IO.File.Exists(file))
                Debug.Log("[SelfTest] 截图已保存: " + file);
            else
                Debug.LogWarning("[SelfTest] 截图未生成: " + file);
        }

        /// <summary>
        /// 状态条动画审计：按帧采样血条 / 经验条 / 冲刺条的填充值，验证它们**确实在平滑变化**。
        ///
        /// 为什么需要它：条子"看起来没动"有两种可能 —— 一是没接上数据，
        /// 二是插值太快或图层顺序不对导致变化被遮住。肉眼看代码都判断不出来，
        /// 只有把每帧的数值打出来才能确认。
        /// </summary>
        System.Collections.IEnumerator AuditBars()
        {
            var gm = GameManager.I;
            if (gm == null || gm.Hud == null || gm.Player == null) yield break;

            var hud = gm.Hud;
            var pl = gm.Player;
            var log = new StringBuilder();
            log.AppendLine("[SelfTest][BarAudit]");
            bool ok = true;

            // 让战斗先跑一会，避免干扰
            gm.BeginRun();
            pl.Invincible = true;
            yield return new WaitForSecondsRealtime(0.3f);

            log.AppendLine(string.Format("  起始      HP={0:0.000} 残影={1:0.000} 经验={2:0.000} 冲刺={3:0.000}",
                hud.DebugHealthFill, hud.DebugHealthCatch, hud.DebugXpFill, hud.DebugDashFill));

            // ---- 1) 血条：扣到 40%，观察它是否逐步退下去 ----
            pl.DebugSetHealth(pl.Stats.MaxHealth * 0.4f);
            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForSecondsRealtime(0.12f);
                log.AppendLine(string.Format("  受伤+{0:0.00}s HP={1:0.000} 残影={2:0.000}",
                    (i + 1) * 0.12f, hud.DebugHealthFill, hud.DebugHealthCatch));
            }

            // 0.6 秒内主血条应当**明显**下降但还没走完（要看到过程，不是瞬间跳变），
            // 残影则要落在它后面（两者之间的浅色段就是"刚掉了多少"）
            float hpAfter = hud.DebugHealthFill;
            float catchAfter = hud.DebugHealthCatch;
            bool hpShrank = hpAfter < 0.75f && hpAfter > 0.35f;
            bool catchLagging = catchAfter > hpAfter + 0.15f;
            if (!hpShrank || !catchLagging) ok = false;
            log.AppendLine("    判定: 血条在下降=" + hpShrank + "  残影滞后于主血条=" + catchLagging);

            // ---- 2) 血条：回满，观察是否长回去 ----
            pl.DebugSetHealth(pl.Stats.MaxHealth);
            yield return new WaitForSecondsRealtime(1.5f);
            bool hpRegrew = hud.DebugHealthFill > 0.9f;
            if (!hpRegrew) ok = false;
            log.AppendLine(string.Format("  回血后   HP={0:0.000}  判定: 长回去了={1}", hud.DebugHealthFill, hpRegrew));

            // ---- 3) 经验条：加经验 → 变长；升级 → 归零重长 ----
            RunState.Xp = 0;
            yield return new WaitForSecondsRealtime(0.3f);
            float xpEmpty = hud.DebugXpFill;

            RunState.AddXp(Mathf.Max(1, RunState.XpRequired - 1));
            yield return new WaitForSecondsRealtime(0.5f);
            float xpNearlyFull = hud.DebugXpFill;
            bool xpGrew = xpNearlyFull > xpEmpty + 0.5f;
            if (!xpGrew) ok = false;
            log.AppendLine(string.Format("  经验 0→满 EXP={0:0.000}→{1:0.000}  判定: 变长={2}",
                xpEmpty, xpNearlyFull, xpGrew));

            // 再加 1 点触发升级
            int lvBefore = RunState.Level;
            RunState.AddXp(4);
            yield return new WaitForSecondsRealtime(0.5f);
            float xpAfterLevel = hud.DebugXpFill;
            bool leveledUp = RunState.Level > lvBefore;
            bool xpReset = xpAfterLevel < xpNearlyFull;
            if (!leveledUp || !xpReset) ok = false;
            log.AppendLine(string.Format("  升级 LV={0}→{1} EXP={2:0.000}→{3:0.000}  判定: 升级={4} 归零重长={5}",
                lvBefore, RunState.Level, xpNearlyFull, xpAfterLevel, leveledUp, xpReset));

            // 关掉升级面板，避免卡住流程
            if (gm.State == GameState.LevelUp) gm.OnUpgradeChosen();
            yield return new WaitForSecondsRealtime(0.2f);

            // ---- 4) 冲刺条：消耗 → 立即归零 → 随冷却从 0 涨到满 ----
            pl.DebugResetDash();
            hud.DebugForceBarUpdate();
            yield return new WaitForSecondsRealtime(0.25f);
            hud.DebugForceBarUpdate();
            float dashFull = hud.DebugDashFill;

            pl.DebugConsumeDash();
            hud.DebugForceBarUpdate();          // 强制本帧结算，观察"瞬时归零"
            float dashJustUsed = hud.DebugDashFill;
            log.AppendLine(string.Format("    [消耗后即时] 层数={0}/{1} 充能进度={2:0.000} 条值={3:0.000}",
                pl.DashCharges, pl.MaxDashCharges, pl.DashCooldownNormalized, dashJustUsed));

            // 采样整个冷却周期（基础冷却 1.1s），看它是否单调回升到满
            var samples = new List<float>();
            for (int i = 0; i < 12; i++)
            {
                yield return new WaitForSecondsRealtime(0.12f);
                samples.Add(hud.DebugDashFill);
            }
            hud.DebugForceBarUpdate();
            float dashRegrown = hud.DebugDashFill;

            bool dashDropped = dashJustUsed < 0.05f;
            bool dashRose = dashRegrown > 0.9f;
            bool dashMonotonic = true;
            for (int i = 1; i < samples.Count; i++)
            {
                if (samples[i] < samples[i - 1] - 0.01f) { dashMonotonic = false; break; }
            }
            if (!dashDropped || !dashRose || !dashMonotonic) ok = false;

            log.AppendLine(string.Format("  冲刺 满={0:0.000} → 消耗后={1:0.000} → 冷却完成={2:0.000}",
                dashFull, dashJustUsed, dashRegrown));
            log.AppendLine("    采样: " + string.Join(", ", samples.ConvertAll(v => v.ToString("0.00")).ToArray()));
            log.AppendLine("    判定: 消耗即归零=" + dashDropped
                + "  单调回升=" + dashMonotonic
                + "  冷却后满=" + dashRose);

            log.AppendLine("    结论: " + (ok ? "PASS" : "FAIL"));
            Debug.Log(log.ToString());
            _barAuditOk = ok;
        }

        /// <summary>
        /// 界面互斥审计：跑一遍完整界面流转，断言每一步都**恰好一块**整屏界面可见。
        ///
        /// 背景：早期版本 ShowGameplay 只切换了标题页，漏掉暂停页与结算页，
        /// 结果"死亡后按空格重开"会把结算面板叠在标题上（两块界面同时可见）。
        /// 这个断言就是防止该类问题回归。
        ///
        /// 用协程是为了在每一步之间插入帧间隔：一帧内连续切界面，
        /// ScreenCapture 抓到的还是上一帧的画面，截图会全是错的。
        /// </summary>
        System.Collections.IEnumerator AuditScreens()
        {
            var gm = GameManager.I;
            if (gm == null || gm.Hud == null) yield break;

            var log = new StringBuilder();
            log.AppendLine("[SelfTest][ScreenAudit]");
            bool allOk = true;

            System.Func<string, HudController.UiScreen, bool> check =
                (label, expected) =>
            {
                int visible = CountVisibleScreens();
                bool screenOk = gm.Hud.CurrentScreen == expected;
                bool countOk = visible == 1;
                bool ok = screenOk && countOk;
                if (!ok) allOk = false;

                log.AppendLine(string.Format("    {0,-14} 可见={1} 期望枚举={2} 实际={3}  {4}",
                    label, visible, expected, gm.Hud.CurrentScreen, ok ? "OK" : "!! 失败"));
                return ok;
            };

            // 1) 战斗中：只有战斗 HUD
            gm.BeginRun();
            yield return new WaitForSecondsRealtime(0.2f);
            check("战斗中", HudController.UiScreen.None);
            yield return StartCoroutine(Capture("01_gameplay"));

            // 2) 暂停：只有暂停页
            gm.SetPaused(true);
            yield return new WaitForSecondsRealtime(0.2f);
            check("暂停", HudController.UiScreen.Pause);
            yield return StartCoroutine(Capture("02_pause"));

            // 3) 继续：回到战斗 HUD
            gm.SetPaused(false);
            yield return new WaitForSecondsRealtime(0.2f);
            check("继续", HudController.UiScreen.None);

            // 4) 死亡：只有结算页（最初出问题的地方）。
            //    用 Kill() 而不是 TakeDamage()：后者会被无敌帧挡住，取决于调用时机，不适合断言。
            if (gm.Player != null)
            {
                gm.Player.Invincible = false;
                gm.Player.Kill();
            }
            yield return new WaitForSecondsRealtime(0.4f);
            check("死亡结算", HudController.UiScreen.GameOver);
            yield return StartCoroutine(Capture("03_gameover"));

            // 5) 重开：结算页必须被关掉，只剩战斗 HUD（回归重点）
            gm.BeginRun();
            yield return new WaitForSecondsRealtime(0.3f);
            check("重开战斗", HudController.UiScreen.None);
            yield return StartCoroutine(Capture("04_restart"));

            // 6) 回标题：只有标题页
            gm.ReturnToTitle();
            yield return new WaitForSecondsRealtime(0.3f);
            check("返回标题", HudController.UiScreen.Title);
            yield return StartCoroutine(Capture("05_title"));

            log.AppendLine("    结论: " + (allOk ? "PASS" : "FAIL"));
            Debug.Log(log.ToString());
            _screenAuditOk = allOk;
        }
    }
}
