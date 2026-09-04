using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using TextCoreFontAsset = UnityEngine.TextCore.Text.FontAsset;

namespace A2UISchemeA
{
    /// <summary>
    /// Scheme A 验证床：四目标 + G0–G5（ClosedLoop / Gate / Degrade / Replay）。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class A2uiSchemeAHost : MonoBehaviour
    {
        public enum Act
        {
            Coverage,
            Live,
            Scenario,
            Craft,
            ClosedLoop,
            Gate,
            Degrade
        }

        public const string SampleMediaCard = "Assets/A2UISchemeA/Samples/media_card.v0.8.jsonl";
        public const string SampleCatalogAll = "Assets/A2UISchemeA/Samples/catalog_all.v0.8.jsonl";
        public const string SampleCoverageTour = "Assets/A2UISchemeA/Samples/coverage_tour.v0.8.jsonl";
        public const string SamplePromptMedia = "Assets/A2UISchemeA/Samples/prompt_media.v0.8.jsonl";
        public const string SamplePromptClimate = "Assets/A2UISchemeA/Samples/prompt_climate.v0.8.jsonl";
        public const string SamplePromptRest = "Assets/A2UISchemeA/Samples/prompt_rest.v0.8.jsonl";
        public const string SampleInvalid = "Assets/A2UISchemeA/Samples/invalid_bad_packet.v0.8.jsonl";
        public const string SampleUnknown = "Assets/A2UISchemeA/Samples/degrade_unknown.v0.8.jsonl";
        public const string SamplePoiComplex = "Assets/A2UISchemeA/Samples/poi_complex.v0.8.jsonl";
        public const string SampleCabinMedia = "Assets/A2UISchemeA/Samples/cabin_media.v0.8.jsonl";
        public const string SampleListTemplate = "Assets/A2UISchemeA/Samples/list_template.v0.8.jsonl";

        static readonly Dictionary<string, string> NarrationBySurface = new Dictionary<string, string>
        {
            ["catalog"] = "需求：验证 Standard Catalog 全类型映射 → 界面策略：覆盖页 checklist",
            ["tour"] = "需求：验证关键协议属性被解析 → 界面策略：coverage tour 点亮属性",
            ["media"] = "需求：行车中换歌/调音量 → 窄条媒体；点按钮走 Action 闭环回写",
            ["climate"] = "需求：有点热 → 温控面；点降温走闭环改 dataModel",
            ["rest"] = "需求：想休息 → 大字提示+勿扰",
            ["poi"] = "需求：推荐餐厅 → P 挡可复杂卡；行驶中 Gate 强制简化",
            ["degrade"] = "需求：未知类型不可摧毁体验 → Basic 降级卡片",
            ["cabin"] = "需求：量产 Catalog 子集 → MediaMiniBar 等座舱类型",
            ["u-list-tpl"] = "看点：template List · 相对 path · 数据变行数变"
        };

        static readonly Dictionary<Act, string> LookoutByAct = new Dictionary<Act, string>
        {
            [Act.Coverage] = "本场看点：右栏点 Type=单元测；已映射 21 types",
            [Act.Live] = "本场看点：HTTP/inbox 推送 JSONL；坏包拒绝留上一帧",
            [Act.Scenario] = "本场看点：意图样本 → 生成界面（非读心，样本即显式意图）",
            [Act.Craft] = "本场看点：同一 JSONL · Raw vs Crafted · Token A/B 换肤（Figma 第一公里）",
            [Act.ClosedLoop] = "本场看点：点按钮 → action → dataModelUpdate 回刷",
            [Act.Gate] = "本场看点：切 D 挡，复杂 UI 被门禁改写",
            [Act.Degrade] = "本场看点：未知类型/超时骨架，不崩"
        };

        [SerializeField] int port = A2uiSchemeACommandServer.DefaultPort;
        [SerializeField] ThemeStyleSheet runtimeTheme;
        [SerializeField] StyleSheet hostStyle;
        [SerializeField] StyleSheet craftedStyle;
        [SerializeField] StyleSheet tokensStyle;
        [SerializeField] StyleSheet motionStyle;
        [SerializeField] bool replaceOnSampleLoad = true;

        // DS 设计系统（sinanata/unity-ui-toolkit-design-system，MIT）全套样式表，EnsureStyles 时按清单加载
        StyleSheet[] _dsStyles;
        StyleSheet[] _figmaStyles;
        static readonly string[] DsStylePaths =
        {
            "Assets/A2UISchemeA/Styles/DS/DesignTokens.uss",
            "Assets/A2UISchemeA/Styles/DS/Typography.uss",
            "Assets/A2UISchemeA/Styles/DS/Icons.uss",
            "Assets/A2UISchemeA/Styles/DS/Buttons.uss",
            "Assets/A2UISchemeA/Styles/DS/Inputs.uss",
            "Assets/A2UISchemeA/Styles/DS/TabsAndFilters.uss",
            "Assets/A2UISchemeA/Styles/DS/Cards.uss",
            "Assets/A2UISchemeA/Styles/DS/Navigation.uss",
            "Assets/A2UISchemeA/Styles/DS/Badges.uss",
            "Assets/A2UISchemeA/Styles/DS/Controls.uss",
            "Assets/A2UISchemeA/Styles/DS/Overlays.uss",
            "Assets/A2UISchemeA/Styles/DS/Feedback.uss",
            "Assets/A2UISchemeA/Styles/DS/Mobile.uss",
            "Assets/A2UISchemeA/Styles/DS/DropdownPopup.uss",
            "Assets/A2UISchemeA/Styles/DS/A2uiAlias.uss",
        };

        UIDocument _doc;
        A2uiV08Processor _processor;
        A2uiSchemeACommandServer _server;
        A2uiCoverageHud _coverage;
        A2uiActionRouter _router;
        FakeVehicleService _vehicle;
        A2uiPolicyGate _gate;
        A2uiSessionRecorder _recorder;
        Act _act = Act.Coverage;
        string _tokenVariant = "ds";
        string _lastPrompt = "";
        string _lastJsonlPreview = "";
        string _narration = "";
        string _lookout = "";
        string _actionLog = "";
        string _gateStatus = "";
        string _activeSample = SampleCoverageTour;
        bool _showSkeleton;
        bool _gateRewriteApplied;
        bool _suppressRender;

        VisualElement _mainMount;
        VisualElement _sideMount;
        VisualElement _rawMount;
        VisualElement _craftedMount;
        VisualElement _gateBar;
        VisualElement _cardOverlay;
        VisualElement _chrome;
        string _overlayPrompt = "";
        Label _lookoutLabel;
        Label _narrationLabel;
        Label _promptLabel;
        Label _jsonlLabel;
        ScrollView _jsonlScroll;
        Label _actionLabel;
        Label _gateLabel;
        string _lastJsonlFull = "";
        readonly List<Button> _tabButtons = new List<Button>();

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            _doc.sortingOrder = 1000;   // 确保 A2UI 覆盖层始终盖在场景其他 UI 文档之上
            EnsurePanelSettings(_doc);
            EnsureStyles();
            _vehicle = new FakeVehicleService();
            _gate = new A2uiPolicyGate();
            _recorder = new A2uiSessionRecorder();
            _recorder.Begin();
            EnsureProcessor(reset: true);
            _doc.rootVisualElement.schedule.Execute(BuildChrome).StartingIn(0);
        }

