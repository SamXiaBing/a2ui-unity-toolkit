using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace A2UISchemeA
{
    /// <summary>
    /// A2UI v0.9 → v0.8 内部模型归一化。
    /// 把 v0.9 的平铺组件结构（component 为字符串字段、children 为数组、
    /// text 为直接字符串）转换成与 v0.8 等价的嵌套 JObject，
    /// 这样 Mapper / Processor 内部完全不需要区分协议版本。
    /// </summary>
    public static class A2uiV09Normalizer
    {
        /// <summary>v0.9 消息 key 集合。</summary>
        static readonly HashSet<string> V09Keys = new HashSet<string>
        {
            "createSurface", "updateComponents", "updateDataModel", "deleteSurface"
        };

        /// <summary>判断一条消息是否为 v0.9 格式。</summary>
        public static bool IsV09(JObject msg)
        {
            if (msg == null) return false;
            foreach (var k in V09Keys)
            {
                if (msg[k] != null) return true;
            }
            return false;
        }

        /// <summary>
        /// 把 v0.9 消息归一化为一组 v0.8 内部格式的消息。
        /// 一条 v0.9 消息可能产出 0~N 条 v0.8 消息：
        ///   createSurface          → surfaceUpdate（空壳）+ beginRendering（root 占位，等 updateComponents 补）
        ///   updateComponents       → surfaceUpdate（归一化组件）+ beginRendering（如含 root 且未 ready）
        ///   updateDataModel        → dataModelUpdate（value 直接透传，无需 contents 数组）
        ///   deleteSurface          → deleteSurface（透传）
        /// </summary>
        public static List<JObject> Normalize(JObject v09Msg)
        {
            var result = new List<JObject>();
            if (v09Msg == null) return result;

            if (v09Msg["createSurface"] is JObject cs)
            {
                NormalizeCreateSurface(cs, result);
            }
            else if (v09Msg["updateComponents"] is JObject uc)
            {
                NormalizeUpdateComponents(uc, result);
            }
            else if (v09Msg["updateDataModel"] is JObject udm)
            {
                NormalizeUpdateDataModel(udm, result);
            }
            else if (v09Msg["deleteSurface"] is JObject ds)
            {
                // deleteSurface v0.8/v0.9 格式一致，直接透传
                var passthrough = new JObject { ["deleteSurface"] = ds.DeepClone() };
                result.Add(passthrough);
            }

            return result;
        }

        static void NormalizeCreateSurface(JObject cs, List<JObject> result)
        {
            var surfaceId = cs["surfaceId"]?.Value<string>()
                            ?? throw new InvalidOperationException("v0.9 createSurface.surfaceId required");
            var catalogId = cs["catalogId"]?.Value<string>() ?? "https://a2ui.org/specification/v0_9/catalogs/basic/catalog.json";

            // → 等价 v0.8 surfaceUpdate（空组件壳，让 GetOrCreate 建立 surface 状态）
            var su = new JObject
            {
                ["surfaceUpdate"] = new JObject
                {
                    ["surfaceId"] = surfaceId,
                    ["components"] = new JArray()
                }
            };
            result.Add(su);

            // → 等价 v0.8 beginRendering（root 尚未有，标记 ready 但 root 待 updateComponents 填）
            // v0.9 规范说 root 是 updateComponents 里 id="root" 的组件，
            // 所以这里先用占位 root="root"（Processor 的 Mapper 在找不到 root 时已有
            // Placeholder("missing:root") 降级，后续 updateComponents 到达会自动覆盖）。
            var br = new JObject
            {
                ["beginRendering"] = new JObject
                {
                    ["surfaceId"] = surfaceId,
                    ["root"] = "root",
                    ["catalogId"] = catalogId
                }
            };
            result.Add(br);
        }

        static void NormalizeUpdateComponents(JObject uc, List<JObject> result)
        {
            var surfaceId = uc["surfaceId"]?.Value<string>()
                            ?? throw new InvalidOperationException("v0.9 updateComponents.surfaceId required");
            var components = uc["components"] as JArray
                             ?? throw new InvalidOperationException("v0.9 updateComponents.components required array");

            var normalized = new JArray();
            foreach (var token in components)
            {
                if (token is not JObject comp) continue;
                var normalizedComp = NormalizeComponent(comp);
                if (normalizedComp != null) normalized.Add(normalizedComp);
            }

            var su = new JObject
            {
                ["surfaceUpdate"] = new JObject
                {
                    ["surfaceId"] = surfaceId,
                    ["components"] = normalized
                }
            };
            result.Add(su);
        }

        /// <summary>
        /// 把 v0.9 平铺组件归一化为 v0.8 嵌套组件。
        /// v0.9: {"id":"r", "component":"Card", "child":"c", "text":"hi", ...}
        /// v0.8 内部模型: {"id":"r", "component":{"Card":{"child":"c"}}, "text":{"literalString":"hi"}}
        ///
        /// 关键设计：所有属性值**保持原始标量/对象/数组**，不包 literalString 壳。
        /// Mapper 的 MapXxx 方法用 .Value<string>() 直接读 props —— 包了壳就崩。
        /// path 绑定（{"path":"/x"}）原样保留，Mapper 的 ResolveBound 已支持。
        /// 字面值绑定通过 NormalizeProperty 的标量→JValue 直接映射实现。
        ///
        /// 属性名映射：variant→usageHint(Text) / justify→distribution / align→alignment
        /// children 数组→explicitList / children 对象→template
        /// </summary>
        static JObject NormalizeComponent(JObject comp)
        {
            var id = comp["id"]?.Value<string>();
            if (string.IsNullOrEmpty(id)) return null;
            var type = comp["component"]?.Value<string>();
            if (string.IsNullOrEmpty(type)) return null;

            // 构造 v0.8 的属性包（wrapper 内层的 JObject）
            var props = new JObject();
            foreach (var prop in comp.Properties())
            {
                if (prop.Name == "id" || prop.Name == "component") continue;
                NormalizeProperty(prop.Name, prop.Value, type, props);
            }

            // v0.8 格式：text 等属性要在 wrapper 外层
            var result = new JObject();
            var wrapper = new JObject { [type] = props };
            result["id"] = id;
            result["component"] = wrapper;

            // 属性复制到外层（此时已无 literal 壳，全为原始 JValue/JObject）
            foreach (var kv in props)
            {
                result[kv.Key] = kv.Value;
            }

            return result;
        }

        static void NormalizeProperty(string name, JToken value, string componentType, JObject props)
        {
            // 所有属性统一策略：保持原始 JToken 不包装。
            // Mapper 的 MapXxx 用 .Value<T>() 直接读（对 JValue 有效），
            // 或用 ResolveString/ResolveBound 读（对 {path} 对象有效）。
            // literalString 壳会让 .Value<T>() 抛 InvalidCastException。
            // 只做属性名映射和 children 结构转换。
            switch (name)
            {
                case "children":
                    NormalizeChildren(value, props);
                    break;

                case "variant":
                    // Text 的 variant 是字号 → v0.8 usageHint；Button 的 variant:"primary" → primary:true；
                    // 其他组件保持 variant
                    if (componentType == "Text")
                        props["usageHint"] = value.DeepClone();
                    else if (componentType == "Button" && value.Value<string>() == "primary")
                        props["primary"] = true;
                    else
                        props["variant"] = value.DeepClone();
                    break;

                case "action":
                    props["action"] = NormalizeAction(value);
                    break;

                case "usageHint":
                    props["usageHint"] = value.DeepClone();
                    break;

                case "justify":
                    props["distribution"] = value.DeepClone();
                    break;

                case "align":
                    props["alignment"] = value.DeepClone();
                    break;

                case "min":
                    props["minValue"] = value.DeepClone();
                    break;

                case "max":
                    props["maxValue"] = value.DeepClone();
                    break;

                default:
                    // 包括 text/label/url/value/child/tabItems/checks 等
                    // 全部原样透传（v0.9 的直接值/对象/数组就是 v0.8 Mapper 可读的格式）
                    props[name] = value.DeepClone();
                    break;
            }
        }

        /// <summary>
        /// v0.9 action {event:{name, context:{k:v}}} → 内部模型 {name, context:[{key,value}]}。
        /// Mapper 的 BuildActionContext 只认 context 数组、MapButton 只读 action.name。
        /// 已是 v0.8 形状（无 event 包裹）的 action 原样透传。
        /// </summary>
        static JToken NormalizeAction(JToken value)
        {
            if (value is not JObject act) return value?.DeepClone();
            if (act["event"] is not JObject ev) return act.DeepClone();

            var outAct = new JObject();
            foreach (var p in ev.Properties())
                if (p.Name != "context")
                    outAct[p.Name] = p.Value.DeepClone();

            if (ev["context"] is JObject ctx)
            {
                var arr = new JArray();
                foreach (var p in ctx.Properties())
                    arr.Add(new JObject { ["key"] = p.Name, ["value"] = p.Value.DeepClone() });
                outAct["context"] = arr;
            }
            return outAct;
        }

        static void NormalizeChildren(JToken value, JObject props)
        {
            // v0.9 两种形式：
            //   数组：["id1", "id2"]  → v0.8 explicitList
            //   对象：{"path":"/x", "componentId":"tpl"} → v0.8 template
            if (value is JArray arr)
            {
                props["children"] = new JObject
                {
                    ["explicitList"] = arr.DeepClone()
                };
            }
            else if (value is JObject obj)
            {
                // v0.9: {"path":"/employees", "componentId":"tpl"}
                // v0.8: {"template":{"path":"/employees", "componentId":"tpl"}}
                props["children"] = new JObject
                {
                    ["template"] = new JObject
                    {
                        ["dataBinding"] = obj["path"]?.DeepClone() ?? "/",
                        ["componentId"] = obj["componentId"]?.DeepClone()
                    }
                };
            }
        }

        static void NormalizeUpdateDataModel(JObject udm, List<JObject> result)
        {
            var surfaceId = udm["surfaceId"]?.Value<string>()
                            ?? throw new InvalidOperationException("v0.9 updateDataModel.surfaceId required");
            var path = udm["path"]?.Value<string>() ?? "/";
            var value = udm["value"]; // 可能不存在 = 删除该 path

            // v0.8 dataModelUpdate 用 contents 数组格式
            // v0.9 用直接的 value 对象
            // 归一化：把 value 包装成 contents [{key, valueMap/valueString}]
            var contents = new JArray();

            if (value is JObject vObj && path == "/")
            {
                // 替换整棵树：展开为多个 key-value entry
                foreach (var prop in vObj.Properties())
                {
                    contents.Add(WrapValueEntry(prop.Name, prop.Value));
                }
            }
            else if (value != null)
            {
                // 单 path 更新：把 path 末段作为 key
                var parts = path.TrimStart('/').Split('/');
                var key = parts.Length > 0 ? parts[^1] : "_root";
                contents.Add(WrapValueEntry(key, value));
                path = string.Join("/", parts, 0, parts.Length - 1);
                if (string.IsNullOrEmpty(path)) path = "/";
            }
            else
            {
                // 删除：设置 null
                contents.Add(new JObject
                {
                    ["key"] = path.TrimStart('/').Split('/').Last(),
                    ["valueString"] = ""
                });
            }

            var dm = new JObject
            {
                ["dataModelUpdate"] = new JObject
                {
                    ["surfaceId"] = surfaceId,
                    ["contents"] = contents
                }
            };
            if (path != "/")
                dm["dataModelUpdate"]["path"] = "/" + path.TrimStart('/');

            result.Add(dm);
        }

        static JObject WrapValueEntry(string key, JToken value)
        {
            var entry = new JObject { ["key"] = key };
            switch (value.Type)
            {
                case JTokenType.String:
                    entry["valueString"] = value.Value<string>();
                    break;
                case JTokenType.Boolean:
                    entry["valueBoolean"] = value.Value<bool>();
                    break;
                case JTokenType.Integer:
                case JTokenType.Float:
                    entry["valueNumber"] = value.Value<double>();
                    break;
                case JTokenType.Object:
                    // 递归展开为 valueMap
                    var map = new JArray();
                    foreach (var prop in ((JObject)value).Properties())
                        map.Add(WrapValueEntry(prop.Name, prop.Value));
                    entry["valueMap"] = map;
                    break;
                case JTokenType.Array:
                    // 数组：每个元素编号为 0,1,2...（与 GetByPathFromToken 的数组下标路径匹配）
                    var arrMap = new JArray();
                    var idx = 0;
                    foreach (var item in (JArray)value)
                    {
                        arrMap.Add(WrapValueEntry(idx.ToString(), item));
                        idx++;
                    }
                    entry["valueMap"] = arrMap;
                    break;
                default:
                    entry["valueString"] = value?.ToString() ?? "";
                    break;
            }
            return entry;
        }
    }
}
