using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA
{
    /// <summary>
    /// 原子组件的纯渲染器（与 A2uiStyleWorkbench 同源，但独立维护，专供编辑器预览使用）。
    /// 输入一个 A2uiAtomicComponent，输出套好 class 的 VisualElement。样式全靠外部给它所在容器
    /// 加载的 USS 决定（见 ResolveSkin）。
    /// 注意：本类与 A2uiStyleWorkbench 的 Build* 逻辑一致，若改一处请同步另一处。
    /// </summary>
    public static class A2uiAtomicRenderer
    {
        // IStyle 没有 padding/margin/borderWidth 等整体属性，只有拆开的四边，这里统一赋值。
        public static void SetPadding(VisualElement ve, float v)
        {
            ve.style.paddingLeft = ve.style.paddingRight = ve.style.paddingTop = ve.style.paddingBottom = v;
        }
        public static void SetMargin(VisualElement ve, float v)
        {
            ve.style.marginLeft = ve.style.marginRight = ve.style.marginTop = ve.style.marginBottom = v;
        }
        public static void SetBorderWidth(VisualElement ve, float v)
        {
            ve.style.borderLeftWidth = ve.style.borderRightWidth = ve.style.borderTopWidth = ve.style.borderBottomWidth = v;
        }
        public static void SetBorderColor(VisualElement ve, Color c)
        {
            ve.style.borderLeftColor = ve.style.borderRightColor = ve.style.borderTopColor = ve.style.borderBottomColor = c;
        }
        public static void SetBorderRadius(VisualElement ve, float v)
        {
            ve.style.borderTopLeftRadius = ve.style.borderTopRightRadius = ve.style.borderBottomLeftRadius = ve.style.borderBottomRightRadius = v;
        }

        public static VisualElement Build(A2uiAtomicComponent c)
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

        static VisualElement BuildText(A2uiAtomicComponent c)
        {
            var label = new Label(c.Text);
            label.AddToClassList("a2ui-text");
            label.AddToClassList("a2ui-text--" + c.UsageHint);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        static VisualElement BuildButton(A2uiAtomicComponent c)
        {
            var btn = new Button(() => Debug.Log($"[Renderer] 点击按钮 {c.Text}")) { text = c.Text };
            btn.AddToClassList("a2ui-btn");
            btn.AddToClassList(c.Primary ? "a2ui-btn--primary" : "a2ui-btn--secondary");
            return btn;
        }

        static VisualElement BuildCheckBox(A2uiAtomicComponent c)
        {
            var wrap = new VisualElement();
            wrap.AddToClassList("a2ui-checkbox-wrap");
            wrap.style.width = Length.Percent(100);
            var toggle = new Toggle(c.Label) { value = c.IsChecked };
            toggle.AddToClassList("a2ui-checkbox");
            wrap.Add(toggle);
            return wrap;
        }

        static VisualElement BuildSlider(A2uiAtomicComponent c)
        {
            var slider = new Slider(c.Label, c.MinValue, c.MaxValue) { value = c.Value, showInputField = true };
            slider.AddToClassList("a2ui-slider");
            return slider;
        }

        static VisualElement BuildChoice(A2uiAtomicComponent c)
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

        static VisualElement BuildDivider()
        {
            var line = new VisualElement();
            line.AddToClassList("a2ui-divider");
            line.AddToClassList("a2ui-divider--horizontal");
            return line;
        }

        static VisualElement BuildCard(A2uiAtomicComponent c)
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

        static VisualElement BuildFlex(A2uiAtomicComponent c, FlexDirection dir, string cls)
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

        static VisualElement BuildMediaMiniBar(A2uiAtomicComponent c)
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
            var play = new Button(() => Debug.Log("[Renderer] toggle_play")) { text = "▶ 播放" };
            play.AddToClassList("a2ui-cabin__play");
            row.Add(play);
            return row;
        }

        static VisualElement BuildClimateStep(A2uiAtomicComponent c)
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

        static VisualElement BuildRestBanner(A2uiAtomicComponent c)
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

        static VisualElement BuildImage(A2uiAtomicComponent c)
        {
            var box = new VisualElement();
            box.AddToClassList("a2ui-image");
            box.style.minHeight = 80;
            var caption = new Label(c.Label);
            caption.AddToClassList("a2ui-image__caption");
            box.Add(caption);
            return box;
        }

        static VisualElement BuildTabs(A2uiAtomicComponent c)
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

        static VisualElement BuildTextField(A2uiAtomicComponent c)
        {
            var tf = new TextField(c.Label) { value = c.PlaceholderText };
            tf.AddToClassList("a2ui-textfield");
            return tf;
        }

        static VisualElement BuildDateTime(A2uiAtomicComponent c)
        {
            var tf = new TextField("DateTime+date") { value = c.PlaceholderText };
            tf.AddToClassList("a2ui-datetime");
            return tf;
        }

        static void ApplyOverrides(VisualElement ve, A2uiAtomicComponent c)
        {
            if (c.OverrideFontSize) ve.style.fontSize = c.FontSize;
            if (c.OverrideColor) ve.style.color = c.Color;
            if (c.OverrideBackground) ve.style.backgroundColor = c.BackgroundColor;
            if (c.OverrideBorderRadius) SetBorderRadius(ve, c.BorderRadius);
            if (c.OverridePadding) SetPadding(ve, c.Padding);
        }

        /// <summary>
        /// 根据所选 USS 文件名，决定预览容器该加哪些皮肤 class、以及需要加载哪些基础 USS 文件。
        /// 例如文件名含 DS/ds/DesignSystem → 容器加 ds-root，加载全套 DS（sinanata/unity-ui-toolkit-design-system，MIT）样式；
        /// 其余一律回退到 Crafted（M3 打磨）皮肤。
        /// </summary>
        public static void ResolveSkin(string ussFileName, out List<string> classList, out List<string> baseUss)
        {
            classList = new List<string>();
            baseUss = new List<string>();
            var f = ussFileName ?? "";
            if (f.Contains("DS") || f.Contains("ds") || f.Contains("DesignSystem"))
            {
                // DS 设计系统（sinanata/unity-ui-toolkit-design-system，MIT）：ds-root 作用域 + 全套 DS 样式
                classList.Add("ds-root");
                const string prefix = "Assets/A2UISchemeA/Styles/";
                foreach (var p in A2uiDsStyles.DsStylePaths)
                    baseUss.Add(p.StartsWith(prefix) ? p.Substring(prefix.Length) : p);
                return;
            }
            classList.Add("a2ui-skin--crafted");
            baseUss.Add("Crafted.uss");
            if (f.Contains("Aaos")) classList.Add("a2ui-token--aaos");
            else if (f.Contains("Cloud")) classList.Add("a2ui-token--cloud");
            else if (f.Contains("Ice")) classList.Add("a2ui-token--ice");
        }
    }
}