        void Start()
        {
            _server = new A2uiSchemeACommandServer(port, OnLivePayload);
            _server.OnTheme += ApplyThemeFromServer;
            _server.Start();
        }

        void OnDestroy()
        {
            if (_server != null) _server.OnTheme -= ApplyThemeFromServer;
            _server?.Stop();
        }

        void Update() => _server?.Pump();

        [ContextMenu("Reload Active Sample")]
        public void Reload() => LoadSample(_activeSample, replace: true);

        [ContextMenu("Export Session Replay")]
        public void ExportSession()
        {
            var path = _recorder.ExportPath();
            _actionLog = "session exported: " + path;
            UpdateMetaLabels();
        }

        void EnsureProcessor(bool reset)
        {
            if (_processor != null && !reset) return;
            _processor = new A2uiV08Processor();
            _processor.SurfaceReady += OnSurfaceEvent;
            _processor.SurfaceDataChanged += OnSurfaceEvent;
            _processor.SurfaceDeleted += id =>
            {
                _mainMount?.Clear();
                _coverage?.Refresh(null);
                _recorder.RecordDegrade("surface_deleted:" + id);
            };
            _router = new A2uiActionRouter(_processor, _vehicle, msg =>
            {
                _actionLog = msg;
                UpdateMetaLabels();
            });
        }

        void OnSurfaceEvent(string surfaceId)
        {
            var state = FirstReadyState();
            if (state != null)
                _recorder.RecordRender(surfaceId, state.Components.Count);
            if (_suppressRender) return;
            Rerender();
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

        void ApplyFont(VisualElement root)
        {
            LoadMiSansFont();
            if (_miSansFontAsset != null)
            {
                var fd = new FontDefinition { fontAsset = _miSansFontAsset };
                root.style.unityFontDefinition = new StyleFontDefinition(fd);
            }
        }

        void BuildChrome()
        {
            var root = _doc.rootVisualElement;
            if (root == null) return;
            root.Clear();
            ApplyStyleSheets(root);
            ApplyFont(root);

            var chrome = new VisualElement();
            chrome.AddToClassList("a2ui-host");
            _chrome = chrome;
            root.Add(chrome);

            // 整屏左右对半分：左半操作+生成物，右半 Coverage
            var leftCol = new VisualElement();
            leftCol.AddToClassList("a2ui-host__left");
            chrome.Add(leftCol);

            var rightCol = new VisualElement();
            rightCol.AddToClassList("a2ui-host__right");
            chrome.Add(rightCol);

            leftCol.Add(new Label("A2UI Scheme A 验证 Demo").WithClass("a2ui-host__title"));

            leftCol.Add(new Label("演示切换（点一个看一类能力）").WithClass("a2ui-host__section-label"));
            var tabs = new VisualElement();
            tabs.AddToClassList("a2ui-host__tabs");
            _tabButtons.Clear();
            tabs.Add(MakeTab("1 协议覆盖", Act.Coverage));
            tabs.Add(MakeTab("2 实时推送", Act.Live));
            tabs.Add(MakeTab("3 意图场景", Act.Scenario));
            tabs.Add(MakeTab("4 样式对比", Act.Craft));
            tabs.Add(MakeTab("5 点击闭环", Act.ClosedLoop));
            tabs.Add(MakeTab("6 行驶限制", Act.Gate));
            tabs.Add(MakeTab("7 异常降级", Act.Degrade));
            leftCol.Add(tabs);

            _lookoutLabel = new Label("").WithClass("a2ui-host__lookout");
            _narrationLabel = new Label("").WithClass("a2ui-host__narration");
            _promptLabel = new Label("").WithClass("a2ui-host__prompt");
            leftCol.Add(_lookoutLabel);
            leftCol.Add(_narrationLabel);
            leftCol.Add(_promptLabel);

            leftCol.Add(new Label("单元测 / 场景 / 档位 / 换肤").WithClass("a2ui-host__section-label"));
            var compactBar = new VisualElement();
            compactBar.AddToClassList("a2ui-host__compact-bar");

            var unitDrop = new DropdownField(A2uiCoverageHud.CatalogTypeChoices(), 0);
            unitDrop.AddToClassList("a2ui-host__compact-drop");
            unitDrop.tooltip = "单元测 Type";
            unitDrop.RegisterValueChangedCallback(evt =>
                LoadUnitType(A2uiCoverageHud.ParseTypeFromChoice(evt.newValue)));
            var unitLoad = new Button(() =>
                LoadUnitType(A2uiCoverageHud.ParseTypeFromChoice(unitDrop.value))) { text = "加载单元" };
            unitLoad.AddToClassList("a2ui-host__compact-btn");
            compactBar.Add(unitDrop);
            compactBar.Add(unitLoad);

            var scenarioChoices = new List<string>
            {
                "媒体闭环", "空调闭环", "休息", "餐厅/门禁", "打磨对比",
                "动态列表", "未知组件", "错误数据", "座舱条", "全字段巡检",
                "导出会话", "模拟超时"
            };
            var scenarioDrop = new DropdownField(scenarioChoices, 0);
            scenarioDrop.AddToClassList("a2ui-host__compact-drop");
            scenarioDrop.tooltip = "场景样本 / 工具";
            var scenarioGo = new Button(() => RunScenarioChoice(scenarioDrop.value)) { text = "加载场景" };
            scenarioGo.AddToClassList("a2ui-host__compact-btn");
            compactBar.Add(scenarioDrop);
            compactBar.Add(scenarioGo);

            var gateChoices = new List<string> { "停车 P · 0km/h", "前进 D · 40km/h", "倒车 R · 5km/h" };
            var gateDrop = new DropdownField(gateChoices, 0);
            gateDrop.AddToClassList("a2ui-host__compact-drop");
            gateDrop.tooltip = "假装车速档位";
            gateDrop.RegisterValueChangedCallback(evt => ApplyGateChoice(evt.newValue));
            _gateLabel = new Label("").WithClass("a2ui-host__gate-status");
            compactBar.Add(gateDrop);
            compactBar.Add(_gateLabel);

            var tokenChoices = new List<string>();
            foreach (var e in A2uiThemeRegistry.All()) tokenChoices.Add(e.Label);
            var tokenDrop = new DropdownField(tokenChoices, 0);
            tokenDrop.AddToClassList("a2ui-host__compact-drop");
            tokenDrop.tooltip = "同一 JSONL → 皮肤热切（列表随 Styles/ 下新导入的 USS 自动增长）";
            tokenDrop.RegisterValueChangedCallback(evt =>
            {
                var entry = A2uiThemeRegistry.FindByKey(ThemeKeyFromLabel(evt.newValue));
                _tokenVariant = entry.Key;
                _lookout = "换肤 " + _tokenVariant.ToUpperInvariant() + " · 结构不变只换皮";
                UpdateMetaLabels();
                Rerender();
            });
            compactBar.Add(tokenDrop);

            _gateBar = compactBar;
            leftCol.Add(compactBar);

            leftCol.Add(new Label("单元 / 场景 JSONL").WithClass("a2ui-host__section-label"));
            _jsonlScroll = new ScrollView();
            _jsonlScroll.AddToClassList("a2ui-host__jsonl-scroll");
            _jsonlLabel = new Label("").WithClass("a2ui-host__jsonl");
            _jsonlScroll.Add(_jsonlLabel);
            leftCol.Add(_jsonlScroll);

            leftCol.Add(new Label("生成的界面").WithClass("a2ui-host__section-label"));
            _mainMount = new VisualElement();
            _mainMount.AddToClassList("a2ui-host__main");
            var mainScroll = new ScrollView();
            mainScroll.AddToClassList("a2ui-host__main-scroll");
            mainScroll.Add(_mainMount);
            leftCol.Add(mainScroll);

            _actionLabel = new Label("").WithClass("a2ui-host__action");
            leftCol.Add(_actionLabel);

            rightCol.Add(new Label("Types / Props 覆盖").WithClass("a2ui-host__section-label"));
            _sideMount = new VisualElement();
            _sideMount.AddToClassList("a2ui-host__side");
            var sideScroll = new ScrollView();
            sideScroll.AddToClassList("a2ui-host__side-scroll");
            sideScroll.Add(_sideMount);
            rightCol.Add(sideScroll);

            _coverage = new A2uiCoverageHud(LoadUnitType);
            _sideMount.Add(_coverage.Root);

            // ---- 实时提示词覆盖层：半透卡浮在整个场景上方 ----
            // 条带全宽负责居中；拖拽 manipulator 挂在内层卡上（RenderOverlay），
            // 否则全宽条带的水平拖拽 clamp 区间为 0，无法左右拖。
            _cardOverlay = new VisualElement();
            _cardOverlay.AddToClassList("a2ui-overlay-card");
            _cardOverlay.style.display = DisplayStyle.None;
            chrome.Add(_cardOverlay);

            SwitchAct(Act.Live, reload: false);
        }

        void LoadUnitType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return;
            typeName = A2uiCoverageHud.ParseTypeFromChoice(typeName);
            _act = Act.Coverage;
            RefreshTabStyles();
            _coverage?.SetFocusType(typeName);
            LoadSample(A2uiCoverageHud.UnitSamplePath(typeName), replace: true);
            _lookout = "单元测 " + A2uiCoverageHud.FormatTypeLabel(typeName) + " · 右栏仅该 Type 的 props";
            _narration = "已加载单元 JSONL：" + A2uiCoverageHud.UnitSamplePath(typeName);
            UpdateMetaLabels();
        }

