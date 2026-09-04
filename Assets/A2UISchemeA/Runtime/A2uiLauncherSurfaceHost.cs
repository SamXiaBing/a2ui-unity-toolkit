using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using TextCoreFontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace A2UISchemeA
{
    /// <summary>
    /// Launcher 薄宿主：3D 叠层 + HTTP/TCP/inbox 热推（adb forward 直达台架）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class A2uiLauncherSurfaceHost : MonoBehaviour
    {
        public string jsonlRelativePath = "Assets/A2UISchemeA/Samples/prompt_media.v0.8.jsonl";
        public bool enableLiveServer = true;
        public int livePort = A2uiSchemeACommandServer.DefaultPort;
        [Tooltip("默认关闭。开启后会按 0/8/16/24s 自动换卡，会冲掉热推结果。")]
        public bool autoStartTimeline;
        [Tooltip("渲染到哪个 Display（Unity 0-based）。Display 1 → 填 0。")]
        public int targetDisplayIndex = 0;

        [SerializeField] StyleSheet craftedStyle;
        [SerializeField] StyleSheet tokensStyle;
        [SerializeField] StyleSheet motionStyle;
        // DS 设计系统（sinanata/unity-ui-toolkit-design-system，MIT）全套样式由 A2uiDsStyles 在运行时统一加载，无需逐个 SerializeField
        [SerializeField] ThemeStyleSheet runtimeTheme;
        [SerializeField] PanelSettings panelSettingsAsset;
        [Tooltip("ds | a | dark | figma-<dir>（自动发现；未知键回落 ds）。DS=sinanata/unity-ui-toolkit-design-system，MIT")]
        public string tokenVariant = "ds";
        [Tooltip("true=叠层垂直居中于屏幕；false=贴底 48px")]
        public bool mountCenter = true;

        UIDocument _doc;
        A2uiV08Processor _processor;
        A2uiActionRouter _router;
        FakeVehicleService _vehicle;
        A2uiSessionRecorder _recorder;
        A2uiSchemeACommandServer _server;
        A2uiTcpJsonlServer _tcp;
        A2uiTimelineDriver _timeline;
        VisualElement _root;
        VisualElement _mount;
        string _tokenVariant = "ds";

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            ApplyOverlayConfig(Resources.Load<A2uiOverlayConfig>("A2uiOverlayConfig"));
            LoadStylesEditorFallback();
            EnsurePanelSettings();
            if (!string.IsNullOrEmpty(tokenVariant))
                _tokenVariant = tokenVariant.ToLowerInvariant();

            if (runtimeTheme != null && _doc.panelSettings != null &&
                _doc.panelSettings.themeStyleSheet == null)
                _doc.panelSettings.themeStyleSheet = runtimeTheme;

            _vehicle = new FakeVehicleService();
            _recorder = new A2uiSessionRecorder();
            _recorder.Begin("launcher_host");
            _processor = new A2uiV08Processor();
            _processor.SurfaceReady += _ => Render();
            _processor.SurfaceDataChanged += _ => Render();
            _router = new A2uiActionRouter(_processor, _vehicle, msg => Debug.Log("[A2uiLauncherHost] " + msg));

            if (enableLiveServer)
                StartLiveServers();

            _timeline = GetComponent<A2uiTimelineDriver>();
            if (_timeline == null && autoStartTimeline)
                _timeline = gameObject.AddComponent<A2uiTimelineDriver>();

            _doc.rootVisualElement.schedule.Execute(Boot).StartingIn(0);
        }

        /// <summary>热推广播到所有叠层宿主（避免双 Host 时「端口在 A、画面在 B」）。</summary>
        public static void BroadcastLivePayload(string prompt, string jsonl)
        {
            var hosts = Object.FindObjectsOfType<A2uiLauncherSurfaceHost>();
            if (hosts == null || hosts.Length == 0) return;
            foreach (var h in hosts)
            {
                if (h != null && h.isActiveAndEnabled)
                    h.ApplyLivePayloadLocal(prompt, jsonl);
            }
        }

        public static void BroadcastTheme(string theme)
        {
            var hosts = Object.FindObjectsOfType<A2uiLauncherSurfaceHost>();
            if (hosts == null || hosts.Length == 0) return;
            foreach (var h in hosts)
            {
                if (h != null && h.isActiveAndEnabled)
                    h.ApplyTheme(theme);
            }
        }

        void StartLiveServers()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            _tcp = new A2uiTcpJsonlServer(livePort, BroadcastLivePayload, BroadcastTheme);
            _tcp.Start();
#else
            _server = new A2uiSchemeACommandServer(livePort, BroadcastLivePayload);
            _server.OnTheme = BroadcastTheme;
            _server.Start();
            if (!_server.IsRunning)
            {
                _tcp = new A2uiTcpJsonlServer(livePort, BroadcastLivePayload, BroadcastTheme);
                _tcp.Start();
            }
#endif
        }

        void OnDisable()
        {
            _server?.Stop();
            _server = null;
            _tcp?.Stop();
            _tcp = null;
        }

        void Update()
        {
            _server?.Pump();
            _tcp?.Pump();
        }

        void ApplyOverlayConfig(A2uiOverlayConfig cfg)
        {
            if (cfg == null) return;
            if (panelSettingsAsset == null) panelSettingsAsset = cfg.panelSettings;
            if (runtimeTheme == null) runtimeTheme = cfg.runtimeTheme;
            if (craftedStyle == null) craftedStyle = cfg.craftedStyle;
            if (tokensStyle == null) tokensStyle = cfg.tokensStyle;
            if (string.IsNullOrEmpty(jsonlRelativePath) &&
                !string.IsNullOrEmpty(cfg.defaultJsonlRelativePath))
                jsonlRelativePath = cfg.defaultJsonlRelativePath;
        }

        void LoadStylesEditorFallback()
        {
#if UNITY_EDITOR
            if (runtimeTheme == null)
                runtimeTheme = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                    "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
            if (craftedStyle == null)
                craftedStyle = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    "Assets/A2UISchemeA/Styles/Crafted.uss");
            if (tokensStyle == null)
                tokensStyle = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    "Assets/A2UISchemeA/Styles/Tokens.uss");
            if (motionStyle == null)
                motionStyle = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    "Assets/A2UISchemeA/Styles/Motion.uss");
            if (panelSettingsAsset == null)
                panelSettingsAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/A2UISchemeA/PanelSettings.asset");
