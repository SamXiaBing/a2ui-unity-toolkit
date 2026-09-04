using System;
using System.Collections.Generic;
using UnityEngine;

namespace A2UISchemeA
{
    /// <summary>
    /// 原子组件样式工作台的数据载体。场景里每个原子组件一个 GameObject 挂本脚本，
    /// Inspector 直接改字段；改动触发 <see cref="Changed"/>，由 A2uiStyleWorkbench 实时重建。
    /// 保存 = Ctrl+S 存场景（字段持久化）+ 导出 USS 规则（样式复用）。
    /// </summary>
    public class A2uiAtomicComponent : MonoBehaviour
    {
        public enum AtomicType
        {
            Text,
            Button,
            CheckBox,
            Slider,
            MultipleChoice,
            Divider,
            Card,
            Row,
            Column,
            MediaMiniBar,
            ClimateStep,
            RestBanner,
            Image,
            Tabs,
            TextField,
            DateTimeInput,
        }

        public enum Hint
        {
            caption, body, h1, h2, h3, h4
        }

        // ===== 协议 props =====
        [Header("A2UI props")]
        public AtomicType Type = AtomicType.Text;
        public string Text = "按钮文字";
        public Hint UsageHint = Hint.body;
        public bool Primary;
        public string Label = "标签";
        public float Value = 80;
        public float MinValue = 0;
        public float MaxValue = 100;
        public bool IsChecked = true;
        public int MaxAllowedSelections = 2;
        public List<string> Options = new List<string> { "选项 A", "选项 B", "选项 C" };
        public string Title = "雨林白噪 - 鸟鸣";
        public string TempLabel = "24°C";
        public string PlaceholderText = "输入内容…";

        // ===== 样式覆盖（Inspector 改，工作台实时应用到组件本体，保存导出为 USS 规则）=====
        [Header("样式覆盖（0 = 跟随 USS 默认）")]
        public bool OverrideFontSize;
        public float FontSize = 18;
        public bool OverrideColor;
        public Color Color = Color.white;
        public bool OverrideBackground;
        public Color BackgroundColor = new Color(0.16f, 0.22f, 0.34f, 0.9f);
        public bool OverrideBorderRadius;
        public float BorderRadius = 10;
        public bool OverridePadding;
        public float Padding = 12;

        /// <summary>Inspector 字段变化时触发（OnValidate），工作台据此重建该组件。</summary>
        public event Action<A2uiAtomicComponent> Changed;

        /// <summary>任意组件字段变化的全局通知（工作台订阅，统一重建）。</summary>
        public static event Action<A2uiAtomicComponent> OnAnyChanged;

        /// <summary>导出 USS 时使用的选择器名（按类型+关键 props 归类，全局皮肤规则）。</summary>
        public string RuleSelector()
        {
            return Type switch
            {
                AtomicType.Text => ".a2ui-text--" + UsageHint,
                AtomicType.Button => Primary ? ".a2ui-btn--primary" : ".a2ui-btn--secondary",
                AtomicType.CheckBox => ".a2ui-checkbox-wrap",
                AtomicType.Slider => ".a2ui-slider",
                AtomicType.MultipleChoice => ".a2ui-choice",
                AtomicType.Divider => ".a2ui-divider--horizontal",
                AtomicType.Card => ".a2ui-card",
                AtomicType.MediaMiniBar => ".a2ui-cabin--media",
                AtomicType.ClimateStep => ".a2ui-cabin--climate",
                AtomicType.RestBanner => ".a2ui-cabin--rest",
                AtomicType.Image => ".a2ui-image",
                AtomicType.Tabs => ".a2ui-tabs",
                AtomicType.TextField => ".a2ui-textfield",
                AtomicType.DateTimeInput => ".a2ui-datetime",
                _ => ".a2ui-row"
            };
        }

        /// <summary>是否设置了任何样式覆盖（用于导出判断）。</summary>
        public bool HasOverrides =>
            OverrideFontSize || OverrideColor || OverrideBackground ||
            OverrideBorderRadius || OverridePadding;

        void OnValidate()
        {
            // 编辑器改 Inspector 立即触发；运行时不受影响。
            Changed?.Invoke(this);
            OnAnyChanged?.Invoke(this);
        }
    }
}