        void RunScenarioChoice(string choice)
        {
            _coverage?.ClearFocus();
            switch (choice)
            {
                case "媒体闭环":
                    _act = Act.ClosedLoop;
                    RefreshTabStyles();
                    LoadSample(SamplePromptMedia, replace: true);
                    break;
                case "空调闭环":
                    _act = Act.ClosedLoop;
                    RefreshTabStyles();
                    LoadSample(SamplePromptClimate, replace: true);
                    break;
                case "休息":
                    _act = Act.Scenario;
                    RefreshTabStyles();
                    LoadSample(SamplePromptRest, replace: true);
                    break;
                case "餐厅/门禁":
                    _act = Act.Gate;
                    RefreshTabStyles();
                    LoadSample(SamplePoiComplex, replace: true);
                    break;
                case "打磨对比":
                    _act = Act.Craft;
                    RefreshTabStyles();
                    LoadSample(SampleMediaCard, replace: true);
                    break;
                case "动态列表":
                    _act = Act.Coverage;
                    RefreshTabStyles();
                    LoadSample(SampleListTemplate, replace: true);
                    break;
                case "未知组件":
                    _act = Act.Degrade;
                    RefreshTabStyles();
                    LoadSample(SampleUnknown, replace: true);
                    break;
                case "错误数据":
                    _act = Act.Live;
                    RefreshTabStyles();
                    LoadSample(SampleInvalid, replace: true);
                    break;
                case "座舱条":
                    _act = Act.ClosedLoop;
                    RefreshTabStyles();
                    LoadSample(SampleCabinMedia, replace: true);
                    break;
                case "全字段巡检":
                    _act = Act.Coverage;
                    RefreshTabStyles();
                    LoadSample(SampleCoverageTour, replace: true);
                    break;
                case "导出会话":
                    ExportSession();
                    break;
                case "模拟超时":
                    SimulateTimeout();
                    break;
            }
        }

        void ApplyGateChoice(string choice)
        {
            if (choice.StartsWith("停车", StringComparison.Ordinal))
                SetGate(VehicleGear.P, 0);
            else if (choice.StartsWith("前进", StringComparison.Ordinal))
                SetGate(VehicleGear.D, 40);
            else if (choice.StartsWith("倒车", StringComparison.Ordinal))
                SetGate(VehicleGear.R, 5);
        }

        Button MakeTab(string label, Act act)
        {
            var btn = new Button(() => SwitchAct(act, reload: true)) { text = label };
            btn.AddToClassList("a2ui-host__tab");
            _tabButtons.Add(btn);
            btn.userData = act;
            return btn;
        }

        Button MakeToolButton(string label, string path, Act act)
        {
            var btn = new Button(() =>
            {
                _act = act;
                RefreshTabStyles();
                LoadSample(path, replace: true);
            })
            {
                text = label
            };
            btn.AddToClassList("a2ui-host__tool-btn");
            return btn;
        }

        Button MakeGateButton(string label, VehicleGear gear, float speed)
        {
            var btn = new Button(() => SetGate(gear, speed)) { text = label };
            btn.AddToClassList("a2ui-host__tool-btn");
            return btn;
        }

        Button MakeSampleButton(string label, string path, Act act)
        {
            return MakeToolButton(label, path, act);
        }

