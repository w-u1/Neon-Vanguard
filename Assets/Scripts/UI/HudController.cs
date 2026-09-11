using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NeonVanguard
{
    /// <summary>
    /// HUD 与所有界面。全部由代码搭建（Canvas / 血条 / 升级面板 / 结算），
    /// 不依赖任何预制体或美术图，条状元素用纯色 Image 拉出来。
    ///
    /// 两个关键设计：
    ///  1) **界面互斥由 SetScreen 统一管理**。之前只切换标题页和战斗 HUD，
    ///     漏掉了暂停页与结算页，导致"死亡后重开"时结算面板会叠在标题上。
    ///     现在任何时刻最多只有一个整屏界面可见。
    ///  2) **所有状态条都做插值**，不直接跳变。血条平滑下降、受伤残影延迟追赶、
    ///     冲刺与经验条实时反馈进度 —— 玩家能"读"到数值变化的过程，而不是只看到结果。
    /// </summary>
    public class HudController : MonoBehaviour
    {
        /// <summary>
        /// 整屏界面。任何时刻只允许一个可见。
        /// 名字刻意不叫 Screen —— 那会遮蔽 UnityEngine.Screen，
        /// 导致同一个文件里的 Screen.width / Screen.height 静默解析失败。
        /// </summary>
        public enum UiScreen { None, Title, Pause, GameOver }

        public Canvas Canvas { get; private set; }
        public UiScreen CurrentScreen { get; private set; } = UiScreen.None;

        GameObject _gameplayRoot;
        GameObject _titleRoot;
        GameObject _pauseRoot;
        GameObject _gameOverRoot;

        // ---- 状态条 ----
        Bar _barHealthCatch;   // 受伤残影（延迟追赶）
        Bar _barHealth;        // 主血条
        Bar _barHealthLoss;    // 已损失量（垫底）
        Bar _barXp;
        Bar _barDash;
        GameObject _healthText;
        GameObject _levelText;
        GameObject _dashText;

        GameObject _scoreText;
        GameObject _waveText;
        GameObject _weaponText;
        GameObject _comboText;
        GameObject _toastText;
        GameObject _statsText;
        GameObject _titleBest;
        GameObject _gameOverStats;
        GameObject _gameOverRecord;

        // ---- 插值状态 ----
        float _hpShown = 1f;      // 血条当前显示值
        float _hpCatch;           // 残影值
        float _dashShown;         // 冲刺条当前显示值
        float _xpShown;           // 经验条当前显示值
        float _hitPulse;          // 受伤时血条闪白
        float _toastTimer;
        RectTransform _comboRect;
        float _comboScaleTarget = 1f;
        float _comboScale = 1f;

        // 插值速度（越大越快）。
        // 血条：受击要**立刻**看出血少了，但保留一点点可见过程 ——
        // 早期设成 2.6 太慢，掉 80% 血要 1.5 秒才走完，看起来像"条根本没动"。
        // 残影故意留得更慢（约 0.9 秒走完），两者之间的浅色段就是"刚掉了多少"。
        const float HpLerpSpeed = 8f;
        const float HpCatchSpeed = 2.2f;
        const float DashLerpSpeed = 7f;
        const float XpLerpSpeed = 5f;

        static readonly Color BarBg = new Color(0.09f, 0.11f, 0.17f, 0.92f);
        static readonly Color BarBgSoft = new Color(0.14f, 0.16f, 0.24f, 0.75f);
        static readonly Color HealthLossColor = new Color(0.42f, 0.10f, 0.16f, 0.95f);
        static readonly Color HealthCatchColor = new Color(1f, 0.62f, 0.62f, 0.85f);

        /// <summary>
        /// 极简条状图形：直接从 fillAmount 生成一个四边形，**不依赖任何 sprite**。
        ///
        /// 为什么不用 `Image`：Unity 的 `Image` 在 `sprite == null` 时不会走"填充"分支
        /// （哪怕 `type` 设成 Filled），于是 `fillAmount` 被完全忽略、永远画满整块矩形。
        /// 这个坑非常隐蔽 —— 数值全对、Inspector 里 fillAmount 也在变，画面却纹丝不动。
        ///
        /// 所以这里自己继承 `MaskableGraphic`，在 `OnPopulateMesh` 里按比例生成四边形，
        /// 并把 `fillAmount` 做成"变了就 SetVerticesDirty"的属性。彻底绕开 Image 的实现细节。
        /// </summary>
        public class BarGraphic : MaskableGraphic
        {
            [SerializeField] float _fill = 1f;

            public float FillAmount
            {
                get { return _fill; }
                set
                {
                    float v = Mathf.Clamp01(value);
                    if (Mathf.Approximately(_fill, v)) return;
                    _fill = v;
                    SetVerticesDirty();
                }
            }

            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                if (_fill <= 0.0005f) return;      // 空网格 = 完全不画

                var r = GetPixelAdjustedRect();
                float w = r.width * _fill;
                float x0 = r.xMin;
                float x1 = r.xMin + w;

                vh.AddVert(new Vector3(x0, r.yMin), color, new Vector2(0f, 0f));
                vh.AddVert(new Vector3(x0, r.yMax), color, new Vector2(0f, 1f));
                vh.AddVert(new Vector3(x1, r.yMax), color, new Vector2(1f, 1f));
                vh.AddVert(new Vector3(x1, r.yMin), color, new Vector2(1f, 0f));

                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(2, 3, 0);
            }
        }

        /// <summary>一根状态条：一个自绘 graphic + 它的总宽度。</summary>
        class Bar
        {
            public BarGraphic Graphic;

            public Bar(BarGraphic g) { Graphic = g; }

            public void SetFill(float t) { Graphic.FillAmount = t; }

            public void SetColor(Color c) { Graphic.color = c; }
        }

        Bar NewBar(Transform parent, string name, Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            // pivot 放左侧中点 → 宽度从左边开始生长（血条从右往左退、经验条从左往右涨都正确）
            var rt = TextRenderer.SetupRect(go, Vector2.zero, Vector2.zero, anchoredPos, size);
            rt.pivot = new Vector2(0f, 0.5f);

            var g = go.AddComponent<BarGraphic>();
            g.raycastTarget = false;
            g.color = color;

            return new Bar(g);
        }

        // ------------------------------------------------------------------
        //  构建
        // ------------------------------------------------------------------

        public void Build(Camera cam)
        {
            EnsureEventSystem();

            var canvasGo = new GameObject("Canvas");
            canvasGo.transform.SetParent(transform, false);
            Canvas = canvasGo.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            Canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            BuildGameplay();
            BuildTitle();
            BuildPause();
            BuildGameOver();

            SetScreen(UiScreen.None);
            BindEvents();
        }

        /// <summary>
        /// 订阅局内事件。由 GameManager 在每次开局（EventBus.Clear 之后）调用，
        /// 保证"清空订阅"不会把 HUD 自己一起清掉。
        /// </summary>
        public void BindEvents()
        {
            EventBus.Subscribe<ToastEvent>(OnToast);
            EventBus.Subscribe<WaveEvent>(OnWave);
            EventBus.Subscribe<EnemyKilledEvent>(OnKill);
            EventBus.Subscribe<GameOverEvent>(OnGameOver);
        }

        static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        RectTransform Panel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return TextRenderer.SetupRect(go, anchorMin, anchorMax, anchoredPos, size);
        }

        /// <summary>创建一个水平填充条（自绘 graphic，不依赖 sprite）。</summary>
        // 旧的 Image 版 BarFill 已移除：Image 在 sprite==null 时会忽略 fillAmount，
        // 详见 BarGraphic 的注释。现在统一走 NewBar()。

        GameObject Label(Transform parent, string name, string content, float size, Color color,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 rectSize,
            TextAnchor align)
        {
            var go = TextRenderer.UiText(parent, name, content, size, color, align, false);
            TextRenderer.SetupRect(go, anchorMin, anchorMax, anchoredPos, rectSize);
            return go;
        }

        /// <summary>取/设文字内容，兼容 TMP 与 Legacy 两条路径。</summary>
        static void SetLabel(GameObject go, string text)
        {
            if (go == null) return;
            var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
            if (tmp != null) { tmp.text = text; return; }
            var t = go.GetComponent<Text>();
            if (t != null) t.text = text;
        }

        static Color GetLabelColor(GameObject go)
        {
            if (go == null) return Color.white;
            var tmp = go.GetComponent<TMPro.TextMeshProUGUI>();
            if (tmp != null) return tmp.color;
            var t = go.GetComponent<Text>();
            if (t != null) return t.color;
            return Color.white;
        }

        /// <summary>连击倍率之后的实际得分。</summary>
        public static int ScoreFor(int baseScore)
        {
            return Mathf.RoundToInt(baseScore * ComboTracker.Multiplier);
        }

        // ---------------- 战斗 HUD ----------------

        void BuildGameplay()
        {
            var root = new GameObject("Gameplay");
            root.transform.SetParent(Canvas.transform, false);
            var rt = root.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _gameplayRoot = root;

            var c = root.transform;

            // 左下角状态区：自上而下 生命 / 经验 / 冲刺
            const float left = 40f;
            const float width = 400f;

            // ---- 生命条（32 高）----
            // 绘制顺序即创建顺序（UGUI 后创建的在上面）：
            //   底层 = 已损失量（暗红，垫满整条）
            //   中层 = 残影（浅色，缓慢追赶主血条）
            //   顶层 = 主血条（亮红，随血量直接变化）
            // 早期把损失层放最上，主血条变短的过程被完全盖住，看起来就像血条没动。
            const float hpY = 84f;
            const float hpInset = 3f;
            const float hpW = width - 6f;
            const float hpH = 26f;
            Panel(c, "HealthBg", Vector2.zero, Vector2.zero, new Vector2(left, hpY),
                new Vector2(width, 32f), BarBg);

            _barHealthLoss = NewBar(c, "HealthLoss", new Vector2(left + hpInset, hpY + hpInset),
                new Vector2(hpW, hpH), HealthLossColor);
            _barHealthCatch = NewBar(c, "HealthCatch", new Vector2(left + hpInset, hpY + hpInset),
                new Vector2(hpW, hpH), HealthCatchColor);
            _barHealth = NewBar(c, "HealthFill", new Vector2(left + hpInset, hpY + hpInset),
                new Vector2(hpW, hpH), Palette.Health);

            _healthText = Label(c, "HealthText", "100 / 100", 18f, Color.white,
                Vector2.zero, Vector2.zero, new Vector2(left + width * 0.5f, hpY + 16f),
                new Vector2(width, 26f), TextAnchor.MiddleCenter);

            // ---- 经验条（14 高）----
            const float xpY = 56f;
            Panel(c, "XpBg", Vector2.zero, Vector2.zero, new Vector2(left, xpY),
                new Vector2(width, 14f), BarBgSoft);
            _barXp = NewBar(c, "XpFill", new Vector2(left + 2f, xpY + 2f),
                new Vector2(width - 4f, 10f), Palette.Xp);

            // ---- 冲刺冷却条（12 高）----
            const float dashY = 34f;
            Panel(c, "DashBg", Vector2.zero, Vector2.zero, new Vector2(left, dashY),
                new Vector2(width, 12f), BarBgSoft);
            _barDash = NewBar(c, "DashFill", new Vector2(left + 2f, dashY + 2f),
                new Vector2(width - 4f, 8f), Palette.Nova);

            // 状态文字放最下面一行，不挡条
            _levelText = Label(c, "LevelText", "LV.1", 16f, Palette.Good,
                Vector2.zero, Vector2.zero, new Vector2(left, 16f), new Vector2(width * 0.5f, 22f),
                TextAnchor.MiddleLeft);
            _dashText = Label(c, "DashText", "DASH ×1", 16f, Palette.Nova,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(left + width, 16f),
                new Vector2(width, 22f), TextAnchor.MiddleRight);

            // ---- 右下：当前武器 ----
            _weaponText = Label(c, "WeaponText", "脉冲手枪", 26f, Palette.Player,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-280f, 106f), new Vector2(540f, 34f),
                TextAnchor.MiddleRight);
            Label(c, "WeaponHint", "1 / 2 / 3 或滚轮 切换武器", 15f, Palette.TextDim,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-280f, 80f), new Vector2(540f, 22f),
                TextAnchor.MiddleRight);

            // ---- 右上：波次 / 分数 ----
            _waveText = Label(c, "WaveText", "第 1 波", 30f, Color.white,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-190f, -48f), new Vector2(340f, 40f),
                TextAnchor.MiddleRight);
            _scoreText = Label(c, "ScoreText", "SCORE 0", 22f, Palette.Warning,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-190f, -84f), new Vector2(340f, 30f),
                TextAnchor.MiddleRight);

            // ---- 中上：连杀 ----
            _comboText = Label(c, "ComboText", "", 46f, Palette.Warning,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -66f), new Vector2(600f, 60f),
                TextAnchor.MiddleCenter);
            _comboRect = _comboText.GetComponent<RectTransform>();

            // ---- 中上：提示条 ----
            _toastText = Label(c, "ToastText", "", 30f, Color.white,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(900f, 44f),
                TextAnchor.MiddleCenter);

            // ---- 左上：操作提示 ----
            Label(c, "HintText",
                "WASD 移动   鼠标 瞄准   左键 射击   Shift/空格 冲刺   1/2/3 换武器   Esc 暂停",
                15f, new Color(0.60f, 0.66f, 0.80f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -30f), new Vector2(1000f, 24f),
                TextAnchor.MiddleLeft);

            _gameplayRoot.SetActive(false);
        }

        // ---------------- 标题界面 ----------------

        void BuildTitle()
        {
            var root = Panel(Canvas.transform, "Title", Vector2.zero, Vector2.one, Vector2.zero,
                Vector2.zero, new Color(0.015f, 0.022f, 0.048f, 0.95f));
            _titleRoot = root.gameObject;

            var titleName = Label(root, "TitleName", "NEON VANGUARD", 92f, Palette.Player,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 210f),
                new Vector2(1400f, 110f), TextAnchor.MiddleCenter);
            TextRenderer.SetBold(titleName, true);

            Label(root, "TitleSub", "霓 虹 先 锋", 40f, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 138f),
                new Vector2(1200f, 54f), TextAnchor.MiddleCenter);

            Label(root, "TitleTag", "俯视角 · 波次生存 · 肉鸽升级 · 首领战", 22f, Palette.TextDim,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 86f),
                new Vector2(1200f, 34f), TextAnchor.MiddleCenter);

            Label(root, "TitleStart", "按 空格 / 回车 / 鼠标左键 开始游戏", 30f, Palette.Good,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f),
                new Vector2(1200f, 44f), TextAnchor.MiddleCenter);

            Label(root, "TitleHelp",
                "WASD 移动 · 鼠标瞄准 · 左键射击 · Shift/空格 冲刺 · 1/2/3 换武器 · Esc 暂停\n" +
                "击杀敌人掉落经验，升级时从三张强化卡中选一张；每 5 波出现首领。",
                20f, new Color(0.68f, 0.74f, 0.88f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -92f),
                new Vector2(1200f, 70f), TextAnchor.MiddleCenter);

            _titleBest = Label(root, "TitleBest", "", 22f, Palette.Warning,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 110f),
                new Vector2(1200f, 30f), TextAnchor.MiddleCenter);

            Label(root, "TitleQuit", "Esc 退出游戏", 19f, Palette.TextDim,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 74f),
                new Vector2(1200f, 26f), TextAnchor.MiddleCenter);

            Label(root, "TitleCredit", "Unity 2022.3 · 全部场景与资源由代码在运行时生成",
                17f, new Color(0.45f, 0.50f, 0.62f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f),
                new Vector2(1200f, 26f), TextAnchor.MiddleCenter);

            _titleRoot.SetActive(false);
        }

        // ---------------- 暂停 ----------------

        void BuildPause()
        {
            var root = Panel(Canvas.transform, "Pause", Vector2.zero, Vector2.one, Vector2.zero,
                Vector2.zero, new Color(0.015f, 0.022f, 0.045f, 0.94f));
            _pauseRoot = root.gameObject;

            var pauseTitle = Label(root, "PauseTitle", "已 暂 停", 68f, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 170f),
                new Vector2(1000f, 90f), TextAnchor.MiddleCenter);
            TextRenderer.SetBold(pauseTitle, true);

            _statsText = Label(root, "PauseStats", "", 26f, Palette.TextDim,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 50f),
                new Vector2(1000f, 120f), TextAnchor.MiddleCenter);

            Label(root, "PauseHelp",
                "Esc  继续游戏\n" +
                "R    重新开始\n" +
                "Q    返回标题\n" +
                "X    退出游戏",
                26f, Palette.Good,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -70f),
                new Vector2(1000f, 140f), TextAnchor.MiddleCenter);

            _pauseRoot.SetActive(false);
        }

        // ---------------- 结算 ----------------

        void BuildGameOver()
        {
            var root = Panel(Canvas.transform, "GameOver", Vector2.zero, Vector2.one, Vector2.zero,
                Vector2.zero, new Color(0.022f, 0.008f, 0.016f, 0.95f));
            _gameOverRoot = root.gameObject;

            var goTitle = Label(root, "GoTitle", "任 务 失 败", 78f, Palette.Danger,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 200f),
                new Vector2(1200f, 100f), TextAnchor.MiddleCenter);
            TextRenderer.SetBold(goTitle, true);

            _gameOverStats = Label(root, "GoStats", "", 32f, Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 50f),
                new Vector2(1200f, 220f), TextAnchor.MiddleCenter);

            _gameOverRecord = Label(root, "GoRecord", "", 30f, Palette.Warning,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -90f),
                new Vector2(1200f, 40f), TextAnchor.MiddleCenter);
            TextRenderer.SetBold(_gameOverRecord, true);

            Label(root, "GoHint", "按 空格 / 回车 / 鼠标左键 再来一局\nEsc 返回标题 · X 退出游戏", 26f, Palette.Good,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -170f),
                new Vector2(1200f, 80f), TextAnchor.MiddleCenter);

            _gameOverRoot.SetActive(false);
        }

        // ------------------------------------------------------------------
        //  界面切换（互斥）
        // ------------------------------------------------------------------

        /// <summary>
        /// 切换到指定整屏界面。**任何时刻最多只有一个界面可见**。
        /// 之前的问题是只处理了标题页，导致结算/暂停面板会残留并叠在一起。
        /// </summary>
        public void SetScreen(UiScreen screen)
        {
            CurrentScreen = screen;

            if (_gameplayRoot != null)
                _gameplayRoot.SetActive(screen == UiScreen.None);

            if (_titleRoot != null)
                _titleRoot.SetActive(screen == UiScreen.Title);

            if (_pauseRoot != null)
                _pauseRoot.SetActive(screen == UiScreen.Pause);

            if (_gameOverRoot != null)
                _gameOverRoot.SetActive(screen == UiScreen.GameOver);
        }

        public void ShowGameOver()
        {
            SetScreen(UiScreen.GameOver);
        }

        /// <summary>暂停面板打开时刷新统计数据。</summary>
        public void RefreshPauseStats()
        {
            if (_statsText == null) return;
            var p = GameManager.I != null ? GameManager.I.Player : null;
            SetLabel(_statsText, string.Format(
                "波次 {0}    分数 {1}    击杀 {2}\n生命 {3} / {4}    等级 {5}",
                RunState.Wave, RunState.Score, RunState.Kills,
                p != null ? Mathf.CeilToInt(Mathf.Max(0f, p.Health)) : 0,
                p != null ? Mathf.RoundToInt(p.Stats.MaxHealth) : 0,
                RunState.Level));
        }

        // ------------------------------------------------------------------
        //  每帧刷新
        // ------------------------------------------------------------------

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null) return;

            ComboTracker.Tick(Time.unscaledDeltaTime);

            var player = gm.Player;
            float dt = Time.unscaledDeltaTime;

            if (_gameplayRoot != null && _gameplayRoot.activeSelf && player != null)
                UpdateGameplayBars(player, gm, dt);

            UpdateToast(dt);

            if (_titleRoot != null && _titleRoot.activeSelf && _titleBest != null)
            {
                var save = SaveData.Current;
                SetLabel(_titleBest, save.TotalRuns > 0
                    ? string.Format("历史最佳：分数 {0}  ·  波次 {1}  ·  累计击杀 {2}",
                        save.BestScore, save.BestWave, save.TotalKills)
                    : "这是你的第一次挑战");
            }
        }

        /// <summary>
        /// 状态条的推进规则。
        ///
        /// falling 决定"目标变小时"怎么走，这是区分两类条的语义开关：
        ///   falling = true  → 下沉也要平滑（血条：受击后看得见它一格一格退下去，
        ///                     残影再以更慢的速度跟着退，把"刚掉了多少"标出来）
        ///   falling = false → 下沉瞬时（冲刺条：消耗的那一刻必须立刻归零，
        ///                     否则"我到底能不能冲"这个反馈会被动画拖住）
        /// 上升一律平滑 —— 经验累积、冲刺充能都要看得见过程。
        /// </summary>
        static float Approach(float current, float target, float speed, float dt, bool falling)
        {
            if (target < current && !falling) return target;
            float v = Mathf.Lerp(current, target, 1f - Mathf.Exp(-speed * dt));
            return Mathf.Abs(v - target) < 0.002f ? target : v;
        }

        void UpdateGameplayBars(Player player, GameManager gm, float dt)
        {            float targetHp = player.HealthNormalized;

            // ---- 生命 ----
            // 三条各自插值，形成"当前值 / 延迟残影 / 已损失量"的层次：
            //   主血条 = 真实血量，受击后可见地退回
            //   残影   = 更慢地追上去，把"刚刚掉了多少"标出来
            //   损失量 = 垫在残影之外，补满余下那段
            if (targetHp < _hpShown) _hitPulse = 1f;      // 掉血时闪一下
            // 主血条：下沉也平滑 —— 让玩家看见血在一格一格退
            _hpShown = Approach(_hpShown, targetHp, HpLerpSpeed, dt, true);

            // 残影：同样平滑下沉，但速度慢得多，于是会明显"落在"主血条后面，
            // 两者之间的浅色段就是"刚刚掉了多少"。回血时直接跟上，不留空洞。
            if (_hpCatch > _hpShown)
                _hpCatch = Approach(_hpCatch, _hpShown, HpCatchSpeed, dt, true);
            else
                _hpCatch = _hpShown;

            if (_barHealth != null) _barHealth.SetFill(_hpShown);
            if (_barHealthCatch != null) _barHealthCatch.SetFill(_hpCatch);
            if (_barHealthLoss != null) _barHealthLoss.SetFill(1f);

            _hitPulse = Mathf.Max(0f, _hitPulse - dt * 3.2f);
            if (_barHealth != null)
            {
                Color baseHp = player.HealthNormalized < 0.3f
                    ? Color.Lerp(Palette.Health, Color.white, 0.35f + 0.35f * Mathf.Sin(Time.unscaledTime * 9f))
                    : Palette.Health;
                _barHealth.SetColor(Color.Lerp(baseHp, Color.white, _hitPulse * 0.6f));
            }

            if (_healthText != null)
                SetLabel(_healthText, string.Format("{0} / {1}",
                    Mathf.CeilToInt(Mathf.Max(0f, player.Health)),
                    Mathf.RoundToInt(player.Stats.MaxHealth)));

            // ---- 经验 ----
            // 升级后 RunState.Xp 归零、XpRequired 变成新等级的门槛，
            // 于是目标值从接近 1 掉回接近 0，条子会明显"退回起点"再重新长起来。
            float targetXp = RunState.XpRequired > 0
                ? Mathf.Clamp01(RunState.Xp / (float)RunState.XpRequired) : 0f;
            _xpShown = Approach(_xpShown, targetXp, XpLerpSpeed, dt, false);
            if (_barXp != null) _barXp.SetFill(_xpShown);
            if (_levelText != null)
                SetLabel(_levelText, string.Format("LV.{0}   EXP {1}/{2}",
                    RunState.Level, RunState.Xp, RunState.XpRequired));

            // ---- 冲刺冷却 ----
            // DashCooldownNormalized 的定义是「当前这一层的充能进度 0..1」，
            // 所以整条的进度必须是：
            //      (已就绪层数 + 下一层的充能进度) / 总层数
            // 这样"刚用掉 → 空"、"冷却完 → 满"，方向永远正确。
            // （早期版本漏算了这个换算，导致满充能时显示空、用掉后反而显示满。）
            int maxCharges = Mathf.Max(1, player.MaxDashCharges);
            int charges = Mathf.Clamp(player.DashCharges, 0, maxCharges);
            float progress = charges >= maxCharges
                ? 1f
                : (charges + player.DashCooldownNormalized) / maxCharges;

            // 下沉瞬时：用掉冲刺的那一刻必须立刻看到条子清空
            _dashShown = Approach(_dashShown, progress, DashLerpSpeed, dt, false);

            if (_barDash != null)
            {
                _barDash.SetFill(_dashShown);
                bool ready = charges > 0;
                _barDash.SetColor(ready
                    ? Color.Lerp(Palette.Nova, Color.white, 0.25f)
                    : new Color(0.35f, 0.48f, 0.70f, 0.95f));
            }
            if (_dashText != null)
            {
                SetLabel(_dashText, string.Format("DASH  {0}/{1}{2}",
                    charges, maxCharges,
                    charges >= maxCharges ? "  就绪" : ""));
                TextRenderer.SetColor(_dashText, charges > 0 ? Palette.Nova : Palette.TextDim);
            }

            // ---- 文本区 ----
            if (_scoreText != null) SetLabel(_scoreText, "SCORE " + RunState.Score);
            if (_waveText != null)
                SetLabel(_waveText, string.Format("第 {0} 波  ·  剩余 {1}",
                    Mathf.Max(1, RunState.Wave),
                    gm.Waves != null ? gm.Waves.AliveCount + gm.Waves.RemainingToSpawn : 0));

            var def = WeaponLibrary.Get(player.CurrentWeapon);
            if (_weaponText != null)
            {
                SetLabel(_weaponText, def.Name);
                TextRenderer.SetColor(_weaponText, def.Tint);
            }

            // ---- 连杀 ----
            if (_comboText != null)
            {
                int combo = ComboTracker.Combo;
                if (combo >= 3)
                {
                    SetLabel(_comboText, string.Format("{0} COMBO   ×{1:0.0}", combo, ComboTracker.Multiplier));
                    _comboScaleTarget = Mathf.Lerp(1f, 1.3f, Mathf.Clamp01(ComboTracker.RecentHeat));
                }
                else
                {
                    SetLabel(_comboText, "");
                    _comboScaleTarget = 1f;
                }
                _comboScale = Mathf.Lerp(_comboScale, _comboScaleTarget, 1f - Mathf.Exp(-14f * dt));
                if (_comboRect != null) _comboRect.localScale = Vector3.one * _comboScale;
            }
        }

        void UpdateToast(float dt)
        {
            if (_toastTimer <= 0f) return;

            _toastTimer -= dt;
            if (_toastText != null)
            {
                var col = GetLabelColor(_toastText);
                // 最后 0.5 秒淡出，避免文字"啪"地消失
                col.a = Mathf.Clamp01(_toastTimer * 1.8f);
                TextRenderer.SetColor(_toastText, col);
            }
            if (_toastTimer <= 0f && _toastText != null) SetLabel(_toastText, "");
        }

        /// <summary>供自检读取当前条值，也允许自检强制刷新一帧以观察"瞬时归零"。</summary>
        public void DebugForceBarUpdate()
        {
            var gm = GameManager.I;
            if (gm != null && gm.Player != null) UpdateGameplayBars(gm.Player, gm, 0f);
        }

        /// <summary>供自检读取当前条值（用于验证"条确实在动"）。</summary>
        public float DebugHealthFill { get { return _hpShown; } }
        public float DebugHealthCatch { get { return _hpCatch; } }
        public float DebugXpFill { get { return _xpShown; } }
        public float DebugDashFill { get { return _dashShown; } }

        /// <summary>重开一局时把插值状态复位，避免血条从上一局的值"滑"过来。</summary>
        public void ResetBars()
        {
            var p = GameManager.I != null ? GameManager.I.Player : null;
            _hpShown = _hpCatch = p != null ? p.HealthNormalized : 1f;
            _xpShown = 0f;
            _dashShown = 1f;
            _hitPulse = 0f;
            _toastTimer = 0f;
            _comboScale = _comboScaleTarget = 1f;
            if (_barHealth != null) _barHealth.SetFill(_hpShown);
            if (_barHealthCatch != null) _barHealthCatch.SetFill(_hpCatch);
            if (_barHealthLoss != null) _barHealthLoss.SetFill(1f);
            if (_barXp != null) _barXp.SetFill(_xpShown);
            if (_barDash != null) _barDash.SetFill(_dashShown);
        }

        // ------------------------------------------------------------------
        //  事件响应
        // ------------------------------------------------------------------

        void OnToast(ToastEvent e)
        {
            if (_toastText == null) return;
            SetLabel(_toastText, e.Message);
            TextRenderer.SetColor(_toastText, e.Color);
            _toastTimer = 1.9f;
        }

        void OnWave(WaveEvent e)
        {
            if (_waveText == null) return;
            RunState.Wave = Mathf.Max(RunState.Wave, e.Wave);
        }

        void OnKill(EnemyKilledEvent e)
        {
            ComboTracker.Add();
        }

        void OnGameOver(GameOverEvent e)
        {
            ShowGameOver();

            if (_gameOverStats != null)
            {
                int minutes = Mathf.FloorToInt(e.TimeSurvived / 60f);
                int seconds = Mathf.FloorToInt(e.TimeSurvived % 60f);
                SetLabel(_gameOverStats, string.Format(
                    "存活 {0:00}:{1:00}      抵达第 {2} 波\n击杀 {3}      分数 {4}\n历史最佳 {5}",
                    minutes, seconds, e.Wave, e.Kills, e.Score, SaveData.Current.BestScore));
            }

            if (_gameOverRecord != null)
                SetLabel(_gameOverRecord, e.NewRecord ? "★ 新 纪 录 ★" : "");
        }

        void OnDestroy()
        {
            EventBus.Unsubscribe<ToastEvent>(OnToast);
            EventBus.Unsubscribe<WaveEvent>(OnWave);
            EventBus.Unsubscribe<EnemyKilledEvent>(OnKill);
            EventBus.Unsubscribe<GameOverEvent>(OnGameOver);
        }
    }

    /// <summary>
    /// 连杀计数器。短时间内连续击杀会累积 combo 并给出分数倍率，
    /// 这是"打得猛"与"苟着"之间最直接的收益差。
    /// </summary>
    public static class ComboTracker
    {
        public static int Combo { get; private set; }
        const float Window = 2.4f;

        static float _timer;
        static float _heat;

        public static float Multiplier
        {
            get { return 1f + Mathf.Min(2.5f, Combo * 0.03f); }
        }

        public static float RecentHeat
        {
            get { return _heat; }
        }

        public static void Add()
        {
            Combo++;
            _timer = Window;
            _heat = 1f;
        }

        public static void Reset()
        {
            Combo = 0;
            _timer = 0f;
            _heat = 0f;
        }

        /// <summary>由外部每帧驱动衰减。</summary>
        public static void Tick(float dt)
        {
            if (_timer > 0f)
            {
                _timer -= dt;
                if (_timer <= 0f) Combo = 0;
            }
            _heat = Mathf.Max(0f, _heat - dt * 3.2f);
        }
    }
}
