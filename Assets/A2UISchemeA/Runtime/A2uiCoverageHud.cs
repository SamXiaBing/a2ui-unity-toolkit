using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace A2UISchemeA
{
    /// <summary>
    /// Types / Props 覆盖面板。点 Type = 加载该类型单元 JSONL，右侧只列该 Type 的协议 props。
    /// </summary>
    public class A2uiCoverageHud
    {
        public static readonly string[] CatalogTypes =
        {
            "Text", "Image", "Icon", "Video", "AudioPlayer",
            "Row", "Column", "List", "Card", "Tabs", "Divider", "Modal",
            "Button", "CheckBox", "TextField", "DateTimeInput", "MultipleChoice", "Slider",
            "MediaMiniBar", "ClimateStep", "RestBanner"
        };

        static readonly Dictionary<string, string> TypeZh = new Dictionary<string, string>
        {
            ["Text"] = "文本",
            ["Image"] = "图片",
            ["Icon"] = "图标",
            ["Video"] = "视频",
            ["AudioPlayer"] = "音频播放器",
            ["Row"] = "横向布局",
            ["Column"] = "纵向布局",
            ["List"] = "列表",
            ["Card"] = "卡片",
            ["Tabs"] = "页签",
            ["Divider"] = "分割线",
            ["Modal"] = "弹层",
            ["Button"] = "按钮",
            ["CheckBox"] = "复选框",
            ["TextField"] = "输入框",
            ["DateTimeInput"] = "日期时间",
            ["MultipleChoice"] = "多选",
            ["Slider"] = "滑条",
            ["MediaMiniBar"] = "媒体短条",
            ["ClimateStep"] = "空调步进",
            ["RestBanner"] = "休憩横幅"
        };

        static readonly Dictionary<string, string> PropZh = new Dictionary<string, string>
        {
            ["text"] = "文案",
            ["usageHint"] = "样式提示",
            ["url"] = "地址",
            ["altText"] = "替代文本",
            ["fit"] = "填充方式",
            ["name"] = "名称",
            ["description"] = "描述",
            ["children"] = "子节点",
            ["explicitList"] = "显式列表",
            ["template"] = "模板展开",
            ["distribution"] = "主轴分布",
            ["alignment"] = "交叉轴对齐",
            ["direction"] = "方向",
            ["child"] = "子组件",
            ["tabItems"] = "页签项",
            ["title"] = "标题",
            ["axis"] = "轴向",
            ["entryPointChild"] = "入口子节点",
            ["contentChild"] = "内容子节点",
            ["primary"] = "主按钮",
            ["action"] = "动作",
            ["context"] = "动作上下文",
            ["label"] = "标签",
            ["value"] = "值",
            ["textFieldType"] = "输入类型",
            ["validationRegexp"] = "校验正则",
            ["enableDate"] = "可选日期",
            ["enableTime"] = "可选时间",
            ["selections"] = "已选值",
            ["options"] = "选项",
            ["maxAllowedSelections"] = "最多可选",
            ["variant"] = "变体",
            ["filterable"] = "可过滤",
            ["minValue"] = "最小值",
            ["maxValue"] = "最大值",
            ["tempLabel"] = "温度文案",
            ["path"] = "数据路径",
            ["literalString"] = "字面字符串",
            ["literalNumber"] = "字面数字",
            ["literalBoolean"] = "字面布尔",
            ["literalArray"] = "字面数组",
            ["weight"] = "布局权重"
        };

        /// <summary>各 Type 在协议/扩展 Catalog 中的顶层 props（不含别的 Type）。</summary>
        static readonly Dictionary<string, string[]> TypeProps = new Dictionary<string, string[]>
        {
            ["Text"] = new[] { "text", "usageHint" },
            ["Image"] = new[] { "url", "altText", "fit", "usageHint" },
            ["Icon"] = new[] { "name" },
            ["Video"] = new[] { "url" },
            ["AudioPlayer"] = new[] { "url", "description" },
            ["Row"] = new[] { "children", "distribution", "alignment", "explicitList", "template" },
            ["Column"] = new[] { "children", "distribution", "alignment", "explicitList", "template" },
            ["List"] = new[] { "children", "direction", "alignment", "explicitList", "template" },
            ["Card"] = new[] { "child" },
            ["Tabs"] = new[] { "tabItems", "title", "child" },
            ["Divider"] = new[] { "axis" },
            ["Modal"] = new[] { "entryPointChild", "contentChild" },
            ["Button"] = new[] { "child", "primary", "action", "context" },
            ["CheckBox"] = new[] { "label", "value" },
            ["TextField"] = new[] { "label", "text", "textFieldType", "validationRegexp" },
            ["DateTimeInput"] = new[] { "value", "enableDate", "enableTime" },
            ["MultipleChoice"] = new[] { "selections", "options", "maxAllowedSelections", "variant", "filterable" },
            ["Slider"] = new[] { "label", "value", "minValue", "maxValue" },
            ["MediaMiniBar"] = new[] { "title", "text", "child" },
            ["ClimateStep"] = new[] { "tempLabel", "text", "child" },
            ["RestBanner"] = new[] { "text" }
        };

        public const string UnitSampleDir = "Assets/A2UISchemeA/Samples/components/";

        readonly VisualElement _root;
        readonly VisualElement _typeList;
        readonly VisualElement _propList;
        readonly Label _summary;
        Action<string> _onTypeUnitTest;
        string _focusType;

        public A2uiCoverageHud(Action<string> onTypeUnitTest = null)
        {
            _onTypeUnitTest = onTypeUnitTest;
            _root = new VisualElement();
            _root.AddToClassList("a2ui-coverage");
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.minWidth = Length.Percent(100);
            _root.style.maxWidth = Length.Percent(100);
            _root.style.width = Length.Percent(100);
            _root.style.flexGrow = 1;

            _summary = new Label("点左侧 Type = 加载该类型单元测；右侧只显示该 Type 的 props");
            _summary.AddToClassList("a2ui-coverage__summary");
            _root.Add(_summary);

            var row = new VisualElement();
            row.AddToClassList("a2ui-coverage__columns");
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexGrow = 1;

            _typeList = new VisualElement();
            _typeList.AddToClassList("a2ui-coverage__col");
            _typeList.style.flexGrow = 1;
            _typeList.style.flexBasis = 0;
            _typeList.style.marginRight = 10;

            _propList = new VisualElement();
            _propList.AddToClassList("a2ui-coverage__col");
            _propList.style.flexGrow = 1;
            _propList.style.flexBasis = 0;

            row.Add(_typeList);
            row.Add(_propList);
            _root.Add(row);
        }

        public VisualElement Root => _root;
        public string FocusType => _focusType;

        public void SetTypeUnitTestHandler(Action<string> handler) => _onTypeUnitTest = handler;

        public void SetFocusType(string typeName) => _focusType = typeName;

        public void ClearFocus() => _focusType = null;

        public static string UnitSamplePath(string typeName) => UnitSampleDir + typeName + ".v0.8.jsonl";

        public static string FormatTypeLabel(string typeName)
        {
            if (TypeZh.TryGetValue(typeName, out var zh))
                return typeName + "  " + zh;
            return typeName;
        }

        public static string FormatPropLabel(string propName)
        {
            if (PropZh.TryGetValue(propName, out var zh))
                return propName + "  " + zh;
            return propName;
        }

        /// <summary>下拉用：英文 Type 或「Text  文本」→ Type 名。</summary>
        public static string ParseTypeFromChoice(string choice)
        {
            if (string.IsNullOrEmpty(choice)) return choice;
            var sp = choice.IndexOf("  ", StringComparison.Ordinal);
            if (sp > 0) return choice.Substring(0, sp).Trim();
            var space = choice.IndexOf(' ');
            return space > 0 ? choice.Substring(0, space).Trim() : choice.Trim();
        }

        public static List<string> CatalogTypeChoices()
        {
            return CatalogTypes.Select(FormatTypeLabel).ToList();
        }

        public void Refresh(A2uiV08SurfaceState state)
        {
            _typeList.Clear();
            _propList.Clear();

            var typesInFrame = new HashSet<string>();
            var allPropsInFrame = new HashSet<string>();
            if (state != null)
                Scan(state, typesInFrame, allPropsInFrame, focusTypeOnly: null);

            var focusPropsUsed = new HashSet<string>();
            if (state != null && !string.IsNullOrEmpty(_focusType))
                Scan(state, new HashSet<string>(), focusPropsUsed, focusTypeOnly: _focusType);

            _typeList.Add(Header("Catalog Types（点测）"));
            foreach (var t in CatalogTypes)
                _typeList.Add(TypeRow(t, typesInFrame.Contains(t), t == _focusType));

            if (!string.IsNullOrEmpty(_focusType) && TypeProps.TryGetValue(_focusType, out var schemaProps))
            {
                _propList.Add(Header("Protocol Props · " + FormatTypeLabel(_focusType)));
                foreach (var p in schemaProps)
                    _propList.Add(CheckRow(p, focusPropsUsed.Contains(p)));
                _summary.text =
                    $"单元测 {_focusType}  ·  协议 props {schemaProps.Count(focusPropsUsed.Contains)}/{schemaProps.Length} 点亮  ·  ●=本单元 JSONL 用到";
            }
            else
            {
                _propList.Add(Header("Protocol Props（选 Type 后按类型过滤）"));
                var flat = TypeProps.Values.SelectMany(x => x).Distinct().OrderBy(x => x).ToList();
                foreach (var p in flat)
                    _propList.Add(CheckRow(p, allPropsInFrame.Contains(p)));
                var typeHit = CatalogTypes.Count(typesInFrame.Contains);
                _summary.text =
                    $"点 Type 加载单元测  ·  本帧 types {typeHit}/{CatalogTypes.Length}  ·  props 点亮 {flat.Count(allPropsInFrame.Contains)}/{flat.Count}";
            }

            _summary.tooltip = "●=当前报文用到；选中 Type 后右侧只列该 Type 协议 props";
        }

        VisualElement TypeRow(string name, bool usedInFrame, bool focused)
        {
            var btn = new Button(() => _onTypeUnitTest?.Invoke(name))
            {
                text = (usedInFrame ? "● " : "○ ") + FormatTypeLabel(name)
            };
            btn.AddToClassList("a2ui-coverage__type-btn");
            btn.AddToClassList(usedInFrame ? "a2ui-coverage__ok" : "a2ui-coverage__miss");
            if (focused)
                btn.AddToClassList("a2ui-coverage__type-btn--active");
            return btn;
        }

        static Label Header(string text)
        {
            var l = new Label(text);
            l.AddToClassList("a2ui-coverage__header");
            return l;
        }

        static Label CheckRow(string name, bool ok)
        {
            var l = new Label((ok ? "● " : "○ ") + FormatPropLabel(name));
            l.AddToClassList(ok ? "a2ui-coverage__ok" : "a2ui-coverage__miss");
            return l;
        }

        static void Scan(
            A2uiV08SurfaceState state,
            HashSet<string> types,
            HashSet<string> props,
            string focusTypeOnly)
        {
            foreach (var def in state.Components.Values)
            {
                if (def["weight"] != null &&
                    (focusTypeOnly == null || HasType(def, focusTypeOnly)))
                    props.Add("weight");

                if (!A2uiV08Processor.TryGetComponentType(def, out var type, out var p)) continue;
                types.Add(type);
                if (focusTypeOnly != null && type != focusTypeOnly) continue;
                ScanToken(p, props);
            }
        }

        static bool HasType(JObject def, string typeName)
        {
            return A2uiV08Processor.TryGetComponentType(def, out var type, out _) && type == typeName;
        }

        static void ScanToken(JToken token, HashSet<string> props)
        {
            if (token == null) return;
            if (token is JObject o)
            {
                foreach (var prop in o.Properties())
                {
                    props.Add(prop.Name);
                    ScanToken(prop.Value, props);
                }
            }
            else if (token is JArray a)
            {
                foreach (var item in a)
                    ScanToken(item, props);
            }
        }
    }
}