        void SwitchAct(Act act, bool reload)
        {
            _act = act;
            _showSkeleton = false;
            _gateRewriteApplied = false;
            if (act != Act.Coverage || reload)
                _coverage?.ClearFocus();
            RefreshTabStyles();
            _lookout = LookoutByAct.TryGetValue(act, out var lo) ? lo : "";
            if (!reload)
            {
                UpdateMetaLabels();
                Rerender();
                return;
            }

            var sample = act switch
            {
                Act.Coverage => SampleCoverageTour,
                Act.Live => SamplePromptMedia,
                Act.Scenario => SamplePromptMedia,
                Act.Craft => SampleMediaCard,
                Act.ClosedLoop => SamplePromptClimate,
                Act.Gate => SamplePoiComplex,
                Act.Degrade => SampleUnknown,
                _ => SampleMediaCard
            };
            LoadSample(sample, replace: true);
            _lookout = LookoutByAct.TryGetValue(act, out var lo2) ? lo2 : _lookout;
            UpdateMetaLabels();
        }

        void RefreshTabStyles()
        {
            foreach (var btn in _tabButtons)
            {
                var act = (Act)btn.userData;
                if (act == _act) btn.AddToClassList("a2ui-host__tab--active");
                else btn.RemoveFromClassList("a2ui-host__tab--active");
            }
        }

        void SetGate(VehicleGear gear, float speed)
        {
            _gate.Gear = gear;
            _gate.SpeedKph = speed;
            _gateStatus = _gate.StatusText;
            _recorder.RecordGate(_gateStatus);
            if (_gateLabel != null) _gateLabel.text = _gateStatus;

            if (!_gate.IsDriving && _gateRewriteApplied)
            {
                _gateRewriteApplied = false;
                LoadSample(_activeSample, replace: true);
                return;
            }

            _gateRewriteApplied = false;
            Rerender();
        }

        void SimulateTimeout()
        {
            _showSkeleton = true;
            _act = Act.Degrade;
            RefreshTabStyles();
            _recorder.RecordDegrade("agent_timeout");
            _narration = "G3：Agent 超时 → 骨架屏，保留上一会话可恢复";
            UpdateMetaLabels();
            Rerender();
        }

        void LoadSample(string relativePath, bool replace)
        {
            _activeSample = relativePath;
            _showSkeleton = false;
            _gateRewriteApplied = false;
            var path = ResolvePath(relativePath);
            try
            {
                var text = File.ReadAllText(path);
                ApplyJsonl(
                    A2uiSchemeACommandServer.ExtractPrompt(text),
                    A2uiSchemeACommandServer.StripMetaLines(text),
                    Path.GetFileName(path),
                    replace: replace || replaceOnSampleLoad);
            }
            catch (Exception e)
            {
                _narration = "加载失败: " + e.Message;
                UpdateMetaLabels();
                Debug.LogException(e);
            }
        }

        /// <summary>回归测试入口（A2uiTestApi 调用）：显式设主题后走 OnLivePayload 完整链路。</summary>
        internal void ApplyForTest(string themeKey, string prompt, string jsonl)
        {
            if (!string.IsNullOrEmpty(themeKey))
                _tokenVariant = NormalizeTheme(themeKey);
            OnLivePayload(prompt, jsonl);
        }

        void OnLivePayload(string prompt, string jsonl)
        {
            Debug.Log($"[A2UISchemeA] OnLivePayload received · prompt={prompt ?? "(null)"} · jsonlLen={(jsonl?.Length ?? 0)}");

            // 还原 percent-encode 的中文 prompt
            if (!string.IsNullOrEmpty(prompt) && prompt.Contains("%"))
            {
                try { prompt = Uri.UnescapeDataString(prompt); }
                catch { /* ignore */ }
            }

            if (A2uiAgUiAdapter.TryUnwrap(jsonl, out var unwrapped, out var p2))
            {
                jsonl = unwrapped;
                if (!string.IsNullOrEmpty(p2)) prompt = p2;
            }

            _act = Act.Live;
            _overlayPrompt = prompt;
            // 同 surface 的增量推送保留当前主题与结构；全新 surface 也保留当前主题
            // （用户可能已通过 /theme 或下拉手动切换，不应被自动检测覆盖）。
            var incomingSurface = ExtractFirstSurfaceId(jsonl);
            var isNewSurface = string.IsNullOrEmpty(incomingSurface) || !_processor.Surfaces.ContainsKey(incomingSurface);
            var detected = _tokenVariant;
            _lookout = (isNewSurface ? "实时推送 · 保留主题 " : "实时增量 · 保留主题 ")
                + detected.ToUpperInvariant() + " · 结构不变只换皮";
            Debug.Log($"[A2UISchemeA] OnLivePayload theme={detected} · first100chars={(jsonl?.Length > 100 ? jsonl.Substring(0, 100) : jsonl ?? "")}");
            RefreshTabStyles();

            try
            {
                // 全新 surface 用 replace=true 先清掉之前并存的旧面板：
                // 否则 FirstReadyState 永远取第一个就绪 surface，新面板只换主题、结构不显示。
                // 同 surface 的增量修改保持 replace=false，不清除、保留主题。
                ApplyJsonl(prompt, jsonl, "live", replace: isNewSurface);
                Debug.Log($"[A2UISchemeA] OnLivePayload ApplyJsonl returned OK. _act={_act}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[A2UISchemeA] OnLivePayload ApplyJsonl THREW: {e.GetType().Name} — {e.Message}\n{e.StackTrace}");
            }
        }

        void ApplyJsonl(string prompt, string jsonl, string sourceTag, bool replace)
        {
            _lastPrompt = string.IsNullOrEmpty(prompt) ? $"(sample: {sourceTag})" : prompt;
            _lastJsonlFull = jsonl ?? "";
            _lastJsonlPreview = PreviewJsonl(jsonl);
            _recorder.RecordPrompt(_lastPrompt);
            _recorder.RecordJsonl(jsonl);

            var validation = A2uiV08Validator.ValidateJsonl(jsonl, out var messages);
            _recorder.RecordValidation(validation.Ok, validation.Error);
            if (!validation.Ok)
            {
                _narration = "G0 校验拒绝（保留上一帧）: " + validation.Error;
                _actionLog = "validation FAIL";
                Debug.LogError($"[A2UISchemeA] ApplyJsonl validation FAIL: {validation.Error}");
                UpdateMetaLabels();
                Rerender();
                return;
            }
            Debug.Log($"[A2UISchemeA] ApplyJsonl validation OK: {messages.Count} messages, replace={replace}");

            EnsureProcessor(reset: false);
            if (replace)
            {
                _processor.Clear();
                _gateRewriteApplied = false;
            }

            try
            {
                // Suppress event-driven renders during batch ingestion;
                // a single Rerender() at the end handles the full state.
                // Without this, SurfaceReady fires mid-batch → RenderOverlay
                // builds a tree → then Rerender() builds ANOTHER tree,
                // leaving a stale ApplyEntranceAnimation scheduled callback
                // referencing detached elements → ArgumentOutOfRangeException.
                _suppressRender = true;
                foreach (var msg in messages)
                    _processor.IngestMessage(msg);
                _suppressRender = false;

                var surfaceId = FirstSurfaceId();
                if (!string.IsNullOrEmpty(surfaceId) && NarrationBySurface.TryGetValue(surfaceId, out var n))
                    _narration = n;
                else
                    _narration = $"surface={surfaceId} · source={sourceTag} · replace={replace}";
            }
            catch (Exception e)
            {
                _suppressRender = false;
                _narration = "解析失败（保留上一帧）: " + e.Message;
                Debug.LogException(e);
            }

            UpdateMetaLabels();
            Rerender();
        }