#endif
        }

        void EnsurePanelSettings()
        {
            if (panelSettingsAsset != null)
            {
                _doc.panelSettings = panelSettingsAsset;
                panelSettingsAsset.sortingOrder = 100;
                panelSettingsAsset.clearColor = false;
                panelSettingsAsset.targetDisplay = targetDisplayIndex;
            }
            else if (_doc.panelSettings == null)
            {
                var settings = ScriptableObject.CreateInstance<PanelSettings>();
                settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                settings.referenceResolution = new Vector2Int(1920, 1080);
                settings.clearColor = false;
                settings.sortingOrder = 100;
                settings.targetDisplay = targetDisplayIndex;
                _doc.panelSettings = settings;
            }
            else
            {
                _doc.panelSettings.targetDisplay = targetDisplayIndex;
            }
        }

        TextCoreFontAsset _miSansFontAsset;

        void LoadMiSansFont()
        {
            if (_miSansFontAsset != null) return;
            // 字体随包自带（A2UISchemeA/Resources/MiSans-Regular.ttf），Editor/Player 通用
            var legacyFont = Resources.Load<Font>("MiSans-Regular");
            if (legacyFont != null)
                _miSansFontAsset = TextCoreFontAsset.CreateFontAsset(legacyFont);
        }

        void Boot()
        {
            _root = _doc.rootVisualElement;
            _root.Clear();
            LoadMiSansFont();
            if (_miSansFontAsset != null)
            {
                var fd = new FontDefinition { fontAsset = _miSansFontAsset };
                _root.style.unityFontDefinition = new StyleFontDefinition(fd);
            }
            TryAddStyle(_root, craftedStyle);
            TryAddStyle(_root, tokensStyle);
            // DS 样式在 _mount 创建后统一应用（见下方 ApplyTokenClassToMount 之后）
            TryAddStyle(_root, motionStyle);
            _root.style.flexGrow = 1;
            _root.style.justifyContent = Justify.FlexEnd;
            _root.style.alignItems = Align.Center;
            _root.style.paddingBottom = 48;
            _root.style.backgroundColor = new Color(0, 0, 0, 0);
            _root.pickingMode = PickingMode.Ignore;

            _mount = new VisualElement();
            _mount.AddToClassList("a2ui-skin--crafted");
            _mount.AddToClassList("a2ui-skin--overlay");
            ApplyTokenClassToMount();
            _mount.style.backgroundColor = new Color(0, 0, 0, 0);
            _mount.style.flexGrow = 0;
            _mount.style.width = StyleKeyword.Auto;
            _mount.style.maxWidth = 720;
            // DS 设计系统（sinanata/unity-ui-toolkit-design-system，MIT）全套样式 + ds-root 作用域，作为默认皮肤
            A2uiDsStyles.Apply(_mount);
            _mount.pickingMode = PickingMode.Position;
            _mount.AddToClassList("a2ui-draggable");
            _mount.AddManipulator(new A2uiDragManipulator());
            _root.Add(_mount);

            _mount.RegisterCallback<GeometryChangedEvent>(OnMountGeometryChanged);

            if (!string.IsNullOrEmpty(jsonlRelativePath))
                LoadJsonlFile(jsonlRelativePath, replace: true);

            if (autoStartTimeline)
            {
                if (_timeline == null)
                    _timeline = gameObject.AddComponent<A2uiTimelineDriver>();
                _timeline.Bind(ApplyJsonl);
                _timeline.StartTimeline();
            }
        }

        void ApplyTokenClassToMount()
        {
            if (_mount == null) return;
            _mount.RemoveFromClassList("a2ui-token--a");
            _mount.RemoveFromClassList("a2ui-token--b");
            _mount.RemoveFromClassList("a2ui-token--figma");
            _mount.RemoveFromClassList("a2ui-token--beach");
            _mount.RemoveFromClassList("a2ui-token--pink");
            _mount.RemoveFromClassList("a2ui-token--ice");
            _mount.RemoveFromClassList("a2ui-token--green");
            _mount.RemoveFromClassList("a2ui-token--aaos");
            _mount.RemoveFromClassList("a2ui-token--cloud");
            _mount.RemoveFromClassList("a2ui-token--dark");
            switch (_tokenVariant)
            {
                case "b": _mount.AddToClassList("a2ui-token--b"); break;
                case "dark": _mount.AddToClassList("a2ui-token--dark"); break;
                case "ds": _mount.AddToClassList("ds-root"); break;
                default: _mount.AddToClassList("a2ui-token--a"); break;
            }
        }

        public void ApplyTheme(string theme)
        {
            if (string.IsNullOrEmpty(theme)) return;
            _tokenVariant = theme.Trim().ToLowerInvariant();
            tokenVariant = _tokenVariant;
#if UNITY_EDITOR
            if (tokensStyle == null)
                tokensStyle = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    "Assets/A2UISchemeA/Styles/Tokens.uss");
            if (motionStyle == null)
                motionStyle = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                    "Assets/A2UISchemeA/Styles/Motion.uss");
