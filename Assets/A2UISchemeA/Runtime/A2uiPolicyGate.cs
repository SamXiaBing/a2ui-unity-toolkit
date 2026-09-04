using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA
{
    public enum VehicleGear
    {
        P,
        R,
        N,
        D
    }

    /// <summary>
    /// G4：情境门禁——行驶中禁止复杂组件，强制单行提示模板。
    /// </summary>
    public class A2uiPolicyGate
    {
        public VehicleGear Gear = VehicleGear.P;
        public float SpeedKph;

        public bool IsDriving => Gear is VehicleGear.D or VehicleGear.R && SpeedKph > 0.5f;

        static readonly HashSet<string> RestrictedTypes = new HashSet<string>
        {
            "Tabs", "Modal", "List", "MultipleChoice", "Video", "DateTimeInput"
        };

        public bool AllowsComplex => !IsDriving;

        public string StatusText =>
            IsDriving
                ? $"GATE BLOCK complex · gear={Gear} speed={SpeedKph:0} → 强制行驶模板"
                : $"GATE ALLOW · gear={Gear} speed={SpeedKph:0}";

        /// <summary>
        /// 若行驶中且 surface 含受限类型，返回替换用 JSONL（单行提示）。
        /// </summary>
        public bool TryRewriteToDrivingTemplate(A2uiV08SurfaceState state, out string jsonl, out string reason)
        {
            jsonl = null;
            reason = null;
            if (!IsDriving || state == null) return false;

            var blocked = new List<string>();
            foreach (var def in state.Components.Values)
            {
                if (!A2uiV08Processor.TryGetComponentType(def, out var type, out _)) continue;
                if (RestrictedTypes.Contains(type)) blocked.Add(type);
            }

            if (blocked.Count == 0) return false;

            reason = "restricted: " + string.Join(",", blocked.Distinct());
            var sid = state.SurfaceId ?? "gated";
            jsonl =
                "{\"surfaceUpdate\":{\"surfaceId\":\"" + sid + "\",\"components\":[" +
                "{\"id\":\"root\",\"component\":{\"Card\":{\"child\":\"col\"}}}," +
                "{\"id\":\"col\",\"component\":{\"Column\":{\"children\":{\"explicitList\":[\"msg\",\"hint\"]},\"alignment\":\"center\"}}}," +
                "{\"id\":\"msg\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"行驶中已简化界面\"},\"usageHint\":\"h2\"}}}," +
                "{\"id\":\"hint\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"请停车后再查看完整内容\"},\"usageHint\":\"caption\"}}}" +
                "]}}\n" +
                "{\"beginRendering\":{\"surfaceId\":\"" + sid + "\",\"root\":\"root\",\"catalogId\":\"https://a2ui.org/specification/v0_8/json/standard_catalog_definition.json\"}}\n";
            return true;
        }
    }

    /// <summary>
    /// G3：未知类型 → Basic 降级卡片；超时骨架。
    /// </summary>
    public static class A2uiDegrade
    {
        public static VisualElement UnknownTypeFallback(string typeName, string id)
        {
            var card = new VisualElement();
            card.AddToClassList("a2ui-card");
            card.AddToClassList("a2ui-degrade");
            card.style.flexDirection = FlexDirection.Column;
            card.Add(new Label("降级组件").WithClass("a2ui-text").WithClass("a2ui-text--h4"));
            card.Add(new Label($"未知类型 `{typeName}` (id={id}) 已折叠为 Basic")
                .WithClass("a2ui-text").WithClass("a2ui-text--caption"));
            return card;
        }

        public static VisualElement Skeleton(string reason)
        {
            var box = new VisualElement();
            box.AddToClassList("a2ui-card");
            box.AddToClassList("a2ui-skeleton");
            box.style.flexDirection = FlexDirection.Column;
            box.Add(new Label("等待编排…").WithClass("a2ui-text").WithClass("a2ui-text--h3"));
            box.Add(new Label(reason ?? "agent timeout / degrade")
                .WithClass("a2ui-text").WithClass("a2ui-text--caption"));
            return box;
        }
    }
}