        void Rerender()
        {
            // Live 模式：隐藏 chrome，只在透明底上显示卡片覆盖层
            if (_act == Act.Live)
            {
                if (_chrome != null)
                {
                    _chrome.style.backgroundColor = new Color(0, 0, 0, 0);
                    for (int i = 0; i < _chrome.childCount; i++)
                    {
                        if (_chrome[i] != _cardOverlay)
                            _chrome[i].style.display = DisplayStyle.None;
                    }
                }
                RenderOverlay();
                return;
            }

            // 非 Live 模式恢复 chrome（曾被隐藏过的话）
            if (_chrome != null)
            {
                _chrome.style.backgroundColor = StyleKeyword.Null;
                for (int i = 0; i < _chrome.childCount; i++)
                {
                    if (_chrome[i] != _cardOverlay)
                        _chrome[i].style.display = StyleKeyword.Null;
                }
            }

            if (_mainMount == null)
            {
                Debug.LogWarning("[A2UISchemeA] Rerender SKIP: _mainMount is null");
                return;
            }
            _mainMount.Clear();
            if (_gateLabel != null) _gateLabel.text = _gate.StatusText;

            if (_showSkeleton)
            {
                _mainMount.Add(A2uiDegrade.Skeleton("agent timeout — G3"));
                _coverage?.Refresh(null);
                UpdateMetaLabels();
                return;
            }

            if (_processor == null) return;
            var state = FirstReadyState();
            _coverage?.Refresh(state);
            UpdateMetaLabels();
            if (state == null) return;

            if (_act == Act.Gate || _gate.IsDriving)
            {
                if (!_gateRewriteApplied &&
                    _gate.TryRewriteToDrivingTemplate(state, out var rewrite, out var reason))
                {
                    _gateRewriteApplied = true;
                    _recorder.RecordGate("rewrite:" + reason);
                    _narration = "G4 Policy Gate 触发 → " + reason;
                    EnsureProcessor(reset: false);
                    _suppressRender = true;
                    _processor.Clear();
                    _processor.IngestJsonlText(rewrite);
                    _suppressRender = false;
                    state = FirstReadyState();
                    _coverage?.Refresh(state);
                    if (state == null) return;
                }
            }

            if (_act == Act.Craft)
            {
                var split = new VisualElement();
                split.AddToClassList("a2ui-host__split");

                var rawPane = new VisualElement();
                rawPane.AddToClassList("a2ui-host__pane");
                rawPane.Add(new Label("左=协议直出 Raw").WithClass("a2ui-host__pane-label"));
                _rawMount = new VisualElement();
                _rawMount.AddToClassList("a2ui-skin--raw");
                _rawMount.Add(new A2uiV08CatalogMapper(OnUserAction).Build(state));
                rawPane.Add(_rawMount);

                var craftedPane = new VisualElement();
                craftedPane.AddToClassList("a2ui-host__pane");
                craftedPane.Add(new Label("右=HMI 打磨 Crafted + Token " + _tokenVariant.ToUpperInvariant())
                    .WithClass("a2ui-host__pane-label"));
                _craftedMount = MakeCraftedSkin();
                _craftedMount.Add(new Label(ThemeBadge(_tokenVariant))
                    .WithClass("a2ui-token-badge"));
                _craftedMount.Add(new A2uiV08CatalogMapper(OnUserAction).Build(state));
                craftedPane.Add(_craftedMount);

                split.Add(rawPane);
                split.Add(craftedPane);
                _mainMount.Add(split);
                // Yoga 测量固化兜底：主挂载路径与覆盖层同病同治
                A2uiV08CatalogMapper.ScheduleTextMeasureFix(split);
            }
            else
            {
                var skin = MakeCraftedSkin();
                skin.Add(new Label(ThemeBadge(_tokenVariant))
                    .WithClass("a2ui-token-badge"));
                skin.Add(new A2uiV08CatalogMapper(OnUserAction).Build(state));
                _mainMount.Add(skin);
                A2uiV08CatalogMapper.ScheduleTextMeasureFix(skin);
            }

            // 实时推送时同步更新半透覆盖层
            RenderOverlay();
        }

