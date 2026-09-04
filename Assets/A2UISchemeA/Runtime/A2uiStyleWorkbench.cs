using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA
{
    /// <summary>
    /// 原子组件样式工作台：把协议所有原子组件平铺在场景中（不重叠），
    /// 顶栏可切换 USS 皮肤（整树重应用），点组件高亮并同步 Hierarchy 选中，
    /// Inspector 改字段即时重建，导出按钮把样式覆盖写成 USS 规则保存。
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class A2uiStyleWorkbench : MonoBehaviour
    {
        [Header("USS 皮肤源")]
        [SerializeField] StyleSheet craftedStyle;
        [SerializeField] StyleSheet tokensStyle;
        [Tooltip("导出 USS 覆盖规则的目标文件（Assets 相对路径）")]
        [SerializeField] string exportPath = "Assets/A2UISchemeA/Styles/WorkbenchOverrides.uss";

        UIDocument _doc;
        VisualElement _root;
        VisualElement _stage;          // 组件平铺滚动区
        DropdownField _skinDrop;
        VisualElement _selectedCell;
        A2uiAtomicComponent _selectedComponent;
        string _currentSkin = SkinDs;            // 当前皮肤，跨重建保持
        readonly Dictionary<A2uiAtomicComponent, VisualElement> _cellMap = new Dictionary<A2uiAtomicComponent, VisualElement>();
        string _lastFullSig;

        public const string SkinDs = "DS 设计系统";
        public const string SkinCrafted = "Crafted 打磨 (M3)";

        void OnEnable()
        {
            _doc = GetComponent<UIDocument>();
            // PanelSettings 可能被 EnsureScene 删建导致 GUID 失配存成 fileID:0，
            // 这里兜底：为空就按固定路径重新挂上，确保一按 Play 就能渲染。
            if (_doc.panelSettings == null)
            {
#if UNITY_EDITOR
                var panel = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>(
                    "Assets/A2UISchemeA/PanelSettings.asset");
                if (panel != null)
                    _doc.panelSettings = panel;
                else
                    Debug.LogError("[Workbench] 找不到 PanelSettings.asset，UI 无法渲染");
#else
                Debug.LogError("[Workbench] 运行时 PanelSettings 为空，UI 无法渲染");
#endif
            }
            A2uiAtomicComponent.OnAnyChanged += OnComponentChanged;
#if UNITY_EDITOR
            UnityEditor.Selection.selectionChanged += OnEditorSelectionChanged;
            UnityEditor.EditorApplication.update += EditorPoll;
#endif
            _doc.rootVisualElement.schedule.Execute(Build).StartingIn(0);
        }

        void OnDisable()
        {
            A2uiAtomicComponent.OnAnyChanged -= OnComponentChanged;
#if UNITY_EDITOR
            UnityEditor.Selection.selectionChanged -= OnEditorSelectionChanged;
            UnityEditor.EditorApplication.update -= EditorPoll;
#endif
        }

        void Update() => Poll();

#if UNITY_EDITOR
        void EditorPoll() => Poll();
#endif

        // 不依赖 OnValidate 时机：每帧比对组件序列化状态，有变化就只重建平铺区。
        // 编辑模式（EditorApplication.update）和运行时（Update）都触发，所见即所得必响。
        void Poll()
        {
            if (_root == null || _stage == null) return;
            var sb = new System.Text.StringBuilder();
            foreach (var c in GetComponents())
                sb.Append(c.GetInstanceID()).Append('|').Append(JsonUtility.ToJson(c)).Append(';');
            var sig = sb.ToString();
            if (sig != _lastFullSig)
            {
                _lastFullSig = sig;
                PopulateStage();
            }
        }

        void OnComponentChanged(A2uiAtomicComponent comp)
        {
            if (_stage == null) return;
            RebuildStage();
        }

        /// <summary>编辑器菜单在 Hierarchy 新增组件后调用，整体重铺场景。</summary>
        public void Rebuild() => Build();

        void Build()
        {
            _root = _doc.rootVisualElement;
            _root.Clear();
            _root.style.flexGrow = 1;
            _root.style.width = Length.Percent(100);
            _root.style.height = Length.Percent(100);
            _root.style.flexDirection = FlexDirection.Column;

            _root.AddToClassList("a2ui-workbench");
            _root.AddToClassList("a2ui-skin--crafted"); // 让 Crafted 样式覆盖生效，皮肤下拉可再换

            BuildTopBar();
            BuildStage();
        }

        void BuildTopBar()
        {
            var bar = new VisualElement();
            bar.AddToClassList("a2ui-workbench__bar");
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.alignItems = Align.Center;
            SetPadding(bar, 8);
            bar.style.borderBottomWidth = 1;
            bar.style.borderBottomColor = new Color(0.2f, 0.3f, 0.45f);
            bar.style.backgroundColor = new Color(0.10f, 0.14f, 0.20f);

            var title = new Label("原子组件样式工作台");
            title.AddToClassList("a2ui-workbench__title");
            title.style.fontSize = 18;
            bar.Add(title);

            var skinOptions = new List<string> { SkinDs, SkinCrafted };
            _skinDrop = new DropdownField(skinOptions, Math.Max(0, skinOptions.IndexOf(_currentSkin)));
            _skinDrop.style.minWidth = 220;
            _skinDrop.style.marginLeft = 16;
            _skinDrop.style.marginRight = 8;
            _skinDrop.RegisterValueChangedCallback(evt =>
            {
                _currentSkin = evt.newValue;
                ApplySkin(_currentSkin);
                PopulateStage();
            });
            bar.Add(_skinDrop);

            var saveBtn = new Button(SaveOverrides) { text = "导出 USS 覆盖" };
            saveBtn.style.marginLeft = 8;
            bar.Add(saveBtn);

            var hint = new Label("点组件 = Hierarchy 选中；Inspector 改参数即所见即所得");
            hint.style.marginLeft = 24;
            hint.style.color = new Color(0.7f, 0.8f, 0.95f);
            hint.style.fontSize = 13;
            bar.Add(hint);

            _root.Add(bar);
        }

        void BuildStage()
        {
            _stage = new ScrollView();
            _stage.AddToClassList("a2ui-workbench__stage");
            _stage.style.flexGrow = 1;
            _stage.contentContainer.style.flexDirection = FlexDirection.Row;
            _stage.contentContainer.style.flexWrap = Wrap.Wrap;
            _stage.contentContainer.style.alignContent = Align.FlexStart;
            _root.Add(_stage);
            PopulateStage();
            ApplySkin(_currentSkin);
        }

        /// <summary>只重建平铺区（不碰顶栏/皮肤），用于改参、换肤后的轻量刷新。</summary>
        void PopulateStage()
        {
            _cellMap.Clear();
            _stage.contentContainer.Clear();
            var comps = GetComponents();
            if (comps.Count == 0)
            {
                _stage.contentContainer.Add(new Label("场景里还没有原子组件：菜单 A2UI Scheme A → 创建原子组件样式工作台，或手动 New GameObject 挂 A2uiAtomicComponent。"));
                return;
            }

            foreach (var comp in comps)
            {
                var cell = BuildCell(comp);
                _cellMap[comp] = cell;
                _stage.contentContainer.Add(cell);
            }

            // 重建后恢复选中高亮
            if (_selectedComponent != null && _cellMap.TryGetValue(_selectedComponent, out var sc))
                HighlightCell(sc);
        }

        /// <summary>数据变化（Inspector 改参）后的轻量刷新，只重建平铺区。</summary>
        void RebuildStage() => PopulateStage();

        List<A2uiAtomicComponent> GetComponents()
        {
            // 场景里可能有不激活的残留，过滤掉
            return FindObjectsOfType<A2uiAtomicComponent>()
                .Where(c => c != null && c.gameObject.activeInHierarchy)
                .OrderBy(c => (int)c.Type)
                .ThenBy(c => c.gameObject.name, StringComparer.Ordinal)
                .ToList();
        }

        VisualElement BuildCell(A2uiAtomicComponent comp)
        {
            var cell = new VisualElement();
            cell.AddToClassList("a2ui-workbench__cell");
            cell.pickingMode = PickingMode.Position; // 确保能收到点击，点中即选中对应组件
            cell.style.flexDirection = FlexDirection.Column;
            cell.style.width = 300;
            SetMargin(cell, 10);
            SetPadding(cell, 12);
            SetBorderWidth(cell, 1);
            SetBorderColor(cell, new Color(0.25f, 0.35f, 0.5f));
            SetBorderRadius(cell, 10);
            cell.style.backgroundColor = new Color(0.13f, 0.18f, 0.26f, 0.92f);

            // 标题行：类型名 + 规则选择器
            var head = new Label($"{comp.Type}  ·  {comp.RuleSelector()}");
            head.style.color = new Color(0.55f, 0.75f, 1f);
            head.style.fontSize = 13;
            head.style.marginBottom = 8;
            cell.Add(head);

            var body = BuildAtomic(comp);
            cell.Add(body);

            // 点 cell → 高亮 + Hierarchy 选中对应 GameObject（用 PointerDown 更稳，避免被 ScrollView 吞掉）
            cell.RegisterCallback<PointerDownEvent>(evt =>
            {
                Select(comp, cell);
            });

            return cell;
        }

        void Select(A2uiAtomicComponent comp, VisualElement cell)
        {
            _selectedComponent = comp;
            HighlightCell(cell);
#if UNITY_EDITOR
            UnityEditor.Selection.activeGameObject = comp.gameObject;
#endif
        }

        void HighlightCell(VisualElement cell)
        {
            if (_selectedCell != null && _selectedCell != cell)
            {
                SetBorderColor(_selectedCell, new Color(0.25f, 0.35f, 0.5f));
                SetBorderWidth(_selectedCell, 1);
            }
            _selectedCell = cell;
            SetBorderColor(cell, new Color(0.4f, 1f, 0.7f));
            SetBorderWidth(cell, 2);
        }

        // 在 Hierarchy 选中某组件 GameObject 时，反向高亮对应 cell（不回写 Selection，避免递归）
#if UNITY_EDITOR
        void OnEditorSelectionChanged()
        {
            var go = UnityEditor.Selection.activeGameObject;
            if (go == null) return;
            var comp = go.GetComponent<A2uiAtomicComponent>();
            if (comp != null && _cellMap.TryGetValue(comp, out var cell))
            {
                _selectedComponent = comp;
                HighlightCell(cell);
            }
        }
#endif

        // ===================== 组件构建 =====================

        VisualElement BuildAtomic(A2uiAtomicComponent c)
        {
            VisualElement ve = c.Type switch
            {
                A2uiAtomicComponent.AtomicType.Text => BuildText(c),
                A2uiAtomicComponent.AtomicType.Button => BuildButton(c),
                A2uiAtomicComponent.AtomicType.CheckBox => BuildCheckBox(c),
                A2uiAtomicComponent.AtomicType.Slider => BuildSlider(c),
                A2uiAtomicComponent.AtomicType.MultipleChoice => BuildChoice(c),
                A2uiAtomicComponent.AtomicType.Divider => BuildDivider(),
                A2uiAtomicComponent.AtomicType.Card => BuildCard(c),
                A2uiAtomicComponent.AtomicType.Row => BuildFlex(c, FlexDirection.Row, "a2ui-row"),
                A2uiAtomicComponent.AtomicType.Column => BuildFlex(c, FlexDirection.Column, "a2ui-column"),
                A2uiAtomicComponent.AtomicType.MediaMiniBar => BuildMediaMiniBar(c),
                A2uiAtomicComponent.AtomicType.ClimateStep => BuildClimateStep(c),
                A2uiAtomicComponent.AtomicType.RestBanner => BuildRestBanner(c),
                A2uiAtomicComponent.AtomicType.Image => BuildImage(c),
                A2uiAtomicComponent.AtomicType.Tabs => BuildTabs(c),
                A2uiAtomicComponent.AtomicType.TextField => BuildTextField(c),
                A2uiAtomicComponent.AtomicType.DateTimeInput => BuildDateTime(c),
                _ => new Label("[unknown]")
            };

            ApplyOverrides(ve, c);
            return ve;
        }

        VisualElement BuildText(A2uiAtomicComponent c)
        {
            var label = new Label(c.Text);
            label.AddToClassList("a2ui-text");
            label.AddToClassList("a2ui-text--" + c.UsageHint);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        VisualElement BuildButton(A2uiAtomicComponent c)
        {
            var btn = new Button(() => Debug.Log($"[Workbench] 点击按钮 {c.Text} · action 占位")) { text = c.Text };
            btn.AddToClassList("a2ui-btn");
            btn.AddToClassList(c.Primary ? "a2ui-btn--primary" : "a2ui-btn--secondary");
            return btn;
        }

        VisualElement BuildCheckBox(A2uiAtomicComponent c)
        {
            var wrap = new VisualElement();
            wrap.AddToClassList("a2ui-checkbox-wrap");
            wrap.style.width = Length.Percent(100);
            var toggle = new Toggle(c.Label) { value = c.IsChecked };
            toggle.AddToClassList("a2ui-checkbox");
            toggle.RegisterValueChangedCallback(evt => c.IsChecked = evt.newValue);
            wrap.Add(toggle);
            return wrap;
        }

        VisualElement BuildSlider(A2uiAtomicComponent c)
        {
            var slider = new Slider(c.Label, c.MinValue, c.MaxValue) { value = c.Value, showInputField = true };
            slider.AddToClassList("a2ui-slider");
            slider.RegisterValueChangedCallback(evt => c.Value = evt.newValue);
            return slider;
        }

        VisualElement BuildChoice(A2uiAtomicComponent c)
        {
            var col = new VisualElement();
            col.AddToClassList("a2ui-choice");
            col.style.flexDirection = FlexDirection.Column;
            for (var i = 0; i < c.Options.Count; i++)
            {
                var t = new Toggle(c.Options[i]) { value = i == 0 };
                t.AddToClassList("a2ui-choice__option");
                col.Add(t);
            }
            return col;
        }

        VisualElement BuildDivider()
        {
            var line = new VisualElement();
            line.AddToClassList("a2ui-divider");
            line.AddToClassList("a2ui-divider--horizontal");
            return line;
        }

        VisualElement BuildCard(A2uiAtomicComponent c)
        {
            var card = new VisualElement();
            card.AddToClassList("a2ui-card");
            card.style.flexDirection = FlexDirection.Column;
            SetPadding(card, 12);
            var label = new Label(c.Label);
            label.AddToClassList("a2ui-text");
            label.AddToClassList("a2ui-text--body");
            card.Add(label);
            return card;
        }

        VisualElement BuildFlex(A2uiAtomicComponent c, FlexDirection dir, string cls)
        {
            var ve = new VisualElement();
            ve.AddToClassList(cls);
            ve.style.flexDirection = dir;
            ve.style.flexWrap = Wrap.Wrap;
            for (var i = 0; i < 3; i++)
            {
                var box = new Label($"子{i + 1}");
                box.AddToClassList("a2ui-text");
                box.AddToClassList("a2ui-text--caption");
                SetBorderWidth(box, 1);
                SetBorderColor(box, new Color(0.4f, 0.5f, 0.65f));
                SetPadding(box, 6);
                SetMargin(box, 3);
                ve.Add(box);
            }
            return ve;
        }

        VisualElement BuildMediaMiniBar(A2uiAtomicComponent c)
        {
            var row = new VisualElement();
            row.AddToClassList("a2ui-cabin");
            row.AddToClassList("a2ui-cabin--media");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            var cover = new Label("♪");
            cover.AddToClassList("a2ui-cabin__cover");
            row.Add(cover);
            var col = new VisualElement();
            col.AddToClassList("a2ui-cabin__meta");
            col.style.flexGrow = 1;
            var title = new Label(c.Title);
            title.AddToClassList("a2ui-text");
            title.AddToClassList("a2ui-text--h4");
            col.Add(title);
            var status = new Label("已暂停");
            status.AddToClassList("a2ui-cabin__status");
            col.Add(status);
            row.Add(col);
            var play = new Button(() => Debug.Log("[Workbench] toggle_play")) { text = "▶ 播放" };
            play.AddToClassList("a2ui-cabin__play");
            row.Add(play);
            return row;
        }

        VisualElement BuildClimateStep(A2uiAtomicComponent c)
        {
            var col = new VisualElement();
            col.AddToClassList("a2ui-cabin");
            col.AddToClassList("a2ui-cabin--climate");
            col.style.flexDirection = FlexDirection.Column;
            var label = new Label(c.TempLabel);
            label.AddToClassList("a2ui-text");
            label.AddToClassList("a2ui-text--h1");
            col.Add(label);
            return col;
        }

        VisualElement BuildRestBanner(A2uiAtomicComponent c)
        {
            var col = new VisualElement();
            col.AddToClassList("a2ui-cabin");
            col.AddToClassList("a2ui-cabin--rest");
            col.style.flexDirection = FlexDirection.Column;
            col.style.alignItems = Align.Center;
            col.style.minHeight = 120;
            col.style.width = Length.Percent(100);
            var tag = new Label("RestBanner");
            tag.AddToClassList("a2ui-cabin__tag");
            col.Add(tag);
            var label = new Label(c.Label);
            label.AddToClassList("a2ui-text");
            label.AddToClassList("a2ui-text--h1");
            col.Add(label);
            return col;
        }

        VisualElement BuildImage(A2uiAtomicComponent c)
        {
            var box = new VisualElement();
            box.AddToClassList("a2ui-image");
            box.style.minHeight = 80;
            var caption = new Label(c.Label);
            caption.AddToClassList("a2ui-image__caption");
            box.Add(caption);
            return box;
        }

        VisualElement BuildTabs(A2uiAtomicComponent c)
        {
            var root = new VisualElement();
            root.AddToClassList("a2ui-tabs");
            root.style.flexDirection = FlexDirection.Column;
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            root.Add(header);
            foreach (var opt in c.Options)
                header.Add(new Button(() => { }) { text = opt });
            return root;
        }

        VisualElement BuildTextField(A2uiAtomicComponent c)
        {
            var tf = new TextField(c.Label) { value = c.PlaceholderText };
            tf.AddToClassList("a2ui-textfield");
            return tf;
        }

        VisualElement BuildDateTime(A2uiAtomicComponent c)
        {
            var tf = new TextField("DateTime+date") { value = c.PlaceholderText };
            tf.AddToClassList("a2ui-datetime");
            return tf;
        }

        void ApplyOverrides(VisualElement ve, A2uiAtomicComponent c)
        {
            if (c.OverrideFontSize) ve.style.fontSize = c.FontSize;
            if (c.OverrideColor) ve.style.color = c.Color;
            if (c.OverrideBackground) ve.style.backgroundColor = c.BackgroundColor;
            if (c.OverrideBorderRadius) SetBorderRadius(ve, c.BorderRadius);
            if (c.OverridePadding) SetPadding(ve, c.Padding);
        }

        // ===================== 皮肤切换 =====================

        // 按路径兜底加载 USS：场景里序列化引用可能为空，这里保证一定能取到。
        static StyleSheet LoadStyle(string fileName)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/A2UISchemeA/Styles/" + fileName);
#else
            return null;
#endif
        }

        void ApplySkin(string skin)
        {
            if (_root == null) return;
            _root.styleSheets.Clear();
            _root.RemoveFromClassList("a2ui-skin--crafted");
            _root.RemoveFromClassList("a2ui-token--ice");
            _root.RemoveFromClassList("ds-root");

            void Add(StyleSheet s) { if (s != null) _root.styleSheets.Add(s); }

            switch (skin)
            {
                case SkinDs:
                    // DS 设计系统（sinanata/unity-ui-toolkit-design-system，MIT）全套样式 + ds-root 作用域，作为默认皮肤
                    A2uiDsStyles.Apply(_root);
                    break;
                default: // Crafted 打磨（M3 语义层兜底）
                    Add(craftedStyle != null ? craftedStyle : LoadStyle("Crafted.uss"));
                    _root.AddToClassList("a2ui-skin--crafted");
                    break;
            }
        }

        // ===================== 保存：导出 USS 覆盖 =====================

        void SaveOverrides()
        {
            var comps = GetComponents();
            var sb = new StringBuilder();
            sb.AppendLine("/* ===== A2UI 原子组件样式工作台导出（WorkbenchOverrides）===== */");
            sb.AppendLine("/* 由 A2uiStyleWorkbench 生成。改「原子组件样式工作台」场景后再次导出会整体覆盖本文件。 */");
            sb.AppendLine();

            foreach (var c in comps.Where(c => c.HasOverrides))
            {
                sb.AppendLine(c.RuleSelector() + " {");
                if (c.OverrideFontSize) sb.AppendLine($"    font-size: {c.FontSize}px;");
                if (c.OverrideColor) sb.AppendLine($"    color: {ColorToUss(c.Color)};");
                if (c.OverrideBackground) sb.AppendLine($"    background-color: {ColorToUss(c.BackgroundColor)};");
                if (c.OverrideBorderRadius) sb.AppendLine($"    border-radius: {c.BorderRadius}px;");
                if (c.OverridePadding) sb.AppendLine($"    padding: {c.Padding}px;");
                sb.AppendLine("}");
                sb.AppendLine();
            }

            var abs = Path.Combine(Directory.GetCurrentDirectory(), exportPath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(abs));
                File.WriteAllText(abs, sb.ToString(), new UTF8Encoding(true));
                Debug.Log($"[Workbench] 已导出 {exportPath}（{comps.Count(c => c.HasOverrides)} 条规则）");
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
            }
            catch (Exception e)
            {
                Debug.LogError($"[Workbench] 导出失败: {e.Message}");
            }
        }

        // IStyle 没有 padding/margin/borderWidth/borderColor/borderRadius 这类整体属性，
        // 只有拆开的 Left/Right/Top/Bottom，这里用辅助方法统一赋值。
        static void SetPadding(VisualElement ve, float v)
        {
            ve.style.paddingLeft = ve.style.paddingRight = ve.style.paddingTop = ve.style.paddingBottom = v;
        }
        static void SetMargin(VisualElement ve, float v)
        {
            ve.style.marginLeft = ve.style.marginRight = ve.style.marginTop = ve.style.marginBottom = v;
        }
        static void SetBorderWidth(VisualElement ve, float v)
        {
            ve.style.borderLeftWidth = ve.style.borderRightWidth = ve.style.borderTopWidth = ve.style.borderBottomWidth = v;
        }
        static void SetBorderColor(VisualElement ve, Color c)
        {
            ve.style.borderLeftColor = ve.style.borderRightColor = ve.style.borderTopColor = ve.style.borderBottomColor = c;
        }
        static void SetBorderRadius(VisualElement ve, float v)
        {
            ve.style.borderTopLeftRadius = ve.style.borderTopRightRadius = ve.style.borderBottomLeftRadius = ve.style.borderBottomRightRadius = v;
        }

        static string ColorToUss(Color c) =>
            c.a < 1f
                ? $"rgba({(int)(c.r * 255)}, {(int)(c.g * 255)}, {(int)(c.b * 255)}, {c.a:0.##})"
                : $"rgb({(int)(c.r * 255)}, {(int)(c.g * 255)}, {(int)(c.b * 255)})";
    }
}