#endif
            if (_root != null)
            {
                TryAddStyle(_root, tokensStyle);
                TryAddStyle(_root, motionStyle);
                // DS 设计系统（sinanata/unity-ui-toolkit-design-system，MIT）全套样式 + ds-root 作用域
                A2uiDsStyles.Apply(_root);
            }

            ApplyTokenClassToMount();
            PaintThemeInline(_mount);
            Debug.Log("[A2uiLauncherHost] theme=" + _tokenVariant + " (inline paint)");
        }

        void OnMountGeometryChanged(GeometryChangedEvent evt)
        {
            if (_mount == null) return;
            var parent = _mount.parent;
            if (parent == null) return;
            var pw = parent.contentRect.width;
            var ph = parent.contentRect.height;
            var w = _mount.layout.width;
            var h = _mount.layout.height;
            if (w < 1f || h < 1f || pw < 1f || ph < 1f) return;

            // 锚点：默认垂直居中；mountCenter=false 时贴底 48px，内容变高向上长
            _mount.style.position = UnityEngine.UIElements.Position.Absolute;
            _mount.style.left = Mathf.Max(0f, (pw - w) * 0.5f);
            _mount.style.right = StyleKeyword.Auto;
            if (mountCenter)
            {
                _mount.style.top = Mathf.Max(0f, (ph - h) * 0.5f);
                _mount.style.bottom = StyleKeyword.Auto;
            }
            else
            {
                _mount.style.top = StyleKeyword.Auto;
                _mount.style.bottom = 48f;
            }
        }

        public void ApplyJsonl(string prompt, string jsonl) => ApplyLivePayloadLocal(prompt, jsonl);

        void OnLivePayload(string prompt, string jsonl) => ApplyLivePayloadLocal(prompt, jsonl);

        void ApplyLivePayloadLocal(string prompt, string jsonl)
        {
            if (string.IsNullOrWhiteSpace(jsonl)) return;
            var v = A2uiV08Validator.ValidateJsonl(jsonl, out var msgs);
            if (!v.Ok)
            {
                Debug.LogWarning("[A2uiLauncherHost] validation fail: " + v.Error);
                return;
            }

            _processor.Clear();
            foreach (var m in msgs)
                _processor.IngestMessage(m);
            _recorder.RecordPrompt(string.IsNullOrEmpty(prompt) ? "(live)" : prompt);
            _recorder.RecordJsonl(jsonl);
            Debug.Log("[A2uiLauncherHost] live apply · prompt=" + prompt);
        }

        public void LoadJsonlFile(string relativePath, bool replace)
        {
            try
            {
                var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
                if (!File.Exists(path))
                {
                    Debug.Log("[A2uiLauncherHost] no local sample, wait live push: " + relativePath);
                    return;
                }

                var text = File.ReadAllText(path);
                var prompt = A2uiSchemeACommandServer.ExtractPrompt(text);
                var jsonl = A2uiSchemeACommandServer.StripMetaLines(text);
                if (replace)
                    _processor.Clear();
                ApplyLivePayloadLocal(prompt, jsonl);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[A2uiLauncherHost] LoadJsonlFile: " + e.Message);
            }
        }

        void Render()
        {
            if (_mount == null || _processor == null) return;
            _mount.Clear();
            foreach (var kv in _processor.Surfaces)
            {
                if (!kv.Value.ReadyToRender) continue;
                var built = new A2uiV08CatalogMapper(OnAction).Build(kv.Value);
                _mount.Add(built);
                _recorder.RecordRender(kv.Key, kv.Value.Components.Count);
                PaintThemeInline(_mount);
                // Yoga 测量固化兜底：与覆盖层宿主同病同治
                A2uiV08CatalogMapper.ScheduleTextMeasureFix(built);
                return;
            }
        }

        /// <summary>
        /// 热切肤：内联色盖过 Crafted 深色底，Play 中即时可见，不依赖 Stop。
        /// </summary>
        void PaintThemeInline(VisualElement root)
        {
            if (root == null) return;

            ThemeInk ink;
            switch (_tokenVariant)
            {
                case "pink":
                    ink = new ThemeInk
                    {
                        Card = new Color(1f, 0.72f, 0.82f, 0.62f),
                        Cabin = new Color(1f, 0.78f, 0.88f, 0.5f),
                        Text = new Color(0.35f, 0.12f, 0.22f, 1f),
                        Caption = new Color(0.55f, 0.22f, 0.35f, 0.92f),
                        Primary = new Color(1f, 0.41f, 0.63f, 0.92f),
                        PrimaryText = Color.white,
                        Secondary = new Color(1f, 0.86f, 0.92f, 0.85f),
                        SecondaryText = new Color(0.47f, 0.16f, 0.27f, 1f),
                        Border = new Color(1f, 0.47f, 0.67f, 0.65f)
                    };
                    break;
                case "beach":
                    ink = new ThemeInk
                    {
                        Card = new Color(1f, 0.97f, 0.92f, 0.92f),
                        Cabin = new Color(1f, 0.94f, 0.86f, 0.9f),
                        Text = new Color(0.11f, 0.19f, 0.25f, 1f),
                        Caption = new Color(0.35f, 0.47f, 0.55f, 1f),
                        Primary = new Color(0.13f, 0.66f, 0.77f, 1f),
                        PrimaryText = Color.white,
                        Secondary = new Color(0.94f, 0.89f, 0.8f, 0.95f),
                        SecondaryText = new Color(0.2f, 0.29f, 0.34f, 1f),
                        Border = new Color(0.13f, 0.59f, 0.71f, 0.45f)
                    };
                    break;
                case "green":
                    ink = new ThemeInk
                    {
                        Card = new Color(0.933f, 0.980f, 0.949f, 0.96f),
                        Cabin = new Color(0.863f, 0.957f, 0.894f, 0.9f),
                        Text = new Color(0.094f, 0.251f, 0.157f, 1f),
                        Caption = new Color(0.353f, 0.549f, 0.408f, 1f),
                        Primary = new Color(0.180f, 0.627f, 0.376f, 1f),
                        PrimaryText = Color.white,
                        Secondary = new Color(0.808f, 0.941f, 0.863f, 0.95f),
                        SecondaryText = new Color(0.078f, 0.376f, 0.220f, 1f),
                        Border = new Color(0.471f, 0.784f, 0.588f, 0.55f)
                    };
                    break;
                case "ice":
                    // 淡蓝：浅色底、深蓝字，车机白天可读
                    ink = new ThemeInk
                    {
                        Card = new Color(0.953f, 0.976f, 1f, 0.96f),
                        Cabin = new Color(0.871f, 0.933f, 0.992f, 0.9f),
                        Text = new Color(0.11f, 0.204f, 0.322f, 1f),
                        Caption = new Color(0.369f, 0.486f, 0.62f, 1f),
                        Primary = new Color(0.227f, 0.541f, 0.863f, 1f),
                        PrimaryText = Color.white,
                        Secondary = new Color(0.808f, 0.894f, 0.973f, 0.95f),
                        SecondaryText = new Color(0.102f, 0.29f, 0.486f, 1f),
                        Border = new Color(0.478f, 0.698f, 0.91f, 0.55f)
                    };
                    break;
                case "aaos":
                    // 安卓车载暗色：深色底 + 浅蓝强调 + 高对比
                    ink = new ThemeInk
                    {
                        Card = new Color(0.106f, 0.122f, 0.153f, 0.95f),
                        Cabin = new Color(0.133f, 0.153f, 0.192f, 0.92f),
                        Text = new Color(0.91f, 0.918f, 0.929f, 1f),
                        Caption = new Color(0.604f, 0.635f, 0.694f, 1f),
                        Primary = new Color(0.541f, 0.706f, 0.973f, 1f),
                        PrimaryText = new Color(0.051f, 0.067f, 0.09f, 1f),
                        Secondary = new Color(0.153f, 0.188f, 0.247f, 0.95f),
                        SecondaryText = new Color(0.863f, 0.902f, 0.961f, 1f),
                        Border = new Color(0.541f, 0.706f, 0.973f, 0.25f)
                    };
                    break;
                case "blood":
                    // 血红：深红底 + 高饱和血红强调，车载夜读可读
                    ink = new ThemeInk
                    {
                        Card = new Color(0.16f, 0.02f, 0.03f, 0.96f),
                        Cabin = new Color(0.13f, 0.03f, 0.05f, 0.92f),
                        Text = new Color(0.97f, 0.9f, 0.91f, 1f),
                        Caption = new Color(0.74f, 0.45f, 0.47f, 1f),
                        Primary = new Color(0.72f, 0.06f, 0.09f, 1f),
                        PrimaryText = Color.white,
                        Secondary = new Color(0.32f, 0.08f, 0.1f, 0.9f),
                        SecondaryText = new Color(0.95f, 0.8f, 0.82f, 1f),
                        Border = new Color(0.62f, 0.1f, 0.13f, 0.9f)
                    };
                    break;
                case "cloud":
                    // 参考图浅色风：浅灰背景 + 纯白卡片 + 亮蓝强调
                    ink = new ThemeInk
                    {
                        Card = new Color(1f, 1f, 1f, 0.98f),
                        Cabin = new Color(1f, 1f, 1f, 0.96f),
                        Text = new Color(0.118f, 0.133f, 0.165f, 1f),
                        Caption = new Color(0.51f, 0.557f, 0.62f, 1f),
                        Primary = new Color(0.169f, 0.541f, 1f, 1f),
                        PrimaryText = Color.white,
                        Secondary = new Color(0.941f, 0.953f, 0.973f, 0.95f),
                        SecondaryText = new Color(0.118f, 0.133f, 0.165f, 1f),
                        Border = new Color(0.784f, 0.824f, 0.863f, 0.45f)
                    };
                    break;
                default:
                    // 清掉内联，回到 USS Token A/B
                    ClearInlineTheme(root);
                    root.MarkDirtyRepaint();
                    return;
            }

            ApplyInlineWalk(root, ink);
            root.MarkDirtyRepaint();
        }

        /// <summary>
        /// 一套主题要内联刷上去的颜色。
        /// </summary>
        struct ThemeInk
        {
            public Color Card;
            public Color Cabin;
            public Color Text;
            public Color Caption;
            public Color Primary;
            public Color PrimaryText;
            public Color Secondary;
            public Color SecondaryText;
            public Color Border;
        }

        static void ClearInlineTheme(VisualElement ve)
        {
            if (ve.ClassListContains("a2ui-card") || ve.ClassListContains("a2ui-cabin") ||
                ve.ClassListContains("a2ui-text") || ve.ClassListContains("a2ui-btn") ||
                ve.ClassListContains("a2ui-btn--primary") || ve.ClassListContains("a2ui-btn--secondary"))
            {
                ve.style.backgroundColor = StyleKeyword.Null;
                ve.style.color = StyleKeyword.Null;
                ve.style.borderTopColor = StyleKeyword.Null;
                ve.style.borderBottomColor = StyleKeyword.Null;
                ve.style.borderLeftColor = StyleKeyword.Null;
                ve.style.borderRightColor = StyleKeyword.Null;
            }

            for (var i = 0; i < ve.childCount; i++)
                ClearInlineTheme(ve[i]);
        }

        static void ApplyInlineWalk(VisualElement ve, ThemeInk ink, int cardDepth = 0)
        {
            if (ve.ClassListContains("a2ui-card"))
            {
                // 嵌套卡片用更深的底色
                if (cardDepth > 0)
                {
                    var darker = new Color(
                        ink.Card.r * 0.82f, ink.Card.g * 0.82f, ink.Card.b * 0.82f, ink.Card.a);
                    ve.style.backgroundColor = darker;
                    ve.style.borderLeftColor = ink.Primary;
                    ve.style.borderLeftWidth = 3;
                    ve.style.borderTopColor = ink.Border;
                    ve.style.borderRightColor = ink.Border;
                    ve.style.borderBottomColor = ink.Border;
                    ve.style.borderTopWidth = 1;
                    ve.style.borderRightWidth = 1;
                    ve.style.borderBottomWidth = 1;
                }
                else
                {
                    ve.style.backgroundColor = ink.Card;
                    ve.style.borderTopColor = ink.Border;
                    ve.style.borderLeftColor = ink.Border;
                    ve.style.borderRightColor = ink.Border;
                    ve.style.borderBottomColor = ink.Text;
                    ve.style.borderBottomWidth = 3;
                    ve.style.borderTopWidth = 1;
                    ve.style.borderLeftWidth = 1;
                    ve.style.borderRightWidth = 1;
                }
            }

            if (ve.ClassListContains("a2ui-cabin"))
                ve.style.backgroundColor = ink.Cabin;

            if (ve.ClassListContains("a2ui-text"))
                ve.style.color = ve.ClassListContains("a2ui-text--caption") ? ink.Caption : ink.Text;

            if (ve.ClassListContains("a2ui-btn--primary"))
            {
                ve.style.backgroundColor = ink.Primary;
                ve.style.color = ink.PrimaryText;
            }
            else if (ve.ClassListContains("a2ui-btn--secondary"))
            {
                ve.style.backgroundColor = ink.Secondary;
                ve.style.color = ink.SecondaryText;
            }

            var childDepth = ve.ClassListContains("a2ui-card") ? cardDepth + 1 : cardDepth;
            for (var i = 0; i < ve.childCount; i++)
                ApplyInlineWalk(ve[i], ink, childDepth);
        }

        void OnAction(string name, Newtonsoft.Json.Linq.JObject ctx)
        {
            string sid = null;
            foreach (var kv in _processor.Surfaces)
            {
                sid = kv.Key;
                break;
            }

            _recorder.RecordAction(name, ctx, "launcher");
            _router.Handle(name, ctx, sid);
        }

        static void TryAddStyle(VisualElement root, StyleSheet sheet)
        {
            if (sheet == null || root == null) return;
            for (var i = 0; i < root.styleSheets.count; i++)
            {
                if (root.styleSheets[i] == sheet) return;
            }

            root.styleSheets.Add(sheet);
        }

        [ContextMenu("Export Session")]
        public void Export() => Debug.Log(_recorder.ExportPath());

        [ContextMenu("Start Timeline")]
        public void StartTimelineMenu()
        {
            if (_timeline == null)
                _timeline = gameObject.AddComponent<A2uiTimelineDriver>();
            _timeline.Bind(ApplyJsonl);
            _timeline.StartTimeline();
        }
    }
}
