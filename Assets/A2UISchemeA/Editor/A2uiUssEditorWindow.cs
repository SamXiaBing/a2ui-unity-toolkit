using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using A2UISchemeA;

namespace A2uiSchemeA.Editor
{
    /// <summary>
    /// USS 可视化修改器（不依赖 Play）。
    /// - 顶部下拉选要改的 USS 文件；也能输入文件名新建一个 USS
    /// - 选组件类型（或在 Hierarchy 选中原子组件自动跟随），右侧实时渲染该组件（应用所选 USS 样式）
    /// - 组件参数面板：所见即所得改预览（改完立即重渲染）
    /// - USS 规则编辑：列出所选 USS 的全部规则，改属性后点「确定修改」或勾「实时同步」写回保存
    ///
    /// 菜单：A2UI Scheme A → USS 样式编辑器
    /// </summary>
    public class A2uiUssEditorWindow : EditorWindow
    {
        const string StylesDir = "Assets/A2UISchemeA/Styles";

        [MenuItem("A2UI Scheme A/USS 样式编辑器")]
        public static A2uiUssEditorWindow Open()
        {
            var w = GetWindow<A2uiUssEditorWindow>("USS 样式编辑器");
            w.minSize = new Vector2(720, 520);
            return w;
        }

        // ----- 状态 -----
        List<string> _ussNames = new List<string>();
        DropdownField _ussDrop;
        TextField _newNameField;
        DropdownField _typeDrop;
        Toggle _followToggle;

        ScrollView _previewScroll;
        VisualElement _previewRoot;

        IMGUIContainer _ussContainer;

        A2uiAtomicComponent _comp;          // 示例组件（仅用于左侧预览渲染，不显示 Inspector）
        GameObject _compGo;

        A2uiAtomicComponent _previewOverride; // 自动跟随时临时指向 Hierarchy 选中的组件（只读）

        int _propPickIndex;                 // USS 属性调色板下拉索引

        string _selectedUssPath;
        string _selectedUssName;
        UssDocument _doc;
        string[] _ruleNames = new string[0];
        int _ruleIndex;

        Vector2 _ussScroll;
        bool _liveSync;
        string _status = "";

        void OnEnable()
        {
            _liveSync = EditorPrefs.GetBool("A2uiUssStudio.LiveSync", false);
            CreatePreviewComponent();
            BuildLayout();
            RefreshUssList();
            LoadUss("Crafted.uss");
            Selection.selectionChanged += OnSelectionChanged;
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            EditorPrefs.SetBool("A2uiUssStudio.LiveSync", _liveSync);
            if (_compGo != null) DestroyImmediate(_compGo);
        }

        // 临时示例组件（不进场景、不持久化），仅用于左侧预览渲染
        void CreatePreviewComponent()
        {
            if (_compGo != null) return;
            _compGo = new GameObject("__USS_Preview_Comp");
            _compGo.hideFlags = HideFlags.HideAndDontSave;
            _comp = _compGo.AddComponent<A2uiAtomicComponent>();
            _comp.hideFlags = HideFlags.HideAndDontSave;
        }