        void RenderOverlay()
        {
            if (_cardOverlay == null)
            {
                Debug.LogWarning("[A2UISchemeA] RenderOverlay: _cardOverlay is null");
                return;
            }

            // 只在实时推送模式下显示覆盖层
            if (_act != Act.Live || string.IsNullOrEmpty(_overlayPrompt))
            {
                Debug.Log($"[A2UISchemeA] RenderOverlay: hiding · _act={_act} · _overlayPrompt='{_overlayPrompt ?? "(null)"}'");
                _cardOverlay.style.display = DisplayStyle.None;
                return;
            }

            var state = FirstReadyState();
            if (state == null)
            {
                Debug.LogWarning("[A2UISchemeA] RenderOverlay: no ready surface · processor has " + (_processor?.Surfaces?.Count ?? 0) + " surfaces");
                return;
            }

            _cardOverlay.Clear();
            _cardOverlay.style.display = DisplayStyle.Flex;

            var scopeClass = _tokenVariant == "ds" ? "ds-root" : A2uiThemeRegistry.ScopeFor(_tokenVariant);
            // figma-* 的作用域类挂在常驻的 _cardOverlay 上，只加不删会让它的后代规则
            // （76px 圆形卡片、橙色、padding:0 等）永久污染之后切换的所有主题。
            // 每次渲染前先清掉上一次残留的 figma 作用域类。
            var staleScopeClasses = new List<string>();
            foreach (var c in _cardOverlay.GetClasses())
                if (c.StartsWith("a2ui-skin--figma-"))
                    staleScopeClasses.Add(c);
            foreach (var c in staleScopeClasses)
                _cardOverlay.RemoveFromClassList(c);
            // 给覆盖层外层也挂上主题作用域类，皮肤可控制容器尺寸/对齐
            if (_tokenVariant.StartsWith("figma-"))
                _cardOverlay.AddToClassList(scopeClass);
            Debug.Log($"[A2UISchemeA] RenderOverlay building card · theme={_tokenVariant} · scopeClass={scopeClass}");

            var card = new VisualElement();
            card.AddToClassList("a2ui-overlay-card__inner");
            // 非 figma 主题保留 crafted 基底；figma 主题只用它自己的作用域类，避免变量/规则打架
            if (!_tokenVariant.StartsWith("figma-"))
                card.AddToClassList("a2ui-skin--crafted");
            card.AddToClassList(scopeClass);

            // 内联设置卡片背景色，确保与主题配色匹配（USS 在本引擎有时不生效）
            ApplyOverlayCardBackground(card, _tokenVariant);

            try
            {
                var built = new A2uiV08CatalogMapper(OnUserAction).Build(state);
                // full_control_center 等高样例：内容包进 ScrollView。
                // 外层条带 overflow:auto 在 UITK 不产生滚动条（只有 ScrollView 自带 Scroller），
                // 没有这层时超高内容被内卡 overflow:hidden 直接裁掉，底部不可见。
                var scroll = new ScrollView();
                scroll.AddToClassList("a2ui-overlay-card__scroll");
                // 内容合同是 fillMaxWidth，永不横向溢出：API 级隐藏横向 Scroller
                // （Tuanjie 下 mode=Vertical 的横向 Scroller 仍会实体化占 24px 高）
                scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                scroll.Add(built);
                card.Add(scroll);
                _cardOverlay.Add(card);
                // Apply entrance animation AFTER panel attach (Tuanjie bug workaround)
                A2uiV08CatalogMapper.ApplyEntranceAnimation(built);
                // 拖拽挂在内层卡上：条带全宽负责居中，内层卡有水平拖拽余量
                card.AddManipulator(new A2uiDragManipulator());
                card.AddToClassList("a2ui-draggable");
                // 修复 Yoga 测量缓存缺陷：文本高度被测成 0~8px 导致行间距拥挤
                A2uiV08CatalogMapper.ScheduleTextMeasureFix(built);

                // 触发 Yoga 重测量：等 2 帧布局稳定后，将卡片切为 absolute 定位。
                // 这与拖拽按下时 EnsureAbsoluteLayout() 做的事完全相同——
                // 卡片脱离文档流 → Yoga 用新约束重测全部子元素 → 文本高度被修正。
                // 没有这一步，首次渲染的文本高度是错误的（Yoga 测量缓存一次性固化）。
                built.schedule.Execute(() =>
                {
                    if (card.panel == null) return;
                    var world = card.worldBound;
                    var parentWorld = card.parent.worldBound;
                    card.style.position = Position.Absolute;
                    card.style.left = world.x - parentWorld.x;
                    card.style.top = world.y - parentWorld.y;
                    card.style.right = StyleKeyword.Auto;
                    card.style.bottom = StyleKeyword.Auto;
                    card.style.marginLeft = 0;
                    card.style.marginTop = 0;
                    card.style.marginRight = 0;
                    card.style.marginBottom = 0;
                }).StartingIn(50); // 2 帧：首帧布局 + 一帧缓冲
            }
            catch (Exception ex)
            {
                Debug.LogError($"[A2UISchemeA] RenderOverlay 失败 theme={_tokenVariant} components={state.Components.Count}: {ex}");
                throw;
            }
            Debug.Log($"[A2UISchemeA] RenderOverlay: showing card · components={state.Components.Count} · theme={_tokenVariant}");

            // 内联刷主题色（本引擎 USS 后代选择器不可靠，必须内联覆盖）
            PaintOverlayInline(card, _tokenVariant);
        }

        void PaintOverlayInline(VisualElement root, string theme)
        {
            var ink = GetThemeInk(theme);
            if (ink == null) return;
            ApplyInlineWalk(root, ink.Value);
            root.MarkDirtyRepaint();
        }

        struct ThemeInk
        {
            public Color Card, Text, Caption, Primary, PrimaryText, Secondary, SecondaryText, Border;
        }

        static ThemeInk? GetThemeInk(string theme) => theme switch
        {
            // M3 Light baseline：a 与 figma 之前返回 null 纯靠 USS，
            // 一旦有皮肤规则泄漏/变量缺失就整卡失控 —— 补内联保底
            "a" => new ThemeInk
            {
                Card = new Color(1f, 0.984f, 0.996f, 1f),
                Text = new Color(0.11f, 0.106f, 0.122f, 1f),
                Caption = new Color(0.286f, 0.271f, 0.31f, 1f),
                Primary = new Color(0.404f, 0.314f, 0.643f, 1f),
                PrimaryText = Color.white,
                Secondary = new Color(0.91f, 0.871f, 0.973f, 1f),
                SecondaryText = new Color(0.114f, 0.098f, 0.169f, 1f),
                Border = new Color(0.792f, 0.769f, 0.816f, 1f)
            },
            // 自动发现的 Figma 皮肤纯走 USS——内联 ink 会把 FigmaExport 的橙色主按钮
            // 覆盖成 M3 紫（951893a 前的行为就是返回 null）
            string t when t.StartsWith("figma-") => null,
            "dark" => new ThemeInk
            {
                Card = new Color(0.11f, 0.106f, 0.122f, 0.94f),
                Text = new Color(0.9f, 0.9f, 0.9f, 1f),
                Caption = new Color(0.7f, 0.7f, 0.7f, 1f),
                Primary = new Color(0.816f, 0.737f, 1f, 1f),
                PrimaryText = new Color(0.22f, 0.12f, 0.45f, 1f),
                Secondary = new Color(0.18f, 0.17f, 0.19f, 0.9f),
                SecondaryText = new Color(0.8f, 0.8f, 0.82f, 1f),
                Border = new Color(0.58f, 0.56f, 0.6f, 0.35f)
            },
            _ => null,  // a/figma/默认走 USS
        };

        static void ApplyInlineWalk(VisualElement ve, ThemeInk ink)
        {
            if (ve.ClassListContains("a2ui-card"))
            {
                ve.style.backgroundColor = ink.Card;
                ve.style.borderTopColor = ink.Border;
                ve.style.borderLeftColor = ink.Border;
                ve.style.borderRightColor = ink.Border;
                ve.style.borderBottomColor = new Color(ink.Border.r * 0.5f, ink.Border.g * 0.5f, ink.Border.b * 0.5f, ink.Border.a);
                ve.style.borderBottomWidth = 3;
                ve.style.borderTopWidth = 1;
                ve.style.borderLeftWidth = 1;
                ve.style.borderRightWidth = 1;
            }

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

            for (var i = 0; i < ve.childCount; i++)
                ApplyInlineWalk(ve[i], ink);
        }

        void ApplyOverlayCardBackground(VisualElement card, string theme)
        {
            var (bg, border) = theme switch
            {
                "ds"      => (new Color(0.075f, 0.102f, 0.141f, 0.96f), new Color(0.149f, 0.188f, 0.255f, 0.6f)),
                "dark"    => (new Color(0.11f, 0.106f, 0.122f, 0.94f), new Color(0.576f, 0.561f, 0.6f, 0.35f)),
                string t when t.StartsWith("figma-") => (new Color(0.976f, 0.98f, 0.984f, 1f), new Color(0.898f, 0.906f, 0.922f, 1f)),
                _         => (new Color(1f, 0.984f, 0.996f, 0.95f), new Color(0.4f, 0.314f, 0.643f, 0.25f)),
            };
            card.style.backgroundColor = bg;
            card.style.borderTopColor = border;
            card.style.borderLeftColor = border;
            card.style.borderRightColor = border;
            card.style.borderBottomColor = new Color(border.r * 0.5f, border.g * 0.5f, border.b * 0.5f, border.a);
            card.style.borderBottomWidth = 3;
            card.style.borderTopWidth = 1;
            card.style.borderLeftWidth = 1;
            card.style.borderRightWidth = 1;
        }

