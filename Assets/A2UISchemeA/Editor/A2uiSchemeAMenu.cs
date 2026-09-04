using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA.Editor
{
    public static class A2uiSchemeAMenu
    {
        const string ScenePath = "Assets/A2UISchemeA/Scenes/A2UISchemeA.unity";
        const string LauncherHostPath = "Assets/A2UISchemeA/Scenes/A2UILauncherHost.unity";
        const string LauncherScenePath = "Assets/Scenes/Launcher.scene";
        const string PanelPath = "Assets/A2UISchemeA/PanelSettings.asset";

        [MenuItem("A2UI Scheme A/打开场景并准备 Play")]
        public static void Open()
        {
            EnsureScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorUtility.DisplayDialog(
                "A2UI Scheme A",
                "G0–G5 验证床\nTabs: Coverage|Live|Scenario|Craft|ClosedLoop|Gate|Degrade\nExportSession 导出回放包",
                "OK");
        }

        [MenuItem("A2UI Scheme A/打开 Launcher + A2UI 叠层")]
        public static void OpenLauncherWithOverlay()
        {
            EnsureLauncherHostSceneInternal(openAfter: false);
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(LauncherScenePath)))
            {
                EditorUtility.DisplayDialog("A2UI Scheme A", "找不到 " + LauncherScenePath, "OK");
                return;
            }

            EditorSceneManager.OpenScene(LauncherScenePath, OpenSceneMode.Single);
            EditorSceneManager.OpenScene(LauncherHostPath, OpenSceneMode.Additive);
            var host = Object.FindObjectOfType<A2uiLauncherSurfaceHost>();
            if (host != null)
            {
                var so = new SerializedObject(host);
                so.FindProperty("enableLiveServer").boolValue = true;
                // Editor 联调默认关闭时间轴，避免热推卡片被自动换卡冲掉
                so.FindProperty("autoStartTimeline").boolValue = false;
                so.FindProperty("jsonlRelativePath").stringValue =
                    "Assets/A2UISchemeA/Samples/timeline_bench/01_media.v0.8.jsonl";
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(host.gameObject.scene);
            }

            Debug.Log("[A2UISchemeA] Launcher + A2UI overlay ready. Press Play：3D + 底部卡片 + 时间轴。");
            EditorUtility.DisplayDialog(
                "A2UI Scheme A",
                "已打开 Launcher.scene，并 Additive 加载 A2UI 薄宿主。\n\n按 Play：看 Game 视图的 Display 1。\n已关闭时间轴自动换卡（避免热推被冲掉）。\n需要时再 ContextMenu → Start Timeline。\nLive：http://127.0.0.1:18766/a2ui",
                "OK");
        }

        [MenuItem("A2UI Scheme A/创建 Launcher 薄宿主场景")]
        public static void EnsureLauncherHostScene()
        {
            EnsureLauncherHostSceneInternal(openAfter: true);
        }

        static void EnsureLauncherHostSceneInternal(bool openAfter)
        {
            EnsureFolder("Assets/A2UISchemeA/Scenes");
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(LauncherHostPath)))
                AssetDatabase.DeleteAsset(LauncherHostPath);

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                EnsureScene();
                panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            }

            if (panel != null)
            {
                panel.sortingOrder = 100;
                panel.clearColor = false;
                EditorUtility.SetDirty(panel);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("A2uiLauncherSurfaceHost");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            var host = go.AddComponent<A2uiLauncherSurfaceHost>();
            go.AddComponent<A2uiTimelineDriver>();
            var so = new SerializedObject(host);
            so.FindProperty("craftedStyle").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/A2UISchemeA/Styles/Crafted.uss");
            so.FindProperty("tokensStyle").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/A2UISchemeA/Styles/Tokens.uss");
            if (theme != null)
                so.FindProperty("runtimeTheme").objectReferenceValue = theme;
            so.FindProperty("panelSettingsAsset").objectReferenceValue = panel;
            so.FindProperty("enableLiveServer").boolValue = true;
            so.FindProperty("autoStartTimeline").boolValue = false;
            so.FindProperty("tokenVariant").stringValue = "ds";
            so.FindProperty("jsonlRelativePath").stringValue =
                "Assets/A2UISchemeA/Samples/timeline_bench/01_media.v0.8.jsonl";
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, LauncherHostPath);
            AssetDatabase.SaveAssets();
            if (openAfter)
                EditorSceneManager.OpenScene(LauncherHostPath, OpenSceneMode.Single);
            Debug.Log("[A2UISchemeA] Launcher thin host scene ready: " + LauncherHostPath);
        }

        [MenuItem("A2UI Scheme A/强制重建场景")]
        public static void EnsureScene()
        {
            EnsureFolder("Assets/A2UISchemeA");
            EnsureFolder("Assets/A2UISchemeA/Scenes");

            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ScenePath)))
                AssetDatabase.DeleteAsset(ScenePath);

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            var panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.clearColor = false;
            panel.sortingOrder = 100;
            if (theme != null) panel.themeStyleSheet = theme;
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(PanelPath)))
                AssetDatabase.DeleteAsset(PanelPath);
            AssetDatabase.CreateAsset(panel, PanelPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Object.FindObjectOfType<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.backgroundColor = Color.black;
            }
            RenderSettings.skybox = new Material(Shader.Find("Skybox/Procedural"));

            var go = new GameObject("A2uiSchemeAHost");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            var host = go.AddComponent<A2uiSchemeAHost>();
            var so = new SerializedObject(host);
            so.FindProperty("hostStyle").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/A2UISchemeA/Styles/Host.uss");
            so.FindProperty("craftedStyle").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/A2UISchemeA/Styles/Crafted.uss");
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[A2UISchemeA] scene ready");
        }

        [MenuItem("A2UI Scheme A/创建 Overlay Config (Resources)")]
        public static void CreateOverlayConfig()
        {
            EnsureFolder("Assets/A2UISchemeA/Resources");
            const string path = "Assets/A2UISchemeA/Resources/A2uiOverlayConfig.asset";
            var cfg = AssetDatabase.LoadAssetAtPath<A2uiOverlayConfig>(path);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<A2uiOverlayConfig>();
                AssetDatabase.CreateAsset(cfg, path);
            }

            cfg.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            cfg.runtimeTheme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            cfg.craftedStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/A2UISchemeA/Styles/Crafted.uss");
            cfg.tokensStyle = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/A2UISchemeA/Styles/Tokens.uss");
            cfg.defaultJsonlRelativePath = "Assets/A2UISchemeA/Samples/timeline_bench/01_media.v0.8.jsonl";
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.FocusProjectWindow();
            Selection.activeObject = cfg;
            Debug.Log("[A2UISchemeA] Overlay Config ready: " + path + "（打进 Player 供台架自举）");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var cur = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDirectoryExists(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        static bool AssetDirectoryExists(string path) => AssetDatabase.IsValidFolder(path);

        /// <summary>
        /// 编辑器内一键截图当前渲染的卡片（GUI 编辑器有渲染帧，readback 合法）。
        /// 输出 TestResults/screenshots/{主题}/{样本}.png，可配 Tools/regression_diff.py --update 做基线。
        /// batchmode 无渲染帧，这个菜单是截图时效验的唯一可靠路径。
        /// </summary>
        [MenuItem("A2UI Scheme A/捕获当前卡片截图 (A2UI_CAPTURE)")]
        public static void CaptureCurrentCard()
        {
            var host = Object.FindObjectOfType<A2uiSchemeAHost>();
            if (host == null)
            {
                Debug.LogError("[Capture] 场景里没有 A2uiSchemeAHost（先 Play A2UITestBed）");
                return;
            }
            // 延迟到下一帧末尾 readback（GUI 编辑器有 GameView 渲染帧）
            EditorApplication.delayCall += () => EditorApplication.update += CaptureUpdater;
        }

        static int _captureFrames;
        static void CaptureUpdater()
        {
            _captureFrames++;
            if (_captureFrames < 2) return; // 等两帧确保布局/渲染稳定
            EditorApplication.update -= CaptureUpdater;
            _captureFrames = 0;

            var host = Object.FindObjectOfType<A2uiSchemeAHost>();
            if (host == null) return;
            var tex = CaptureGameViewAsTexture();
            if (tex == null) return;

            var dir = Path.Combine(Application.dataPath, "..", "TestResults", "screenshots");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "capture_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            global::System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            Debug.Log("[Capture] saved: " + path);
        }

        static Texture2D CaptureGameViewAsTexture()
        {
            var gameView = GetMainGameView();
            if (gameView == null)
            {
                Debug.LogError("[Capture] 找不到 GameView（请保持在 Game 视图）");
                return null;
            }
            // 直接读屏（GUI 编辑器 GameView 可见时合法）
            var width = Screen.width;
            var height = Screen.height;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            return tex;
        }

        static EditorWindow GetMainGameView()
        {
            var t = System.Type.GetType("UnityEditor.GameView, UnityEditor");
            if (t == null) return null;
            return EditorWindow.GetWindow(t);
        }
    }
}
