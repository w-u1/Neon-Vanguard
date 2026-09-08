using UnityEngine;

namespace NeonVanguard
{
    public enum GameState
    {
        Title,      // 标题界面
        Playing,    // 正常战斗
        LevelUp,    // 升级三选一（时间被冻结）
        Paused,     // 暂停
        Dead        // 结算界面
    }

    /// <summary>
    /// 游戏流程总控：状态机 + 局内生命周期 + 输入路由。
    /// 所有构建流程在 Bootstrap 里完成，本类只负责"什么时候允许发生什么"。
    ///
    /// 界面切换统一走 HudController.SetScreen，保证任何时刻只有一个整屏界面可见
    /// （早期版本只切换了标题页，导致死亡后重开时结算面板叠在标题上）。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager I;

        public GameState State { get; private set; } = GameState.Title;

        public Player Player { get; private set; }
        public WaveDirector Waves { get; private set; }
        public HudController Hud { get; private set; }
        public UpgradePanel Upgrades { get; private set; }
        public Camera Cam { get; private set; }
        public Arena Arena { get; set; }

        bool _recordThisRun;

        public void Configure(Player player, WaveDirector waves, HudController hud,
            UpgradePanel upgrades, Camera cam)
        {
            I = this;
            Player = player;
            Waves = waves;
            Hud = hud;
            Upgrades = upgrades;
            Cam = cam;
        }