        /// <summary>
        /// 从提示词中自动检测主题。返回 "ice" | "b"(beach/青) | "pink" ，默认 "ice"(蓝)。
        /// </summary>
        static string AutoThemeFromPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return "aaos";
            var p = prompt.ToLowerInvariant();

            // M3 暗色（显式）
            if (p.Contains("m3 暗色") || p.Contains("material 暗") || p.Contains("纯黑主题"))
                return "dark";

            // beach 关键词 → 青皮肤（token--b）
            if (p.Contains("海") || p.Contains("沙滩") || p.Contains("暖") ||
                p.Contains("夏天") || p.Contains("橘") || p.Contains("橙") ||
                p.Contains("黄昏") || p.Contains("落日") || p.Contains("beach") ||
                p.Contains("sunset") || p.Contains("阳光") || p.Contains("日落"))
                return "b";

            // pink 关键词
            if (p.Contains("粉") || p.Contains("可爱") || p.Contains("甜") ||
                p.Contains("少女") || p.Contains("pink") || p.Contains("萌") ||
                p.Contains("樱花") || p.Contains("桃"))
                return "pink";

            // green（用户偏好：狗不喜欢蓝、喜欢绿）
            if (p.Contains("狗") || p.Contains("绿") || p.Contains("green")) return "green";

            // cloud：浅色 / 截图风 / 干净 / 白天
            if (p.Contains("浅色") || p.Contains("白色") || p.Contains("白天") ||
                p.Contains("cloud") || p.Contains("干净") || p.Contains("截图"))
                return "cloud";

            // aaos：车载 / 暗色 / 夜 / 驾驶
            if (p.Contains("车载") || p.Contains("车机") || p.Contains("驾驶") ||
                p.Contains("aaos") || p.Contains("暗色") || p.Contains("夜间") ||
                p.Contains("车里") || p.Contains("鹦鹉") || p.Contains("宠物") ||
                p.Contains("留守") || p.Contains("看护"))
                return "aaos";

            // ice
            if (p.Contains("冰") || p.Contains("雪") || p.Contains("冬") ||
                p.Contains("蓝") || p.Contains("ice") || p.Contains("寒") ||
                p.Contains("冷") || p.Contains("霜") || p.Contains("冻"))
                return "ice";

            return "aaos";
        }

        void ApplyThemeFromServer(string theme)
        {
            _tokenVariant = NormalizeTheme(theme);
            _lookout = "主题热切 " + _tokenVariant.ToUpperInvariant() + " · 结构不变只换皮";
            Debug.Log($"[A2UISchemeA] ApplyThemeFromServer: theme={theme} → normalized={_tokenVariant}");
            UpdateMetaLabels();
            Rerender();
        }

        public static string NormalizeTheme(string theme)
        {
            if (string.IsNullOrWhiteSpace(theme)) return "ds";
            var t = theme.Trim().ToLowerInvariant();
            switch (t)
            {
                case "a": return "a";
                case "dark": return "dark";
                case "ds": return "ds";
                case "figma": return "figma-figmaexport"; // 旧键兼容：注册表自动发现的键是 figma-<目录>
                default:
                    // figma 别名规范化：带连字符的简写（figma-export）直接命中解析；
                    // 无分隔变体（figmaexport / "figma export"）过不了 StartsWith("figma-")
                    // 检查、会在此被折叠成 ds——先问注册表容错解析出规范键。
                    // 无匹配（figma-dark 等不存在皮肤）不透传——未知键一律回落 DS，
                    // 避免“DS 结构 + 旧主题内联色”的混搭
                    var figmaAlias = A2uiThemeRegistry.ResolveFigmaAlias(t);
                    if (figmaAlias != null) return figmaAlias;
                    // ice/beach/pink/green/aaos/cloud/b 等装饰皮肤已在 951893a 裁剪，
                    // 未知键一律回落 DS，避免“DS 结构 + 旧主题内联色”的混搭
                    return "ds";
            }
        }

        static string ExtractFirstSurfaceId(string jsonl)
        {
            if (string.IsNullOrEmpty(jsonl)) return null;
            using var reader = new StringReader(jsonl);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                try
                {
                    var jo = JObject.Parse(t);
                    var body = (JObject)jo["surfaceUpdate"] ?? (JObject)jo["beginRendering"]
                        ?? (JObject)jo["dataModelUpdate"] ?? (JObject)jo["deleteSurface"];
                    var sid = body?["surfaceId"]?.Value<string>();
                    if (!string.IsNullOrEmpty(sid)) return sid;
                }
                catch { /* 跳过坏行 */ }
            }
            return null;
        }

        static string ThemeBadge(string variant) => variant switch
        {
            "ds" => "DS · 设计系统",
            "a" => "Token A · M3 Light",
            "dark" => "M3 Dark · 暗色",
            _ => variant.StartsWith("figma-") ? "Figma Export" : "Token " + variant.ToUpperInvariant()
        };

        VisualElement MakeCraftedSkin()
        {
            var skin = new VisualElement();
            skin.AddToClassList("a2ui-skin--crafted");
            if (_tokenVariant == "ds")
            {
                // DS 皮肤：挂 ds-root，别名层（A2uiAlias.uss）在该作用域下接管 a2ui 组件视觉
                skin.AddToClassList("ds-root");
                return skin;
            }
            // 一律走注册表：内置 a/b/dark 与自动发现的 figma-* 共用同一映射
            skin.AddToClassList(A2uiThemeRegistry.ScopeFor(_tokenVariant));
            return skin;
        }

        static string ThemeKeyFromLabel(string label)
        {
            foreach (var e in A2uiThemeRegistry.All())
                if (e.Label == label) return e.Key;
            return "ds";
        }

        void UpdateMetaLabels()
        {
            if (_lookoutLabel != null) _lookoutLabel.text = _lookout ?? "";
            if (_narrationLabel != null) _narrationLabel.text = _narration ?? "";
            if (_promptLabel != null) _promptLabel.text = "Prompt: " + (_lastPrompt ?? "");
            if (_jsonlLabel != null)
            {
                var body = !string.IsNullOrEmpty(_lastJsonlFull) ? _lastJsonlFull : (_lastJsonlPreview ?? "");
                _jsonlLabel.text = string.IsNullOrEmpty(body) ? "(无 JSONL)" : body;
            }

            if (_jsonlScroll != null)
            {
                if (!string.IsNullOrEmpty(_coverage?.FocusType))
                    _jsonlScroll.AddToClassList("a2ui-host__jsonl-scroll--unit");
                else
                    _jsonlScroll.RemoveFromClassList("a2ui-host__jsonl-scroll--unit");
            }

            if (_actionLabel != null) _actionLabel.text = _actionLog ?? "";
        }

        void OnUserAction(string name, JObject context)
        {
            var sid = FirstSurfaceId() ?? "";
            _recorder.RecordAction(name, context, "dispatch");
            _router?.Handle(name, context, sid);
            _actionLog = $"userAction: {name}  context={context}";
            UpdateMetaLabels();
            Debug.Log($"[A2UISchemeA] userAction name={name} context={context}");
        }

        string FirstSurfaceId()
        {
            if (_processor == null) return null;
            foreach (var kv in _processor.Surfaces)
                return kv.Key;
            return null;
        }

        A2uiV08SurfaceState FirstReadyState()
        {
            if (_processor == null) return null;
            foreach (var kv in _processor.Surfaces)
            {
                if (kv.Value.ReadyToRender)
                    return kv.Value;
            }

            return null;
        }

        static string PreviewJsonl(string jsonl)
        {
            if (string.IsNullOrEmpty(jsonl)) return "";
            var lines = jsonl.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var n = Math.Min(2, lines.Length);
            var preview = string.Join(" | ", lines, 0, n);
            return preview.Length > 220 ? preview.Substring(0, 220) + "…" : preview;
        }

        void EnsureStyles()
        {
#if UNITY_EDITOR
            if (hostStyle == null)
                hostStyle = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/A2UISchemeA/Styles/Host.uss");
            if (craftedStyle == null)
                craftedStyle = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/A2UISchemeA/Styles/Crafted.uss");
            if (tokensStyle == null)
                tokensStyle = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/A2UISchemeA/Styles/Tokens.uss");
            if (motionStyle == null)
                motionStyle = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/A2UISchemeA/Styles/Motion.uss");
            if (_dsStyles == null)
            {
                var list = new System.Collections.Generic.List<StyleSheet>();
                foreach (var p in DsStylePaths)
                {
                    var s = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(p);
                    if (s != null) list.Add(s);
                    else Debug.LogWarning("[A2UISchemeA] DS 样式表未找到: " + p);
                }
                _dsStyles = list.ToArray();
            }
            // 每次都刷新 Figma 样式：避免首次 OnEnable 时资源库未就绪导致缓存空数组，且保证新导入的 USS 立刻生效。
            RefreshFigmaStyles();
#endif
        }

