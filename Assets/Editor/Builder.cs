using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NeonVanguard.EditorTools
{
    /// <summary>
    /// 一键构建脚本。支持命令行无头构建，方便 CI 或本地复现：
    ///
    ///   Unity.exe -quit -batchmode -nographics -projectPath &lt;工程路径&gt; \
    ///             -executeMethod NeonVanguard.EditorTools.Builder.BuildWindows
    ///
    /// 之所以需要它，是因为本工程的场景内容完全由运行时代码生成，
    /// 场景文件本身只需要是一个合法的空场景，脚本会在构建前自动生成并加入 Build Settings。
    /// </summary>
    public static class Builder
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string ProductName = "NeonVanguard";

        [MenuItem("Neon Vanguard/准备场景 (Prepare Scene)")]
        public static void PrepareScene()
        {
            EnsureSceneFile();
            EnsureBuildSettings();
            EnsureTmpEssentials();
            AssetDatabase.SaveAssets();
            Debug.Log("[Builder] 场景已准备：" + ScenePath);
        }

        /// <summary>
        /// 导入 TextMeshPro 的 Essential Resources。
        ///
        /// 这一步不能省：工程的 UI 全部走 TMP，而 TMP 的 shader / 默认字体资产
        /// 并不在包缓存里，必须导入到 Assets/TextMesh Pro 下才会被打进构建。
        /// 否则运行时 TMP_FontAsset.CreateFontAsset 会因为 "shader is null" 失败
        /// （这个坑实际踩到过，最后是靠无头自检的日志发现的）。
        ///
        /// 注意：TMP 自带的 TMP_PackageResourceImporter.ImportResources 是打开窗口 +
        /// 协程的异步流程，在 -batchmode 下不会真正落地文件；所以这里直接
        /// 用 AssetDatabase.ImportPackage 同步导入它自带的 .unitypackage，可靠得多。
        /// </summary>
        static void EnsureTmpEssentials()
        {
            const string marker = "Assets/TextMesh Pro/Shaders";

            if (System.IO.Directory.Exists(marker)) return;

            try
            {
                string pkgPath = ResolveTmpEssentialsPath();
                if (string.IsNullOrEmpty(pkgPath))
                {
                    // 退一步：仍然尝试 TMP 官方导入器
                    Debug.LogWarning("[Builder] 未找到 TMP Essential Resources 包，改用 TMP 官方导入器。");
                    TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
                    AssetDatabase.Refresh();
                    return;
                }

                Debug.Log("[Builder] 正在导入 TMP Essential Resources：" + pkgPath);
                AssetDatabase.ImportPackage(pkgPath, false);   // interactive: false = 不弹窗，同步导入
                AssetDatabase.Refresh();

                if (System.IO.Directory.Exists(marker))
                    Debug.Log("[Builder] TMP Essential Resources 导入成功。");
                else
                    Debug.LogWarning("[Builder] TMP Essential Resources 导入后仍未找到 shader 目录，UI 将回退到 Legacy Text。");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Builder] 导入 TMP Essential Resources 失败（UI 会回退到 Legacy Text）：" + e.Message);
            }
        }

        /// <summary>通过 UPM 包信息定位 TMP 包自带的 .unitypackage 绝对路径。</summary>
        static string ResolveTmpEssentialsPath()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMPro.TMP_FontAsset).Assembly);
                if (info == null || string.IsNullOrEmpty(info.resolvedPath)) return null;

                string p = Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
                return File.Exists(p) ? p : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Builder] 解析 TMP 包路径失败：" + e.Message);
                return null;
            }
        }

        [MenuItem("Neon Vanguard/构建 Windows 版本 (Build Windows)")]
        public static void BuildWindows()
        {
            try
            {
                EnsureSceneFile();
                EnsureBuildSettings();
                EnsureTmpEssentials();

                string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Build", "Windows");
                Directory.CreateDirectory(outDir);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = Path.Combine(outDir, ProductName + ".exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                };

                Debug.Log("[Builder] 开始构建：" + options.locationPathName);
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log(string.Format("[Builder] 构建成功：{0}，大小 {1:0.0} MB，耗时 {2:0.0} 秒",
                        options.locationPathName, summary.totalSize / 1048576f, summary.totalTime.TotalSeconds));
                    EditorApplication.Exit(0);
                }
                else
                {
                    Debug.LogError(string.Format("[Builder] 构建失败：{0}，错误 {1} 个",
                        summary.result, summary.totalErrors));
                    EditorApplication.Exit(1);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Builder] 构建异常：" + e);
                EditorApplication.Exit(1);
            }
        }

        [MenuItem("Neon Vanguard/构建 WebGL 版本 (Build WebGL)")]
        public static void BuildWebGL()
        {
            try
            {
                EnsureSceneFile();
                EnsureBuildSettings();
                EnsureTmpEssentials();

                string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Build", "WebGL");
                Directory.CreateDirectory(outDir);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = outDir,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None
                };

                Debug.Log("[Builder] 开始构建 WebGL：" + outDir);
                BuildReport report = BuildPipeline.BuildPlayer(options);
                var summary = report.summary;
                if (summary.result == BuildResult.Succeeded)
                {
                    Debug.Log("[Builder] WebGL 构建成功：" + outDir);
                    EditorApplication.Exit(0);
                }
                else
                {
                    Debug.LogError("[Builder] WebGL 构建失败：" + summary.result);
                    EditorApplication.Exit(1);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[Builder] WebGL 构建异常：" + e);
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// 打开工程时自动补齐场景与构建设置，保证 clone 下来直接就能按 Play / Build。
        /// </summary>
        [InitializeOnLoadMethod]
        static void AutoSetup()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    EnsureSceneFile();
                    EnsureBuildSettings();
                    EnsureTmpEssentials();
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Builder] 自动准备场景失败（可手动执行菜单 Neon Vanguard/准备场景）：" + e.Message);
                }
            };
        }

        // ------------------------------------------------------------------

        static void EnsureSceneFile()
        {
            if (File.Exists(ScenePath)) return;

            var dir = Path.GetDirectoryName(ScenePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // 场景内容为空：真正的舞台由 Bootstrap 在运行时搭建
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[Builder] 已生成空场景：" + ScenePath);
        }

        static void EnsureBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == ScenePath)
                {
                    if (!scenes[i].enabled) scenes[i].enabled = true;
                    return;
                }
            }

            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(scenes);
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[Builder] 已把场景加入 Build Settings。");
        }
    }
}