        void Update()
        {
            RunState.Elapsed += Time.unscaledDeltaTime;

            // 退出游戏：暂停菜单 / 结算页 / 标题页都支持
            if (Input.GetKeyDown(KeyCode.X)
                && (State == GameState.Paused || State == GameState.Dead || State == GameState.Title))
            {
                QuitGame();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (State == GameState.Playing) SetPaused(true);
                else if (State == GameState.Paused) SetPaused(false);
                else if (State == GameState.Dead) ReturnToTitle();
                else if (State == GameState.Title) QuitGame();
                return;
            }

            if (State == GameState.Title || State == GameState.Dead)
            {
                if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)
                    || Input.GetMouseButtonDown(0))
                {
                    BeginRun();
                }
            }

            // 暂停菜单
            if (State == GameState.Paused)
            {
                if (Input.GetKeyDown(KeyCode.R)) BeginRun();
                if (Input.GetKeyDown(KeyCode.Q)) ReturnToTitle();
            }

            // 升级面板的多选（数字键 / 鼠标点击）
            if (State == GameState.LevelUp && Upgrades != null)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) Upgrades.Choose(0);
                if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) Upgrades.Choose(1);
                if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) Upgrades.Choose(2);
            }
        }

        // ------------------------------------------------------------------
        //  流程控制
        // ------------------------------------------------------------------

        public void BeginRun()
        {
            Pool.ClearAll();

            // 重开一局时重建事件订阅。
            // 必须"先清空、再让各系统重新订阅"：上一局的订阅者可能引用即将被复用的对象；
            // 而 SelfTest 这类后创建的系统也必须在清空之后才订阅，否则会被一起抹掉
            // （这是自检抓到过的一个真实 bug）。
            EventBus.Clear();

            // 关键：取消可能还在排队的"死亡恢复时间缩放"。
            // 否则上一局的慢镜头会在新一局中途把 timeScale 拉回 1，干扰升级暂停。
            CancelInvoke(nameof(RestoreTime));

            RunState.Reset();
            ComboTracker.Reset();
            _recordThisRun = false;
            Time.timeScale = 1f;

            if (Player != null) Player.ResetForNewRun();
            if (Waves != null) Waves.ResetForNewRun();
            if (Upgrades != null) Upgrades.ResetForNewRun();

            if (Hud != null)
            {
                Hud.ResetBars();
                // 显示战斗 HUD，同时把结算页 / 暂停页 / 标题页全部关掉
                Hud.SetScreen(HudController.UiScreen.None);
            }

            RebindEventSubscriptions();

            State = GameState.Playing;
            if (Waves != null) Waves.BeginNextWave();

            AudioKit2.PlayUi(AudioKit.Select());
            EventBus.Publish(new ToastEvent { Message = "生存开始！", Color = Palette.Good });
        }

        /// <summary>
        /// 让所有需要监听事件的对象重新订阅。
        /// 用 FindObjectsOfType 而不是维护一份列表：订阅方都挂在激活对象上，
        /// 这样新增订阅者时不需要回来改这里。
        /// </summary>
        static void RebindEventSubscriptions()
        {
            var huds = FindObjectsOfType<HudController>();
            for (int i = 0; i < huds.Length; i++) huds[i].BindEvents();

            var tests = FindObjectsOfType<SelfTest>();
            for (int i = 0; i < tests.Length; i++) tests[i].BindEvents();
        }

        public void ReturnToTitle()
        {
            Time.timeScale = 1f;
            CancelInvoke(nameof(RestoreTime));

            if (Upgrades != null) Upgrades.Hide();
            if (Waves != null) Waves.StopAll();
            if (Hud != null) Hud.SetScreen(HudController.UiScreen.Title);
            State = GameState.Title;
        }

        public void SetPaused(bool paused)
        {
            if (paused && State == GameState.Playing)
            {
                State = GameState.Paused;
                Time.timeScale = 0f;
                if (Hud != null)
                {
                    Hud.RefreshPauseStats();
                    Hud.SetScreen(HudController.UiScreen.Pause);
                }
                AudioKit2.PlayUi(AudioKit.Select());
            }
            else if (!paused && State == GameState.Paused)
            {
                State = GameState.Playing;
                Time.timeScale = 1f;
                if (Hud != null) Hud.SetScreen(HudController.UiScreen.None);
                AudioKit2.PlayUi(AudioKit.Select());
            }
        }

        /// <summary>退出游戏。编辑器里停掉播放模式，构建版本里真正结束进程。</summary>
        public void QuitGame()
        {
            AudioKit2.PlayUi(AudioKit.Select());
            Debug.Log("[GameManager] 退出游戏。");
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>玩家升级：暂停世界并弹出三选一。</summary>
        public void OfferUpgrade(int level)
        {
            if (State != GameState.Playing) return;
            State = GameState.LevelUp;
            Time.timeScale = 0f;
            if (Upgrades != null) Upgrades.Show(level);
        }

        public void OnUpgradeChosen()
        {
            if (Upgrades != null) Upgrades.Hide();
            if (State != GameState.LevelUp) return;
            State = GameState.Playing;
            Time.timeScale = 1f;
        }

        public void AddKill(int score)
        {
            RunState.Kills++;
            RunState.AddScore(HudController.ScoreFor(score));
        }

        public void OnPlayerDied()
        {
            if (State == GameState.Dead) return;

            // 死亡慢镜头，让"结束"这一刻有分量
            State = GameState.Dead;
            Time.timeScale = 0.28f;
            CancelInvoke(nameof(RestoreTime));
            Invoke(nameof(RestoreTime), 1.1f);

            _recordThisRun = SaveData.Current.CommitRun(
                RunState.Score, RunState.Wave, RunState.Kills, RunState.Elapsed);

            AudioKit2.PlayUi(AudioKit.Death());
            if (Juice.I != null) Juice.I.Shake(0.9f);

            // HudController 收到 GameOverEvent 后会切到结算界面
            EventBus.Publish(new GameOverEvent
            {
                Wave = RunState.Wave,
                Score = RunState.Score,
                Kills = RunState.Kills,
                TimeSurvived = RunState.Elapsed,
                NewRecord = _recordThisRun
            });
        }

        void RestoreTime()
        {
            Time.timeScale = 1f;
        }

        public void Shake(float s)
        {
            if (Juice.I != null) Juice.I.Shake(s);
        }
    }
}
