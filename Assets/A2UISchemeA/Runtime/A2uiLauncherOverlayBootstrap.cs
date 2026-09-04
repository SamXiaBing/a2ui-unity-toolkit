using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA
{
    /// <summary>
    /// Player / 台架：随 Launcher 启动自动挂上 A2UI 叠层宿主（不过 Editor Additive）。
    /// </summary>
    public class A2uiLauncherOverlayBootstrap : MonoBehaviour
    {
        public static A2uiLauncherOverlayBootstrap Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoBoot()
        {
            if (!Application.isPlaying) return;
            // Editor 下可用菜单 Additive；Player 必启。Editor Play Launcher 也可启以便联调。
            if (Instance != null) return;
            var go = new GameObject("A2uiLauncherOverlayBootstrap");
            DontDestroyOnLoad(go);
            go.AddComponent<A2uiLauncherOverlayBootstrap>();
        }

        [SerializeField] bool onlyWhenLauncherScene = true;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        void Start()
        {
            if (onlyWhenLauncherScene && !IsLauncherContext())
            {
                Debug.Log("[A2UI] bootstrap skip: not launcher context");
                return;
            }

            EnsureHost();
        }

        static bool IsLauncherContext()
        {
            // 不直接引用宿主工程的 LauncherScene 类型（插件不能反向依赖业务代码）：
            // 按类型名反射探测，找不到类型即视为非 Launcher 上下文。
            var launcherSceneType = System.Type.GetType("LauncherScene, Assembly-CSharp");
            if (launcherSceneType != null)
            {
                var any = typeof(Object).GetMethod("FindObjectOfType", new System.Type[0])
                    ?.MakeGenericMethod(launcherSceneType)
                    .Invoke(null, null);
                if (any != null) return true;
            }
            var n = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? "";
            return n.IndexOf("Launcher", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static A2uiLauncherSurfaceHost EnsureHost()
        {
            var existing = Object.FindObjectOfType<A2uiLauncherSurfaceHost>();
            if (existing != null)
            {
                Debug.Log("[A2UI] overlay host already present");
                return existing;
            }

            var go = new GameObject("A2uiLauncherSurfaceHost");
            DontDestroyOnLoad(go);
            var doc = go.AddComponent<UIDocument>();
            var host = go.AddComponent<A2uiLauncherSurfaceHost>();
            host.enableLiveServer = true;
            host.autoStartTimeline = false;
            host.targetDisplayIndex = 0;
            host.tokenVariant = "a";
            host.jsonlRelativePath = "Assets/A2UISchemeA/Samples/timeline_bench/01_media.v0.8.jsonl";

#if UNITY_EDITOR
            var so = new UnityEditor.SerializedObject(host);
            so.FindProperty("craftedStyle").objectReferenceValue =
                UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/A2UISchemeA/Styles/Crafted.uss");
            so.FindProperty("tokensStyle").objectReferenceValue =
                UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/A2UISchemeA/Styles/Tokens.uss");
            so.FindProperty("panelSettingsAsset").objectReferenceValue =
                UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/A2UISchemeA/PanelSettings.asset");
            so.FindProperty("runtimeTheme").objectReferenceValue =
                UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                    "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            so.ApplyModifiedPropertiesWithoutUndo();
#endif
            // Player：Resources 可选；无引用时 Host OnEnable 仍会建默认 PanelSettings
            Debug.Log("[A2UI] overlay host created for runtime");
            return host;
        }
    }
}