        // ===================== 布局（纯 UI Toolkit）=====================
        void BuildLayout()
        {
            var root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;

            // 顶部工具栏
            var bar = new VisualElement();
            bar.style.flexDirection = FlexDirection.Row;
            bar.style.paddingTop = bar.style.paddingBottom = 6;
            bar.style.paddingLeft = bar.style.paddingRight = 8;
            bar.style.backgroundColor = new Color(0.16f, 0.18f, 0.22f, 1f);

            _ussDrop = new DropdownField("USS 文件") { choices = _ussNames, value = _selectedUssName };
            _ussDrop.style.minWidth = 180;
            _ussDrop.RegisterValueChangedCallback(evt => LoadUss(evt.newValue));
            bar.Add(_ussDrop);

            _newNameField = new TextField("新建") { value = "" };
            _newNameField.style.minWidth = 120;
            bar.Add(_newNameField);

            var newBtn = new Button(() => CreateUss(_newNameField.value)) { text = "创建" };
            bar.Add(newBtn);

            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            bar.Add(spacer);

            var refreshBtn = new Button(() => LoadUss(_selectedUssName)) { text = "刷新预览" };
            refreshBtn.tooltip = "重新从磁盘读取该 USS（含外部编辑器的改动）并重建左侧预览";
            bar.Add(refreshBtn);

            _typeDrop = new DropdownField("组件")
            {
                choices = new List<string>(System.Enum.GetNames(typeof(A2uiAtomicComponent.AtomicType)))
            };
            _typeDrop.style.minWidth = 160;
            _typeDrop.RegisterValueChangedCallback(evt =>
            {
                _comp.Type = (A2uiAtomicComponent.AtomicType)System.Enum.Parse(typeof(A2uiAtomicComponent.AtomicType), evt.newValue);
                _previewOverride = null;
                RefreshPreview();
                FocusRuleFor(_comp);
            });
            bar.Add(_typeDrop);

            _followToggle = new Toggle("自动跟随选中") { value = false };
            _followToggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue) OnSelectionChanged();
                else { _previewOverride = null; RefreshPreview(); FocusRuleFor(_comp); }
            });
            bar.Add(_followToggle);

            root.Add(bar);

            // 主体：左预览 / 右参数+USS
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;

            _previewScroll = new ScrollView();
            _previewScroll.style.width = Length.Percent(42);
            _previewScroll.style.flexShrink = 0;
            _previewRoot = new VisualElement();
            _previewRoot.style.flexGrow = 1;
            _previewScroll.Add(_previewRoot);
            body.Add(_previewScroll);

            var right = new ScrollView();
            right.style.flexGrow = 1;

            var ussFold = new Foldout { text = "USS 规则编辑", value = true };
            _ussContainer = new IMGUIContainer(UssGUI);
            _ussContainer.style.height = 460;   // 固定高度，让内部属性列表可滚轮滚动，避免内容溢出重叠
            ussFold.Add(_ussContainer);
            right.Add(ussFold);

            body.Add(right);
            root.Add(body);

            var hint = new Label("提示：左侧是所选组件按该 USS 渲染的预览；右侧选规则、改属性 → 点「确定修改」或勾「实时同步」写回 .uss 并保存。改完左侧预览立即变。");
            hint.style.paddingLeft = hint.style.paddingRight = 8;
            hint.style.paddingBottom = 4;
            hint.style.color = new Color(0.7f, 0.75f, 0.85f, 1f);
            root.Add(hint);
        }

        // ===================== USS 文件 =====================
        void RefreshUssList()
        {
            _ussNames.Clear();
            var guids = AssetDatabase.FindAssets("t:StyleSheet", new[] { StylesDir });
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                _ussNames.Add(Path.GetFileName(p));
            }
            _ussNames.Sort();
            if (_ussDrop != null)
            {
                _ussDrop.choices = _ussNames;
                if (_ussNames.Contains(_selectedUssName)) _ussDrop.value = _selectedUssName;
            }
        }

        void LoadUss(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            _selectedUssName = fileName;
            _selectedUssPath = StylesDir + "/" + fileName;
            var full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, _selectedUssPath);
            if (!File.Exists(full)) { _status = "找不到 " + full; return; }
            _doc = UssDocument.Parse(File.ReadAllText(full));
            _ruleNames = new string[_doc.Rules.Count];
            for (int i = 0; i < _doc.Rules.Count; i++) _ruleNames[i] = _doc.Rules[i].Selector;
            _ruleIndex = 0;
            if (_ussDrop != null) _ussDrop.value = fileName;
            RefreshPreview();
            _ussContainer?.MarkDirtyRepaint();
        }

        void CreateUss(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) { _status = "请输入文件名"; return; }
            var name = rawName.Trim().Replace("\\", "/");
            if (!name.EndsWith(".uss")) name += ".uss";
            var path = StylesDir + "/" + name;
            if (File.Exists(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path)))
            { _status = "已存在：" + name; return; }
            // 给一个最小可用模板
            File.WriteAllText(
                Path.Combine(Directory.GetParent(Application.dataPath).FullName, path),
                "/* 新建样式表：在下方写规则，例如覆盖按钮颜色\n" +
                ".a2ui-skin--crafted .a2ui-btn--primary {\n    background-color: rgb(40, 120, 255);\n}\n");
            AssetDatabase.Refresh();
            RefreshUssList();
            LoadUss(name);
            _newNameField.value = "";
            _status = "已创建 " + name;
        }

        // ===================== 预览 =====================
        StyleSheet LoadStyle(string fileName)
            => AssetDatabase.LoadAssetAtPath<StyleSheet>(StylesDir + "/" + fileName);

        void RefreshPreview()
        {
            if (_previewRoot == null || _comp == null) return;
            var src = _previewOverride ?? _comp;

            _previewRoot.styleSheets.Clear();
            _previewRoot.ClearClassList();
            _previewRoot.Clear();

            A2uiAtomicRenderer.ResolveSkin(_selectedUssName, out var classes, out var baseUss);
            foreach (var b in baseUss)
            {
                var s = LoadStyle(b);
                if (s != null) _previewRoot.styleSheets.Add(s);
            }
            if (!baseUss.Contains(_selectedUssName))
            {
                var s = LoadStyle(_selectedUssName);
                if (s != null) _previewRoot.styleSheets.Add(s);
            }
            foreach (var c in classes) _previewRoot.AddToClassList(c);

            _previewRoot.Add(A2uiAtomicRenderer.Build(src));

            if (_typeDrop != null) _typeDrop.value = src.Type.ToString();
        }

        // ===================== USS 规则编辑（IMGUI）=====================
        // 可被编辑的 USS 属性清单（聚焦"调外观"常用的那批，去掉几乎不会在样式编辑器里改的：
        // 变换 rotate/scale/translate、cursor、overflow、position 偏移、四边拆开的 border/margin/padding 等）。
        // 选组件 + 选规则后，可从这里挑任意属性加进规则。
        static readonly string[] PropPalette = new string[]
        {
            // —— 文字排版 ——
            "font-size", "color", "-unity-font-style", "-unity-font-weight",
            "unity-text-align", "white-space", "letter-spacing", "text-shadow",
            // —— 背景 ——
            "background-color", "background-image", "unity-background-scale-mode",
            // —— 边框（整体，不拆四边）——
            "border-color", "border-width", "border-radius",
            // —— 尺寸 ——
            "width", "height", "min-width", "max-width", "min-height", "max-height",
            // —— 布局与间距 ——
            "flex-grow", "flex-shrink", "align-self", "justify-content",
            "padding", "margin", "opacity", "display",
        };

        // 给某个属性一个合理默认值；颜色类用 rgb 让编辑器识别成颜色拾取器
        static string DefaultFor(string name)
        {
            switch (name)
            {
                case "font-size": return "16px";
                case "color":
                case "background-color":
                case "border-color":
                case "border-left-color":
                case "border-right-color":
                case "border-top-color":
                case "border-bottom-color":
                case "-unity-background-image-tint": return "rgb(255, 255, 255)";
                case "-unity-font-style": return "normal";
                case "-unity-font-weight": return "400";
                case "unity-text-align": return "center";
                case "white-space": return "normal";
                case "letter-spacing":
                case "paragraph-spacing":
                case "word-spacing": return "0px";
                case "border-width":
                case "border-left-width":
                case "border-right-width":
                case "border-top-width":
                case "border-bottom-width":
                case "border-radius":
                case "border-top-left-radius":
                case "border-top-right-radius":
                case "border-bottom-left-radius":
                case "border-bottom-right-radius":
                case "width":
                case "height":
                case "min-width":
                case "max-width":
                case "min-height":
                case "max-height":
                case "top":
                case "left":
                case "right":
                case "bottom":
                case "margin":
                case "margin-left":
                case "margin-top":
                case "margin-right":
                case "margin-bottom":
                case "padding":
                case "padding-left":
                case "padding-top":
                case "padding-right":
                case "padding-bottom": return "0px";
                case "flex-direction": return "row";
                case "flex-grow": return "0";
                case "flex-shrink": return "1";
                case "flex-basis": return "auto";
                case "align-items":
                case "align-self":
                case "align-content":
                case "justify-content": return "stretch";
                case "position": return "relative";
                case "display": return "flex";
                case "visibility": return "visible";
                case "opacity": return "1";
                case "unity-background-scale-mode":
                case "-unity-background-scale-mode": return "stretch-to-fill";
                case "overflow": return "visible";
                case "cursor": return "arrow";
                default: return "";
            }
        }

        void UssGUI()
        {
            if (_doc == null) { EditorGUILayout.HelpBox("未加载 USS。", MessageType.Warning); return; }

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            _liveSync = EditorGUILayout.ToggleLeft("实时同步", _liveSync, GUILayout.Width(80));
            if (GUILayout.Button("确定修改", GUILayout.Width(80))) ApplyAndSave();
            EditorGUILayout.EndHorizontal();

            if (_ruleNames.Length == 0) { EditorGUILayout.HelpBox("该 USS 暂无规则。", MessageType.Info); return; }
            _ruleIndex = EditorGUILayout.Popup("规则", _ruleIndex, _ruleNames);

            _ussScroll = EditorGUILayout.BeginScrollView(_ussScroll);
            var rule = _doc.Rules[_ruleIndex];
            for (int i = 0; i < rule.Properties.Count; i++)
            {
                var p = rule.Properties[i];
                EditorGUILayout.BeginHorizontal();
                p.Name = EditorGUILayout.TextField(p.Name, GUILayout.Width(150));
                if (p.IsColor && UssProperty.TryParseColor(p.Value, out var col))
                {
                    var nc = EditorGUILayout.ColorField(col);
                    if (!nc.Equals(col)) p.Value = UssProperty.ColorToUss(nc);
                }
                else
                {
                    p.Value = EditorGUILayout.TextField(p.Value);
                }
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    rule.Properties.RemoveAt(i);
                    if (_liveSync) ApplyAndSave();
                    _ussContainer.MarkDirtyRepaint();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            // 添加属性：从清单里挑，加进"当前选中的那条规则"（和顶部选 Text 组件是两件事）
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("下方是给「当前规则」加属性用的，不是选组件（组件已在顶部选过了）。", MessageType.None);
            EditorGUILayout.BeginHorizontal();
            _propPickIndex = EditorGUILayout.Popup("向该规则加属性", _propPickIndex, PropPalette);
            if (GUILayout.Button("＋ 加入", GUILayout.Width(80)))
            {
                var name = PropPalette[_propPickIndex];
                if (!rule.Properties.Exists(p => p.Name == name))
                {
                    rule.Properties.Add(new UssProperty(name, DefaultFor(name)));
                    if (_liveSync) ApplyAndSave();
                    _ussContainer.MarkDirtyRepaint();
                }
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("+ 添加自定义属性（手动填名字）"))
            {
                rule.Properties.Add(new UssProperty("", ""));
                _ussContainer.MarkDirtyRepaint();
            }

            if (EditorGUI.EndChangeCheck() && _liveSync)
                ApplyAndSave();
        }

        void ApplyAndSave()
        {
            if (_doc == null || string.IsNullOrEmpty(_selectedUssPath)) return;
            var text = _doc.Serialize();
            var full = Path.Combine(Directory.GetParent(Application.dataPath).FullName, _selectedUssPath);
            File.WriteAllText(full, text);
            // ForceUpdate：强制 Unity 同步重建内存里那份已缓存的 StyleSheet，否则预览仍用旧样式
            AssetDatabase.ImportAsset(_selectedUssPath, ImportAssetOptions.ForceUpdate);
            // 重新构建左侧预览，让刚写的样式表立刻在预览上生效
            RefreshPreview();
            _status = "已保存 " + System.DateTime.Now.ToString("HH:mm:ss");
        }

        // ===================== 自动跟随 =====================
        void OnSelectionChanged()
        {
            if (_followToggle == null || !_followToggle.value) return;
            var go = Selection.activeGameObject;
            if (go == null) return;
            var c = go.GetComponent<A2uiAtomicComponent>();
            if (c == null) return;
            _previewOverride = c;
            RefreshPreview();
            FocusRuleFor(c);
        }

        // 把右侧规则列表定位到某组件的 RuleSelector 对应规则
        void FocusRuleFor(A2uiAtomicComponent c)
        {
            if (_doc == null) return;
            var full = c.RuleSelector();           // 例如 ".a2ui-text--"
            var baseTok = full.Split(new[] { "--" }, System.StringSplitOptions.None)[0]; // ".a2ui-text"
            // 先精确匹配，再退而求其次：规则选择器里包含基础 token
            var rule = _doc.FindRule(full)
                ?? _doc.Rules.Find(r => r.Selector.Contains(baseTok));
            if (rule != null)
            {
                _ruleIndex = _doc.Rules.IndexOf(rule);
                _ussContainer?.MarkDirtyRepaint();
            }
        }
    }
}
