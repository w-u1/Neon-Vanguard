using UnityEngine;
using UnityEngine.Rendering;

namespace NeonVanguard
{
    /// <summary>
    /// 场景构建器 —— 本工程的核心工程决策之一。
    ///
    /// 整个游戏**没有任何预制体、没有手拖引用、没有美术资源**：相机、光照、竞技场、
    /// 玩家、敌人、UI 全部在这一个文件里用代码搭建。
    /// 好处是工程可以干净地进版本库（无二进制资源、无 GUID 断裂风险），
    /// 任何人 clone 下来打开就能直接按 Play 跑起来。
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        /// <summary>
        /// 关键：不用手工放场景对象，游戏启动时自动注入入口。
        /// 无论是编辑器按 Play，还是打包后的 exe 启动，都会走到这里。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void AutoBoot()
        {
            if (FindObjectOfType<Bootstrap>() != null) return;
            var go = new GameObject("[NeonVanguard]");
            go.AddComponent<Bootstrap>();
        }

        public Camera Cam { get; private set; }
        public Transform CameraRig { get; private set; }
        public Arena Arena { get; private set; }

        void Awake()
        {
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 0;

            // 默认全屏；带 -nvt-selftest 时改为小窗口，
            // 方便在开发机上完整显示并截图核对画面。
            if (SelfTest.MaybeSelfTestRequested())
            {
                Screen.SetResolution(1024, 576, FullScreenMode.Windowed);
            }
            else
            {
                Screen.SetResolution(1600, 900, FullScreenMode.FullScreenWindow);
            }

            TextRenderer.Probe();
            BuildEnvironment();
            BuildSystems();
        }

        // ------------------------------------------------------------------
        //  相机与环境
        // ------------------------------------------------------------------

        void BuildEnvironment()
        {
            // ---- 相机（正交俯视 58°，类似 Hades / 吸血鬼幸存者的取景）----
            var rig = new GameObject("CameraRig");
            CameraRig = rig.transform;

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(rig.transform, false);
            camGo.transform.localPosition = new Vector3(0f, 24f, -13.5f);
            camGo.transform.localRotation = Quaternion.Euler(58f, 0f, 0f);

            Cam = camGo.AddComponent<Camera>();
            Cam.orthographic = true;
            Cam.orthographicSize = 11.5f;
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 160f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = Palette.Bg;
            Cam.allowHDR = false;

            camGo.AddComponent<AudioListener>();

            // ---- 光照：暗色舞台 + 一点方向光，主要靠自发光材质出效果 ----
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.52f, 0.56f, 0.70f);

            // 雾效：只用来给场外远景收边。
            // 起点必须大于"相机到场地远端的距离"（相机高 24、水平后退 13.5，到远端约 75），
            // 否则整块场地会被刷成背景色 —— 这个坑实际踩到过，表现为"地面整个消失"。
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Palette.Bg;
            RenderSettings.fogStartDistance = 58f;
            RenderSettings.fogEndDistance = 125f;

            var keyGo = new GameObject("KeyLight");
            keyGo.transform.SetParent(transform, false);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(0.85f, 0.90f, 1f);
            key.intensity = 0.75f;
            key.shadows = LightShadows.None;   // 俯视小场景，阴影开销大且收益低
            keyGo.transform.rotation = Quaternion.Euler(62f, 34f, 0f);

            var fillGo = new GameObject("FillLight");
            fillGo.transform.SetParent(transform, false);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.45f, 0.40f, 0.85f);
            fill.intensity = 0.35f;
            fill.shadows = LightShadows.None;
            fillGo.transform.rotation = Quaternion.Euler(28f, -140f, 0f);

            // ---- 竞技场 ----
            var arenaGo = new GameObject("Arena");
            arenaGo.transform.SetParent(transform, false);
            Arena = arenaGo.AddComponent<Arena>();
            Arena.Build(arenaGo.transform, 30f);
        }

        // ------------------------------------------------------------------
        //  系统装配
        // ------------------------------------------------------------------

        void BuildSystems()
        {
            // 对象池根节点
            var poolRoot = new GameObject("Pools");
            poolRoot.transform.SetParent(transform, false);

            // 音频
            var audio = new GameObject("Audio").AddComponent<AudioKit2>();
            audio.transform.SetParent(transform, false);
            audio.Init();

            // 特效/打击感（必须在相机建好之后，它会缓存相机的基础位姿）
            var juice = new GameObject("Juice").AddComponent<Juice>();
            juice.transform.SetParent(transform, false);
            juice.Init(Cam);

            // 玩家
            var playerGo = new GameObject("Player");
            playerGo.transform.SetParent(transform, false);
            playerGo.tag = "Player";
            var player = playerGo.AddComponent<Player>();
            player.Build();

            // 波次导演
            var waves = new GameObject("WaveDirector").AddComponent<WaveDirector>();
            waves.transform.SetParent(transform, false);

            // UI
            var uiRoot = new GameObject("UI");
            uiRoot.transform.SetParent(transform, false);
            var hud = uiRoot.AddComponent<HudController>();
            hud.Build(Cam);

            var upgradeGo = new GameObject("UpgradePanel");
            upgradeGo.transform.SetParent(uiRoot.transform, false);
            var upgrades = upgradeGo.AddComponent<UpgradePanel>();
            upgrades.Build(hud.Canvas);

            // 总控
            var gm = gameObject.AddComponent<GameManager>();
            gm.Arena = Arena;
            gm.Configure(player, waves, hud, upgrades, Cam);

            // 相机跟随交给 Player 处理（需要玩家引用）
            player.AttachCameraRig(CameraRig, Cam);

            // 先回到标题界面（SetScreen 会保证只有标题页可见）
            hud.SetScreen(HudController.UiScreen.Title);
            gm.ReturnToTitle();

            // 无头自检：带 -nvt-selftest 参数时自动接管操作并输出报告（详见 SelfTest.cs）。
            // 必须放在 ReturnToTitle 之后，因为自检的 StartRun 会直接调用 BeginRun 进入战斗。
            SelfTest.MaybeStart(transform);
        }
    }
}
