using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace A2UISchemeA
{
    /// <summary>
    /// A2UI v0.8 Standard Catalog → UI Toolkit。
    /// 布局属性来自协议；观感交给 USS class（Raw / Crafted / Token A·B）。
    /// </summary>
    public class A2uiV08CatalogMapper
    {
        readonly Action<string, JObject> _onUserAction;
        A2uiV08SurfaceState _state;
        readonly Dictionary<string, VisualElement> _built = new Dictionary<string, VisualElement>();
        readonly Stack<JToken> _scopeStack = new Stack<JToken>();
        string _scopeKey;

        public A2uiV08CatalogMapper(Action<string, JObject> onUserAction)
        {
            _onUserAction = onUserAction;
        }

        public VisualElement Build(A2uiV08SurfaceState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _built.Clear();
            _scopeStack.Clear();
            _scopeKey = null;
            if (string.IsNullOrEmpty(state.RootId) || !state.Components.ContainsKey(state.RootId))
                throw new InvalidOperationException($"root '{state.RootId}' not found");
            var root = BuildNode(state.RootId);
            root.AddToClassList("a2ui-surface");
            // ApplyEntranceAnimation moved to caller (must be called after panel attach)
            return root;
        }

        /// <summary>
        /// 入场动效：容器整体淡入；列表项按序号交错。
        /// 只挂 class，下一帧再翻到 ready 态触发重绘（JSONL 保持干净，不写动画）。
        /// 必须在元素已挂载到 panel 后调用（Tuanjie 引擎 bug：对 detached 元素
        /// 挂带 USS 规则的 class 会导致 StylePropertyReader 越界）。
        /// </summary>
        public static void ApplyEntranceAnimation(VisualElement root)
        {
            root.AddToClassList("a2ui-anim--enter");

            var items = new System.Collections.Generic.List<VisualElement>();
            CollectListItems(root, items);
            for (var i = 0; i < items.Count; i++)
            {
                var it = items[i];
                it.AddToClassList("a2ui-anim--stagger");
                it.AddToClassList("a2ui-anim--d" + System.Math.Min(i + 1, 12));
            }

            root.schedule.Execute(() =>
            {
                // Guard against detached elements (e.g. parent cleared between
                // Build and this deferred callback) causing UI Toolkit internals to throw.
                if (root.panel == null) return;
                try
                {
                    root.AddToClassList("a2ui-anim--enter-ready");
                    foreach (var it in items)
                    {
                        if (it.panel != null)
                            it.AddToClassList("a2ui-anim--stagger-ready");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[A2UISchemeA] entrance anim skipped: {e.GetType().Name}");
                }
            }).StartingIn(40);
        }

        private static void CollectListItems(VisualElement ve, System.Collections.Generic.List<VisualElement> outList)
        {
            if (ve.ClassListContains("a2ui-list__item")) outList.Add(ve);
            for (var i = 0; i < ve.childCount; i++)
                CollectListItems(ve[i], outList);
        }

        /// <summary>
        /// 延迟执行文本测量兜底（见 <see cref="FixTextMeasure"/>）。
        /// StartingIn 以毫秒计（不是帧）：首跑在卡片首次布局完成后（2ms 实际落在
        /// 下一帧的 scheduler tick），之后按 2.5 倍退避补跑，直到连续两跑零补丁
        /// （且至少跑满 3 轮）或跑满 8 轮为止——高帧率下 500ms 可能已隔 90 帧，
        /// 固定时刻表会漏，退避重跑 + 收敛检测对任意帧率都稳。
        /// 幂等：只抬高高度不足的元素，重复执行无副作用。
        /// </summary>
        public static void ScheduleTextMeasureFix(VisualElement root)
        {
            if (root == null || root.panel == null) return;
            var zeroStreak = 0;
            var pass = 0;
            long delay = 2;

            void Pass()
            {
                if (root.panel == null) return;
                pass++;
                var patched = FixTextMeasure(root, out var measurable);
                if (measurable)
                {
                    if (patched == 0) zeroStreak++;
                    else zeroStreak = 0;
                }
                if ((pass >= 3 && zeroStreak >= 2) || pass >= 8) return;
                delay = System.Math.Min((long)(delay * 2.5) + 10, 1500);
                root.schedule.Execute(Pass).StartingIn(delay);
            }

            root.schedule.Execute(Pass).StartingIn(delay);
        }

        /// <summary>
        /// 团结引擎 UITK 布局缺陷兜底：卡片内嵌套容器里的文本节点首次布局时会被
        /// 测出过小的内容高度（实测 19px 行高被测成 0～8px），且此后任何样式切换、
        /// 文本改写、重新挂载都不会触发重测（Yoga 测量缓存一次性固化），表现为
        /// 行间距拥挤、行与行相互重叠。旧 absolute+left/top 拖拽每次按下都会重建
        /// 布局而掩盖本 bug；改 translate 拖拽后问题固化显形。
        /// 布局外 MeasureTextSize 返回的才是正确值，所以这里逐个 Label 用
        /// 「实测需要高度 + 上下 padding」把 inline minHeight 顶上去，只救高度
        /// 不足的元素，达标的不动。副作用：之后文本变长需重新调用一次。
        /// 返回补丁数；measurable = 树里存在已参与布局（宽度有效）的 Label，
        /// false 表示布局未就绪（本轮结果不参与收敛判断）。
        /// </summary>
        public static int FixTextMeasure(VisualElement root, out bool measurable)
        {
            measurable = false;
            if (root == null || root.panel == null) return 0;
            var texts = new System.Collections.Generic.List<TextElement>();
            root.Query<TextElement>().ToList(texts);
            var patched = 0;
            foreach (var te in texts)
            {
                if (te.panel == null || te.resolvedStyle.display != DisplayStyle.Flex) continue;
                var width = te.layout.width;
                if (!(width >= 1f) || string.IsNullOrEmpty(te.text)) continue;
                // 多行 TextField 内部是定高裁剪区，盒高压矮是有意设计，不兜底
                if (InMultilineField(te)) continue;
                measurable = true;

                // 文本在 content box 内换行：测宽必须扣掉水平 padding/border，
                // 否则带内边距的文本测出的 need 偏小（漏报）
                var contentWidth = width
                    - te.resolvedStyle.paddingLeft - te.resolvedStyle.paddingRight
                    - te.resolvedStyle.borderLeftWidth - te.resolvedStyle.borderRightWidth;
                if (!(contentWidth >= 1f)) continue;

                var v = te.MeasureTextSize(te.text, contentWidth,
                    VisualElement.MeasureMode.AtMost, 9999f, VisualElement.MeasureMode.Undefined);
                var need = v.y + te.resolvedStyle.paddingTop + te.resolvedStyle.paddingBottom;
                if (float.IsNaN(need) || float.IsInfinity(need)) continue;
                if (te.layout.height < need - 0.5f)
                {
                    te.style.minHeight = need;
                    patched++;
                }
            }
            if (patched > 0)
                Debug.Log($"[A2UISchemeA] FixTextMeasure: patched {patched}/{texts.Count} text elements");
            return patched;
        }

        /// <summary>是否在多行 TextField（a2ui-textfield--long）内部——定高裁剪区不兜底。</summary>
        static bool InMultilineField(VisualElement ve)
        {
            var p = ve.parent;
            while (p != null)
            {
                if (p.ClassListContains("a2ui-textfield--long")) return true;
                p = p.parent;
            }
            return false;
        }

        string CacheKey(string id) =>
            string.IsNullOrEmpty(_scopeKey) ? id : id + "@" + _scopeKey;

        /// <summary>
        /// 递归深度上限（对齐 A2UI Compose 参考渲染器 MAX_RENDER_DEPTH=50）：
        /// 恶意/深度嵌套 JSONL 不会栈溢出，超限渲染占位符。
        /// </summary>
        const int MaxRenderDepth = 50;
        int _depth;

        VisualElement BuildNode(string id)
        {
            var key = CacheKey(id);
            if (_built.TryGetValue(key, out var cached))
                return cached;

            if (_depth >= MaxRenderDepth)
                return Placeholder($"depth>{MaxRenderDepth}");

            if (!_state.Components.TryGetValue(id, out var def))
                return Placeholder($"missing:{id}");

            if (!A2uiV08Processor.TryGetComponentType(def, out var type, out var props))
                return Placeholder($"bad:{id}");

            _depth++;
            VisualElement ve = type switch
            {
                "Text" => MapText(props),
                "Image" => MapImage(props),
                "Icon" => MapIcon(props),
                "Video" => MapVideo(props),
                "AudioPlayer" => MapAudioPlayer(props),
                "Row" => MapFlex(props, FlexDirection.Row, "a2ui-row"),
                "Column" => MapFlex(props, FlexDirection.Column, "a2ui-column"),
                "List" => MapList(props),
                "Card" => MapCard(props),
                "Tabs" => MapTabs(props),
                "Divider" => MapDivider(props),
                "Modal" => MapModal(props),
                "Button" => MapButton(id, props),
                "CheckBox" => MapCheckBox(props),
                "TextField" => MapTextField(props),
                "DateTimeInput" => MapDateTimeInput(props),
                "MultipleChoice" => MapMultipleChoice(props),
                "Slider" => MapSlider(props),
                "MediaMiniBar" => MapMediaMiniBar(id, props),
                "ClimateStep" => MapClimateStep(props),
                "RestBanner" => MapRestBanner(props),
                _ => A2uiDegrade.UnknownTypeFallback(type, id)
            };

            ve.name = key;
            ve.AddToClassList("a2ui-node");
            ve.AddToClassList("a2ui-type--" + type.ToLowerInvariant());
            ApplyWeight(ve, def);
            _built[key] = ve;
            _depth--;
            return ve;
        }

        void ApplyWeight(VisualElement ve, JObject def)
        {
            if (def["weight"] == null) return;
            var w = def["weight"].Value<float>();
            ve.style.flexGrow = w;
            ve.style.flexShrink = 1;
            ve.AddToClassList("a2ui-weight");
        }

        VisualElement MapText(JObject props)
        {
            var label = new Label(ResolveString(props["text"]) ?? "");
            label.AddToClassList("a2ui-text");
            var hint = props["usageHint"]?.Value<string>() ?? "body";
            label.AddToClassList("a2ui-text--" + hint);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        VisualElement MapImage(JObject props)
        {
            var box = new VisualElement();
            box.AddToClassList("a2ui-image");
            var hint = props["usageHint"]?.Value<string>() ?? "mediumFeature";
            box.AddToClassList("a2ui-image--" + hint);
            var fit = props["fit"]?.Value<string>() ?? "cover";
            box.AddToClassList("a2ui-image-fit--" + fit);
            ApplyImageFit(box, fit);

            // 关键：背景图不参与布局尺寸，若隐藏 caption 后盒子无可见子节点会塌成 0 高度。
            // 默认可见高度按 usageHint 分级，数值对齐 A2UI Compose 参考渲染器
            // （ComponentRegistry.kt: icon 24 / avatar 48 / small 120 / medium 200 /
            //   large 300 / header 250）。可用 minHeight 属性覆盖。
            float defaultMin = hint switch
            {
                "icon" => 24f,
                "avatar" => 48f,
                "smallFeature" => 120f,
                "mediumFeature" => 200f,
                "largeFeature" => 300f,
                "header" => 250f,
                _ => 200f
            };
            box.style.minHeight = props["minHeight"]?.Value<float>() ?? defaultMin;

            var url = ResolveString(props["url"]) ?? "";
            var alt = ResolveString(props["altText"]) ?? "图片";
            var status = new Label(alt);
            status.AddToClassList("a2ui-image__caption");
            status.style.whiteSpace = WhiteSpace.Normal;
            box.Add(status);

            if (string.IsNullOrEmpty(url))
            {
                // 无 url：仅显示 alt 文案
            }
            else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                status.text = "加载中…";
                LoadRemoteTexture(box, status, url, alt);
            }
            else if (url.StartsWith("resources://", StringComparison.OrdinalIgnoreCase))
            {
                // 本地切图（Figma 导出）：放在任意 Resources/ 目录下，按相对路径加载
                var resName = url.Substring("resources://".Length);
                var tex = Resources.Load<Texture2D>(resName);
                if (tex != null)
                {
                    box.style.backgroundImage = new StyleBackground(tex);
                    status.style.display = DisplayStyle.None;
                }
                else
                {
                    status.text = "未找到切片: " + resName;
                }
            }

            return box;
        }

        /// <summary>
        /// Catalog fit ≈ CSS object-fit。UITK 用 unityBackgroundScaleMode 近似；scale-down 无原生等价，按 contain 处理。
        /// </summary>
        static void ApplyImageFit(VisualElement box, string fit)
        {
            box.style.unityBackgroundScaleMode = fit switch
            {
                "fill" => ScaleMode.StretchToFill,
                "none" => ScaleMode.ScaleAndCrop,
                "contain" => ScaleMode.ScaleToFit,
                "scale-down" => ScaleMode.ScaleToFit,
                _ => ScaleMode.ScaleAndCrop // cover
            };
        }

        static void LoadRemoteTexture(VisualElement box, Label status, string url, string alt)
        {
            var req = UnityWebRequestTexture.GetTexture(url);
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        var tex = DownloadHandlerTexture.GetContent(req);
                        if (tex != null)
                        {
                            box.style.backgroundImage = new StyleBackground(tex);
                            status.style.display = DisplayStyle.None;
                        }
                        else
                        {
                            status.text = alt;
                        }
                    }
                    else
                    {
                        status.text = alt + "（图片加载失败）";
                    }
                }
                finally
                {
                    req.Dispose();
                }
            };
        }

        VisualElement MapIcon(JObject props)
        {
            var rawName = ResolveString(props["name"]) ?? "info";
            var name = A2uiIconCatalog.ResolveName(rawName);
            var box = new VisualElement();
            box.AddToClassList("a2ui-icon");
            box.AddToClassList("a2ui-icon--" + SanitizeClass(name));
            box.style.width = 36;
            box.style.height = 36;
            box.style.minWidth = 36;
            box.style.minHeight = 36;
            box.style.marginRight = 6;
            box.tooltip = rawName != name ? rawName + " → " + name : name;

            // 图标渲染：优先用内置程序化纹理图标（A2uiIconCatalog），无对应名则占位符。
            // 说明：本工程引擎（Tuanjie 分支）未提供 UnityEngine.U2D.VectorImage 所需的
            // Sprite 顶点属性 API，故未引入 com.unity.vectorgraphics；
            // 若需 sinanata 设计系统矢量图标，可后续将 SVG 预渲染为 PNG 后改走 Texture2D 加载。
            if (A2uiIconCatalog.TryGetTexture(name, out var tex) && tex != null)
            {
                box.style.backgroundImage = new StyleBackground(tex);
                box.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            }
            else
            {
                box.Add(new Label("?").WithClass("a2ui-icon__fallback"));
            }

            if (!string.Equals(rawName, name, StringComparison.Ordinal))
                box.AddToClassList("a2ui-icon--fallback");

            return box;
        }

        static string SanitizeClass(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
            return sb.ToString();
        }

        VisualElement MapVideo(JObject props)
        {
            var box = new VisualElement();
            box.AddToClassList("a2ui-video");
            box.style.minHeight = 120;
            box.style.width = Length.Percent(100);
            var title = new Label("▶ Video（占位，未接真播放器）");
            title.AddToClassList("a2ui-video__title");
            var url = new Label(Truncate(ResolveString(props["url"]), 72));
            url.AddToClassList("a2ui-video__url");
            box.Add(title);
            box.Add(url);
            return box;
        }

        VisualElement MapAudioPlayer(JObject props)
        {
            var col = new VisualElement();
            col.AddToClassList("a2ui-audio");
            col.style.flexDirection = FlexDirection.Column;
            col.style.minHeight = 96;
            col.style.width = Length.Percent(100);
            var desc = ResolveString(props["description"]) ?? "Audio";
            var url = ResolveString(props["url"]) ?? "";
            var title = new Label("♪ AudioPlayer · " + desc);
            title.AddToClassList("a2ui-audio__title");
            var urlLabel = new Label(Truncate(url, 72));
            urlLabel.AddToClassList("a2ui-audio__url");
            col.Add(title);
            col.Add(urlLabel);
            return col;
        }

        VisualElement MapFlex(JObject props, FlexDirection dir, string className)
        {
            var ve = new VisualElement();
            ve.AddToClassList(className);
            ve.style.flexDirection = dir;
            // 只有 Row 允许换行；Column 换行会把溢出子元素堆到右侧（假并列布局）
            ve.style.flexWrap = dir == FlexDirection.Row ? Wrap.Wrap : Wrap.NoWrap;
            ApplyDistribution(ve, props["distribution"]?.Value<string>());
            ApplyAlignment(ve, props["alignment"]?.Value<string>());
            var childList = new System.Collections.Generic.List<VisualElement>();
            foreach (var child in ResolveChildRefs(props["children"]))
                childList.Add(BuildChildRef(child));
            if (childList.Count > 0)
                childList[^1].AddToClassList("a2ui-last-child");
            foreach (var c in childList)
                ve.Add(c);
            return ve;
        }

        VisualElement MapList(JObject props)
        {
            var scroll = new ScrollView();
            scroll.AddToClassList("a2ui-list");
            var dir = props["direction"]?.Value<string>() == "horizontal"
                ? FlexDirection.Row
                : FlexDirection.Column;
            scroll.contentContainer.style.flexDirection = dir;
            ApplyAlignment(scroll.contentContainer, props["alignment"]?.Value<string>());
            var childList = new System.Collections.Generic.List<VisualElement>();
            foreach (var child in ResolveChildRefs(props["children"]))
                childList.Add(BuildChildRef(child));
            if (childList.Count > 0)
                childList[^1].AddToClassList("a2ui-last-child");
            foreach (var c in childList)
                scroll.Add(c);
            return scroll;
        }

        VisualElement MapCard(JObject props)
        {
            var card = new VisualElement();
            card.AddToClassList("a2ui-card");
            card.style.flexDirection = FlexDirection.Column;
            var childId = props["child"]?.Value<string>();
            if (!string.IsNullOrEmpty(childId))
                card.Add(BuildNode(childId));
            return card;
        }

        VisualElement MapTabs(JObject props)
        {
            var root = new VisualElement();
            root.AddToClassList("a2ui-tabs");
            root.style.flexDirection = FlexDirection.Column;
            var header = new VisualElement();
            header.AddToClassList("a2ui-tabs__header");
            header.style.flexDirection = FlexDirection.Row;
            // tab 多时换行，不许撑破卡片（Tuanjie UITK 的 flex-shrink 默认 0）
            header.style.flexWrap = Wrap.Wrap;
            var body = new VisualElement();
            body.AddToClassList("a2ui-tabs__body");
            root.Add(header);
            root.Add(body);

            var items = props["tabItems"] as JArray;
            if (items == null) return root;

            var tabButtons = new System.Collections.Generic.List<Button>();
            VisualElement current = null;
            foreach (var token in items)
            {
                if (token is not JObject item) continue;
                var title = ResolveString(item["title"]) ?? "Tab";
                var childId = item["child"]?.Value<string>();
                var btn = new Button(() =>
                {
                    body.Clear();
                    if (!string.IsNullOrEmpty(childId))
                        body.Add(BuildNode(childId));
                    // 换页子树此刻才首次入布局树，会踩 Yoga 测量固化 bug（主兜底
                    // 的 8 轮早已收敛结束），展示后单独补测
                    ScheduleTextMeasureFix(body);
                }) { text = title };
                var thisBtn = btn;
                btn.clicked += () =>
                {
                    foreach (var b in tabButtons) b.RemoveFromClassList("a2ui-tabs__tab--active");
                    thisBtn.AddToClassList("a2ui-tabs__tab--active");
                };
                btn.AddToClassList("a2ui-tabs__tab");
                // inline 覆盖 DS 样式的 flex-grow:1（均分 tab 会把窄卡片撑破），按内容收拢
                btn.style.flexGrow = 0;
                btn.style.flexShrink = 0;
                btn.style.whiteSpace = UnityEngine.UIElements.WhiteSpace.NoWrap;
                header.Add(btn);
                tabButtons.Add(btn);
                if (current == null && !string.IsNullOrEmpty(childId))
                {
                    btn.AddToClassList("a2ui-tabs__tab--active");
                    current = BuildNode(childId);
                    body.Add(current);
                }
            }

            return root;
        }

        VisualElement MapDivider(JObject props)
        {
            var axis = props["axis"]?.Value<string>() ?? "horizontal";
            var line = new VisualElement();
            line.AddToClassList("a2ui-divider");
            line.AddToClassList(axis == "vertical" ? "a2ui-divider--vertical" : "a2ui-divider--horizontal");
            return line;
        }

        VisualElement MapModal(JObject props)
        {
            // UITK 无系统级弹窗：入口常显，内容默认隐藏，点击入口切换。非永远方案，量产需 Overlay/焦点陷阱。
            var row = new VisualElement();
            row.AddToClassList("a2ui-modal");
            row.style.flexDirection = FlexDirection.Column;
            var entry = props["entryPointChild"]?.Value<string>();
            var content = props["contentChild"]?.Value<string>();

            VisualElement panel = null;
            if (!string.IsNullOrEmpty(content))
            {
                panel = new VisualElement();
                panel.AddToClassList("a2ui-modal__content");
                panel.style.display = DisplayStyle.None;
                panel.Add(BuildNode(content));
            }

            if (!string.IsNullOrEmpty(entry))
            {
                var entryVe = BuildNode(entry);
                entryVe.RegisterCallback<ClickEvent>(_ =>
                {
                    if (panel == null) return;
                    var opening = panel.style.display == DisplayStyle.None;
                    panel.style.display = opening
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                    // 隐藏期元素被主兜底跳过（display!=Flex），首次展开才真正参与
                    // 布局——展开时单独补测，否则踩 Yoga 测量固化 bug
                    if (opening)
                        ScheduleTextMeasureFix(panel);
                });
                row.Add(entryVe);
            }

            if (panel != null)
                row.Add(panel);

            return row;
        }

        VisualElement MapButton(string id, JObject props)
        {
            var childId = props["child"]?.Value<string>();
            var primary = props["primary"]?.Value<bool>() ?? false;
            var action = props["action"] as JObject;
            var actionName = action?["name"]?.Value<string>() ?? "";

            var btn = new Button(() =>
            {
                var ctx = BuildActionContext(action);
                _onUserAction?.Invoke(actionName, ctx);
            });
            btn.AddToClassList("a2ui-btn");
            if (primary) btn.AddToClassList("a2ui-btn--primary");
            else btn.AddToClassList("a2ui-btn--secondary");
            btn.style.flexShrink = 0;
            btn.style.flexGrow = 0;
            btn.style.whiteSpace = WhiteSpace.NoWrap;

            // 按钮文字只从 child=Text 子节点取，text 字段 / variant 字段都不认。
            // child 指向非 Text 或缺失时回退到 action 名（事故 5 契约，样例库 53 处
            // 按钮均为 child=Text，无 Icon/容器子按钮用法）。
            if (!string.IsNullOrEmpty(childId) && _state.Components.TryGetValue(childId, out var childDef) &&
                A2uiV08Processor.TryGetComponentType(childDef, out var childType, out var childProps) &&
                childType == "Text")
            {
                btn.text = ResolveString(childProps["text"]) ?? actionName;
            }
            else
            {
                btn.text = actionName;
            }

            return btn;
        }

        JObject BuildActionContext(JObject action)
        {
            var result = new JObject();
            if (action?["context"] is not JArray ctx) return result;
            foreach (var token in ctx)
            {
                if (token is not JObject item) continue;
                var key = item["key"]?.Value<string>();
                if (string.IsNullOrEmpty(key)) continue;
                result[key] = ResolveBound(item["value"]);
            }

            return result;
        }

        VisualElement MapCheckBox(JObject props)
        {
            var wrap = new VisualElement();
            wrap.AddToClassList("a2ui-checkbox-wrap");
            wrap.style.width = Length.Percent(100);

            // 结构化拆分：Toggle 只做勾选盒（36×36），文字用独立 a2ui-text 标签。
            // 原实现把文字塞进 Toggle 内部 label，而 .a2ui-checkbox 是 36×36 定尺寸盒
            // ——20px 字号的标签被钳成 24px 高，触发全矩阵 VCRAMP。
            var toggle = new Toggle();
            toggle.AddToClassList("a2ui-checkbox");
            var v = ResolveBound(props["value"]);
            toggle.value = v != null && v.Type == JTokenType.Boolean && v.Value<bool>();
            toggle.RegisterValueChangedCallback(evt =>
                _onUserAction?.Invoke("checkbox_toggle", new JObject { ["id"] = props["id"], ["value"] = evt.newValue }));
            wrap.Add(toggle);

            var labelText = ResolveString(props["label"]) ?? "";
            if (!string.IsNullOrEmpty(labelText))
            {
                var label = new Label(labelText);
                label.AddToClassList("a2ui-text");
                label.AddToClassList("a2ui-checkbox__label");
                wrap.Add(label);
            }

            return wrap;
        }

        VisualElement MapTextField(JObject props)
        {
            var label = ResolveString(props["label"]) ?? "";
            var text = ResolveString(props["text"]) ?? "";
            var type = props["textFieldType"]?.Value<string>() ?? "shortText";
            var regexp = props["validationRegexp"]?.Value<string>();

            VisualElement fieldVe;
            if (type == "number")
            {
                float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var num);
                var nf = new FloatField { label = label, value = num };
                nf.AddToClassList("a2ui-textfield--number");
                fieldVe = nf;
            }
            else if (type == "date")
            {
                // UITK 无原生 DatePicker：用 TextField 显示 ISO 日期，与 DateTimeInput 同级凑合。
                var df = new TextField { label = label + " (date)", value = text };
                df.AddToClassList("a2ui-textfield--date");
                fieldVe = df;
            }
            else if (type == "longText")
            {
                var field = new TextField { label = label, value = text, multiline = true };
                field.AddToClassList("a2ui-textfield--long");
                fieldVe = field;
            }
            else if (type == "obscured")
            {
                var field = new TextField { label = label, value = text, isPasswordField = true };
                field.AddToClassList("a2ui-textfield--obscured");
                fieldVe = field;
            }
            else
            {
                fieldVe = new TextField { label = label, value = text };
            }

            fieldVe.AddToClassList("a2ui-textfield");
            fieldVe.AddToClassList("a2ui-textfield--" + type);

            if (!string.IsNullOrEmpty(regexp) && fieldVe is TextField tf)
            {
                var hint = new Label("");
                hint.AddToClassList("a2ui-textfield__validation");
                tf.RegisterValueChangedCallback(evt =>
                {
                    try
                    {
                        var ok = System.Text.RegularExpressions.Regex.IsMatch(evt.newValue ?? "", regexp);
                        hint.text = ok ? "" : "格式不符合校验";
                        if (ok) tf.RemoveFromClassList("a2ui-textfield--invalid");
                        else tf.AddToClassList("a2ui-textfield--invalid");
                    }
                    catch
                    {
                        hint.text = "校验表达式无效";
                    }
                });
                var wrap = new VisualElement();
                wrap.AddToClassList("a2ui-textfield-wrap");
                wrap.style.flexDirection = FlexDirection.Column;
                wrap.Add(fieldVe);
                wrap.Add(hint);
                return wrap;
            }

            return fieldVe;
        }

        VisualElement MapDateTimeInput(JObject props)
        {
            var value = ResolveString(props["value"]) ?? "";
            var enableDate = props["enableDate"]?.Value<bool>() ?? true;
            var enableTime = props["enableTime"]?.Value<bool>() ?? false;
            var label = $"DateTime{(enableDate ? "+date" : "")}{(enableTime ? "+time" : "")}";
            var field = new TextField { label = label, value = value };
            field.AddToClassList("a2ui-datetime");
            return field;
        }

        VisualElement MapMultipleChoice(JObject props)
        {
            var col = new VisualElement { style = { flexDirection = FlexDirection.Column } };
            col.AddToClassList("a2ui-choice");
            var variant = props["variant"]?.Value<string>() ?? "checkbox";
            col.AddToClassList("a2ui-choice--" + variant);
            var maxSel = props["maxAllowedSelections"]?.Value<int>() ?? int.MaxValue;
            var filterable = props["filterable"]?.Value<bool>() ?? false;

            var selections = ResolveBound(props["selections"]);
            var selected = new HashSet<string>();
            if (selections is JArray sa)
            {
                foreach (var t in sa)
                    selected.Add(t.ToString());
            }

            var options = props["options"] as JArray;
            if (options == null) return col;

            var optionMount = new VisualElement();
            optionMount.AddToClassList("a2ui-choice__options");
            if (variant == "chips")
                optionMount.style.flexDirection = FlexDirection.Row;
            else
                optionMount.style.flexDirection = FlexDirection.Column;
            optionMount.style.flexWrap = Wrap.Wrap;

            void RebuildOptions(string filter)
            {
                optionMount.Clear();
                foreach (var token in options)
                {
                    if (token is not JObject opt) continue;
                    var val = opt["value"]?.Value<string>() ?? "";
                    var lab = ResolveString(opt["label"]) ?? val;
                    if (!string.IsNullOrEmpty(filter) &&
                        lab.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        val.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    var toggle = new Toggle(lab) { value = selected.Contains(val) };
                    toggle.AddToClassList("a2ui-choice__option");
                    if (variant == "chips")
                        toggle.AddToClassList("a2ui-choice__chip");
                    toggle.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue)
                        {
                            if (selected.Count >= maxSel && !selected.Contains(val))
                            {
                                toggle.SetValueWithoutNotify(false);
                                return;
                            }

                            selected.Add(val);
                        }
                        else
                        {
                            selected.Remove(val);
                        }
                    });
                    optionMount.Add(toggle);
                }
                // 筛选重建的开关子树在面板已挂载后才入树（首建时 panel==null 自动跳过），
                // 同样需要测量兜底
                ScheduleTextMeasureFix(optionMount);
            }

            if (filterable)
            {
                var filter = new TextField { label = "筛选" };
                filter.AddToClassList("a2ui-choice__filter");
                filter.RegisterValueChangedCallback(evt => RebuildOptions(evt.newValue));
                col.Add(filter);
            }

            RebuildOptions("");
            col.Add(optionMount);
            if (maxSel < int.MaxValue)
            {
                var lim = new Label($"最多选 {maxSel} 项");
                lim.AddToClassList("a2ui-choice__limit");
                col.Add(lim);
            }

            return col;
        }

        VisualElement MapSlider(JObject props)
        {
            var min = props["minValue"]?.Value<float>() ?? 0f;
            var max = props["maxValue"]?.Value<float>() ?? 1f;
            var raw = ResolveBound(props["value"]);
            float val = min;
            if (raw != null && (raw.Type == JTokenType.Float || raw.Type == JTokenType.Integer))
                val = raw.Value<float>();
            else if (raw != null && float.TryParse(raw.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                val = parsed;

            var slider = new Slider(ResolveString(props["label"]) ?? "", min, max);
            slider.AddToClassList("a2ui-slider");
            slider.value = Mathf.Clamp(val, min, max);
            slider.showInputField = true;
            return slider;
        }

        /// <summary>座舱 Catalog：紧凑媒体条（封面 + 标题 + 状态 + 内联播放按钮）。每条按组件 id 独立存播放状态，互不串台。</summary>
        VisualElement MapMediaMiniBar(string id, JObject props)
        {
            var row = new VisualElement();
            row.AddToClassList("a2ui-cabin");
            row.AddToClassList("a2ui-cabin--media");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            // 封面占位（♪ 唱片图标）
            var cover = new Label("♪");
            cover.AddToClassList("a2ui-cabin__cover");
            row.Add(cover);

            var col = new VisualElement();
            col.AddToClassList("a2ui-cabin__meta");
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.style.flexDirection = FlexDirection.Column;

            // 把字面标题按 id 种进 /media/<id>，保证首帧有数据可绑定，且各条互不串台
            var literalTitle = ResolveString(props["title"]) ?? ResolveString(props["text"]) ?? "未命名曲目";
            EnsureMediaSeed(id, literalTitle);
            var playing = MediaBoolFor(id, "playing", false);

            var titleEl = new Label(MediaStringFor(id, "title", literalTitle));
            titleEl.AddToClassList("a2ui-text");
            titleEl.AddToClassList("a2ui-text--h4");
            titleEl.style.whiteSpace = WhiteSpace.Normal;
            col.Add(titleEl);

            var status = new Label(playing ? "正在播放 ▶" : "已暂停");
            status.AddToClassList("a2ui-cabin__status");
            status.EnableInClassList("a2ui-cabin__status--on", playing);
            col.Add(status);

            row.Add(col);

            // 内联播放 / 暂停按钮：点击 → toggle_play（带 id）→ dataModelUpdate → 重渲染。
            // 带 id 让后端只切这一条的状态，别的条不变（修复「点一个全变暂停」）。
            var playBtn = new Button(() => _onUserAction?.Invoke("toggle_play", new JObject { ["id"] = id }));
            playBtn.AddToClassList("a2ui-cabin__play");
            playBtn.text = playing ? "⏸ 暂停" : "▶ 播放";
            row.Add(playBtn);

            return row;
        }

        void EnsureMediaSeed(string id, string title)
        {
            if (_state.DataModel["media"] is not JObject media)
            {
                media = new JObject();
                _state.DataModel["media"] = media;
            }
            if (media[id] is not JObject entry)
            {
                entry = new JObject
                {
                    ["title"] = title,
                    ["playLabel"] = "播放",
                    ["playing"] = false
                };
                media[id] = entry;
            }
        }

        string MediaStringFor(string id, string key, string fallback)
        {
            var v = A2uiV08Processor.GetByPath(_state.DataModel, "/media/" + id + "/" + key);
            return v?.Type == JTokenType.String ? v.Value<string>() : fallback;
        }

        bool MediaBoolFor(string id, string key, bool fallback)
        {
            var v = A2uiV08Processor.GetByPath(_state.DataModel, "/media/" + id + "/" + key);
            return v?.Type == JTokenType.Boolean ? v.Value<bool>() : fallback;
        }

        VisualElement MapClimateStep(JObject props)
        {
            var col = new VisualElement();
            col.AddToClassList("a2ui-cabin");
            col.AddToClassList("a2ui-cabin--climate");
            col.style.flexDirection = FlexDirection.Column;
            var temp = ResolveString(props["tempLabel"]) ?? ResolveString(props["text"]) ?? "--°C";
            col.Add(new Label(temp).WithClass("a2ui-text").WithClass("a2ui-text--h1"));
            if (props["child"] != null)
                col.Add(BuildNode(props["child"].Value<string>()));
            return col;
        }

        VisualElement MapRestBanner(JObject props)
        {
            var col = new VisualElement();
            col.AddToClassList("a2ui-cabin");
            col.AddToClassList("a2ui-cabin--rest");
            col.style.flexDirection = FlexDirection.Column;
            col.style.alignItems = Align.Center;
            col.style.minHeight = 140;
            col.style.width = Length.Percent(100);
            var tag = new Label("RestBanner");
            tag.AddToClassList("a2ui-cabin__tag");
            var msg = ResolveString(props["text"]) ?? "休憩模式";
            col.Add(tag);
            col.Add(new Label(msg).WithClass("a2ui-text").WithClass("a2ui-text--h1"));
            if (props["child"] != null)
                col.Add(BuildNode(props["child"].Value<string>()));
            return col;
        }

        struct ChildRef
        {
            public string ComponentId;
            public string ScopeKey;
            public JToken ScopeData;
        }

        VisualElement BuildChildRef(ChildRef child)
        {
            if (child.ScopeData == null)
                return BuildNode(child.ComponentId);

            var prevKey = _scopeKey;
            _scopeKey = child.ScopeKey;
            _scopeStack.Push(child.ScopeData);
            try
            {
                return BuildNode(child.ComponentId);
            }
            finally
            {
                _scopeStack.Pop();
                _scopeKey = prevKey;
            }
        }

        IEnumerable<ChildRef> ResolveChildRefs(JToken childrenToken)
        {
            if (childrenToken is not JObject children) yield break;
            if (children["explicitList"] is JArray list)
            {
                foreach (var t in list)
                {
                    var id = t.Value<string>();
                    if (!string.IsNullOrEmpty(id))
                        yield return new ChildRef { ComponentId = id };
                }

                yield break;
            }

            if (children["template"] is not JObject template) yield break;
            var componentId = template["componentId"]?.Value<string>();
            var binding = template["dataBinding"]?.Value<string>();
            if (string.IsNullOrEmpty(componentId) || string.IsNullOrEmpty(binding)) yield break;

            var data = A2uiV08Processor.GetByPath(_state.DataModel, binding);
            if (data is JObject map)
            {
                foreach (var prop in map.Properties())
                {
                    yield return new ChildRef
                    {
                        ComponentId = componentId,
                        ScopeKey = binding.TrimEnd('/') + "/" + prop.Name,
                        ScopeData = prop.Value
                    };
                }
            }
            else if (data is JArray arr)
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    yield return new ChildRef
                    {
                        ComponentId = componentId,
                        ScopeKey = binding.TrimEnd('/') + "/" + i,
                        ScopeData = arr[i]
                    };
                }
            }
        }

        static void ApplyDistribution(VisualElement ve, string distribution)
        {
            ve.style.justifyContent = distribution switch
            {
                "center" => Justify.Center,
                "end" => Justify.FlexEnd,
                "spaceBetween" => Justify.SpaceBetween,
                "spaceAround" => Justify.SpaceAround,
                // UITK 无 SpaceEvenly，用 SpaceAround 近似；量产若要像素级均分需自定义布局。
                "spaceEvenly" => Justify.SpaceAround,
                _ => Justify.FlexStart
            };
        }

        static void ApplyAlignment(VisualElement ve, string alignment)
        {
            ve.style.alignItems = alignment switch
            {
                "center" => Align.Center,
                "end" => Align.FlexEnd,
                "stretch" => Align.Stretch,
                _ => Align.FlexStart
            };
        }

        string ResolveString(JToken bound)
        {
            var v = ResolveBound(bound);
            if (v == null) return null;
            // 只接受标量。对象/数组绝不把原始 JSON 倒灌进界面（这就是之前框体里出现 {...} 的原因）。
            if (v.Type == JTokenType.String) return v.Value<string>();
            if (v.Type == JTokenType.Integer || v.Type == JTokenType.Float)
                return v.Value<double>().ToString(CultureInfo.InvariantCulture);
            Debug.LogWarning($"[A2UISchemeA] ResolveString 收到非标量绑定值（已忽略原始 JSON，类型={v.Type}）");
            return null;
        }

        JToken ResolveBound(JToken bound)
        {
            if (bound == null) return null;
            if (bound.Type != JTokenType.Object) return bound;
            var o = (JObject)bound;
            // 优先用 path 绑定：命中就返回绑定值；首帧还没数据时退回到同一条里的字面量。
            // 这样倒计时这类会变的数值写 {path, literalNumber} 就能首帧有值、后续被 dataModelUpdate 覆盖。
            if (o["path"] != null)
            {
                var path = o["path"].Value<string>();
                if (!string.IsNullOrEmpty(path))
                {
                    JToken resolved = path.StartsWith("/", StringComparison.Ordinal)
                        ? A2uiV08Processor.GetByPath(_state.DataModel, path)
                        : (_scopeStack.Count > 0
                            ? A2uiV08Processor.GetByPathFromToken(_scopeStack.Peek(), path)
                            : A2uiV08Processor.GetByPath(_state.DataModel, path));
                    if (resolved != null) return resolved;
                }
            }

            if (o["literalString"] != null) return o["literalString"].Value<string>();
            if (o["literalNumber"] != null) return o["literalNumber"].Value<double>();
            if (o["literalBoolean"] != null) return o["literalBoolean"].Value<bool>();
            if (o["literalArray"] is JArray arr) return arr;
            return null;
        }

        static VisualElement Placeholder(string text)
        {
            var l = new Label("[" + text + "]");
            l.AddToClassList("a2ui-placeholder");
            return l;
        }

        static string Truncate(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n) + "…";
        }
    }
}