#if UNITY_EDITOR
        /// <summary>强制刷新所有自动发现的 Figma USS。用 [FIGMA] 前缀日志，方便在 Console 里一键搜索。</summary>
        void RefreshFigmaStyles()
        {
            var discovered = A2uiThemeRegistry.DiscoveredStylePaths();
            Debug.Log($"[FIGMA] RefreshFigmaStyles: 发现 {discovered.Count} 个 USS 路径");
            if (discovered.Count > 0)
            {
                // 先强制递归导入 Styles 目录，确保所有 .uss 都被识别成 StyleSheet 资源
                UnityEditor.AssetDatabase.ImportAsset("Assets/A2UISchemeA/Styles", UnityEditor.ImportAssetOptions.ImportRecursive);
            }
            var list = new System.Collections.Generic.List<StyleSheet>();
            foreach (var p in discovered)
            {
                var s = LoadStyleRobust(p);
                if (s != null)
                {
                    list.Add(s);
                    Debug.Log($"[FIGMA] OK ({list.Count}/{discovered.Count}): {p}");
                }
                else
                {
                    Debug.LogError($"[FIGMA] FAIL: {p} · 文件存在={System.IO.File.Exists(p)}");
                }
            }
            _figmaStyles = list.ToArray();
            Debug.Log($"[FIGMA] 最终加载 {_figmaStyles.Length}/{discovered.Count} 个 Figma 样式表");
        }
#endif

        void ApplyStyleSheets(VisualElement root)
        {
            TryAddStyle(root, hostStyle);
            TryAddStyle(root, craftedStyle);
            TryAddStyle(root, tokensStyle);
            TryAddStyle(root, motionStyle);
            if (_dsStyles != null)
                foreach (var s in _dsStyles) TryAddStyle(root, s);
#if UNITY_EDITOR
            // 在把 Figma 样式挂到 root 之前，强制再刷新一次，绕开 OnEnable 时资源库未就绪的问题
            RefreshFigmaStyles();
#endif
            if (_figmaStyles != null)
                foreach (var s in _figmaStyles) TryAddStyle(root, s);
        }

        static void TryAddStyle(VisualElement root, StyleSheet sheet)
        {
            if (sheet == null) return;
            for (var i = 0; i < root.styleSheets.count; i++)
            {
                if (root.styleSheets[i] == sheet) return;
            }

            root.styleSheets.Add(sheet);
        }

        /// <summary>
        /// 稳健加载一份 USS：优先走资源数据库；若未识别成 StyleSheet 资源（如刚创建尚未 import、
        /// 或首次 Play 启动资源库尚未就绪），则强制 ImportAsset 一次再取，保证皮肤一定上得去。
        /// 注：本引擎（团结/Unity 2022.3）无 StyleSheet.FromUssString，故用 ImportAsset 兜底。
        /// </summary>
        static StyleSheet LoadStyleRobust(string path)
        {
#if UNITY_EDITOR
            if (!System.IO.File.Exists(path)) return null;
            var s = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (s == null)
            {
                // 资源库尚未把这份 .uss 识别成 StyleSheet，强制重新导入后再取
                UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
                s = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                if (s == null)
                    Debug.LogWarning("[A2UISchemeA] LoadStyleRobust: ImportAsset 后仍取不到 StyleSheet: " + path);
            }
            return s;
#else
            return null;
#endif
        }

        void EnsurePanelSettings(UIDocument doc)
        {
            if (doc.panelSettings == null)
            {
                var settings = ScriptableObject.CreateInstance<PanelSettings>();
                settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                settings.referenceResolution = new Vector2Int(1920, 1080);
                doc.panelSettings = settings;
            }

            if (doc.panelSettings.themeStyleSheet == null)
            {
                var theme = runtimeTheme;
#if UNITY_EDITOR
                if (theme == null)
                {
                    theme = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                        "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");
                }
#endif
                if (theme != null)
                    doc.panelSettings.themeStyleSheet = theme;
            }
        }

        static string ResolvePath(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            if (Path.IsPathRooted(p) && File.Exists(p)) return p;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", p));
        }
    }

    static class A2uiVeExt
    {
        public static T WithClass<T>(this T ve, string className) where T : VisualElement
        {
            ve.AddToClassList(className);
            return ve;
        }
    }
}
