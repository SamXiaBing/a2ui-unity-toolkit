using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace A2UISchemeA
{
    public sealed class A2uiValidationResult
    {
        public bool Ok;
        public string Error;
        public static A2uiValidationResult Pass() => new A2uiValidationResult { Ok = true };
        public static A2uiValidationResult Fail(string error) => new A2uiValidationResult { Ok = false, Error = error };
    }

    /// <summary>
    /// G0：入站报文结构校验（精简契约，非完整 JSON Schema 引擎）。
    /// </summary>
    public static class A2uiV08Validator
    {
        static readonly HashSet<string> MessageKeys = new HashSet<string>
        {
            "surfaceUpdate", "dataModelUpdate", "beginRendering", "deleteSurface"
        };

        static readonly HashSet<string> V09Keys = new HashSet<string>
        {
            "createSurface", "updateComponents", "updateDataModel", "deleteSurface"
        };

        public static readonly HashSet<string> StandardTypes = new HashSet<string>
        {
            "Text", "Image", "Icon", "Video", "AudioPlayer",
            "Row", "Column", "List", "Card", "Tabs", "Divider", "Modal",
            "Button", "CheckBox", "TextField", "DateTimeInput", "MultipleChoice", "Slider"
        };

        public static readonly HashSet<string> CabinTypes = new HashSet<string>
        {
            "MediaMiniBar", "ClimateStep", "RestBanner"
        };

        public static bool IsKnownType(string type) =>
            StandardTypes.Contains(type) || CabinTypes.Contains(type);

        public static A2uiValidationResult ValidateMessage(JObject msg)
        {
            if (msg == null) return A2uiValidationResult.Fail("message is null");

            // v0.9 双栈：createSurface 等平铺格式走独立校验
            foreach (var k in V09Keys)
            {
                if (msg[k] != null) return ValidateV09Message(k, (JObject)msg[k]);
            }

            string hit = null;
            foreach (var key in MessageKeys)
            {
                if (msg[key] == null) continue;
                if (hit != null) return A2uiValidationResult.Fail($"multiple message types: {hit} and {key}");
                hit = key;
            }

            if (hit == null)
                return A2uiValidationResult.Fail("must contain one of: surfaceUpdate, dataModelUpdate, beginRendering, deleteSurface");

            return hit switch
            {
                "surfaceUpdate" => ValidateSurfaceUpdate((JObject)msg["surfaceUpdate"]),
                "dataModelUpdate" => ValidateDataModelUpdate((JObject)msg["dataModelUpdate"]),
                "beginRendering" => ValidateBeginRendering((JObject)msg["beginRendering"]),
                "deleteSurface" => ValidateDeleteSurface((JObject)msg["deleteSurface"]),
                _ => A2uiValidationResult.Fail("unknown message")
            };
        }

        public static A2uiValidationResult ValidateJsonl(string text, out List<JObject> messages)
        {
            messages = new List<JObject>();
            if (string.IsNullOrWhiteSpace(text))
                return A2uiValidationResult.Fail("empty payload");

            var n = 0;
            using var reader = new System.IO.StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                n++;
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                JObject obj;
                try { obj = JObject.Parse(t); }
                catch (Exception e)
                {
                    return A2uiValidationResult.Fail($"line {n}: JSON parse error: {e.Message}");
                }

                var r = ValidateMessage(obj);
                if (r.Ok)
                {
                    // v0.9 消息归一化为 v0.8 格式后加入 messages，
                    // 保证 Processor 收到的始终是内部格式
                    if (A2uiV09Normalizer.IsV09(obj))
                    {
                        var normalized = A2uiV09Normalizer.Normalize(obj);
                        messages.AddRange(normalized);
                    }
                    else
                    {
                        messages.Add(obj);
                    }
                }
                else
                {
                    // 容错：生成端偶尔会把多条消息合并进同一个 JSON 对象
                    // （例如 surfaceUpdate + beginRendering 并在一行）。这里按
                    // MessageKeys 顺序拆成多条单类型消息分别校验，行为等价于
                    // "每行一个消息类型" 的合法报文，避免整帧被拒、界面空白。
                    var split = TrySplitMultiMessage(obj, out var splitErr);
                    if (split != null)
                        messages.AddRange(split);
                    else
                        return A2uiValidationResult.Fail($"line {n}: {r.Error}");
                }
            }

            if (messages.Count == 0)
                return A2uiValidationResult.Fail("no messages");
            return A2uiValidationResult.Pass();
        }

        /// <summary>
        /// 兜底：把一个含多个消息类型键的对象拆成多条单类型消息（保持 MessageKeys 顺序）。
        /// 任一部分校验失败则返回 null。
        /// </summary>
        static List<JObject> TrySplitMultiMessage(JObject obj, out string err)
        {
            err = null;
            var keys = new List<string>();
            foreach (var k in MessageKeys)
                if (obj[k] != null) keys.Add(k);
            if (keys.Count < 2) return null;

            var outList = new List<JObject>();
            foreach (var k in keys)
            {
                var single = new JObject();
                single[k] = obj[k];
                var rr = ValidateMessage(single);
                if (!rr.Ok) { err = rr.Error; return null; }
                outList.Add(single);
            }
            return outList;
        }

        static A2uiValidationResult ValidateSurfaceUpdate(JObject body)
        {
            if (string.IsNullOrEmpty(body["surfaceId"]?.Value<string>()))
                return A2uiValidationResult.Fail("surfaceUpdate.surfaceId required");
            if (body["components"] is not JArray comps)
                return A2uiValidationResult.Fail("surfaceUpdate.components required array");

            foreach (var token in comps)
            {
                if (token is not JObject comp)
                    return A2uiValidationResult.Fail("component entry must be object");
                if (string.IsNullOrEmpty(comp["id"]?.Value<string>()))
                    return A2uiValidationResult.Fail("component.id required");
                if (comp["component"] is not JObject wrapper)
                    return A2uiValidationResult.Fail($"component '{comp["id"]}' missing component wrapper");
                var count = 0;
                foreach (var _ in wrapper.Properties()) count++;
                if (count != 1)
                    return A2uiValidationResult.Fail($"component '{comp["id"]}' must have exactly one type key");
            }

            return A2uiValidationResult.Pass();
        }

        static A2uiValidationResult ValidateDataModelUpdate(JObject body)
        {
            if (string.IsNullOrEmpty(body["surfaceId"]?.Value<string>()))
                return A2uiValidationResult.Fail("dataModelUpdate.surfaceId required");
            if (body["contents"] is not JArray)
                return A2uiValidationResult.Fail("dataModelUpdate.contents required array");
            return A2uiValidationResult.Pass();
        }

        static A2uiValidationResult ValidateBeginRendering(JObject body)
        {
            if (string.IsNullOrEmpty(body["surfaceId"]?.Value<string>()))
                return A2uiValidationResult.Fail("beginRendering.surfaceId required");
            if (string.IsNullOrEmpty(body["root"]?.Value<string>()))
                return A2uiValidationResult.Fail("beginRendering.root required");
            return A2uiValidationResult.Pass();
        }

        static A2uiValidationResult ValidateDeleteSurface(JObject body)
        {
            if (string.IsNullOrEmpty(body["surfaceId"]?.Value<string>()))
                return A2uiValidationResult.Fail("deleteSurface.surfaceId required");
            return A2uiValidationResult.Pass();
        }

        // ========== v0.9 校验 ==========

        static A2uiValidationResult ValidateV09Message(string key, JObject body)
        {
            return key switch
            {
                "createSurface" => ValidateV09CreateSurface(body),
                "updateComponents" => ValidateV09UpdateComponents(body),
                "updateDataModel" => ValidateV09UpdateDataModel(body),
                "deleteSurface" => ValidateDeleteSurface(body),
                _ => A2uiValidationResult.Fail("unknown v0.9 message: " + key)
            };
        }

        static A2uiValidationResult ValidateV09CreateSurface(JObject body)
        {
            if (string.IsNullOrEmpty(body["surfaceId"]?.Value<string>()))
                return A2uiValidationResult.Fail("createSurface.surfaceId required");
            // catalogId 必填（协议 v0.9 语义；转换器 Tools/v08_to_v09.py 与全部样例均已带值）
            if (string.IsNullOrEmpty(body["catalogId"]?.Value<string>()))
                return A2uiValidationResult.Fail("createSurface.catalogId required");
            return A2uiValidationResult.Pass();
        }

        static A2uiValidationResult ValidateV09UpdateComponents(JObject body)
        {
            if (string.IsNullOrEmpty(body["surfaceId"]?.Value<string>()))
                return A2uiValidationResult.Fail("updateComponents.surfaceId required");
            if (body["components"] is not JArray comps)
                return A2uiValidationResult.Fail("updateComponents.components required array");

            foreach (var token in comps)
            {
                if (token is not JObject comp)
                    return A2uiValidationResult.Fail("component entry must be object");
                if (string.IsNullOrEmpty(comp["id"]?.Value<string>()))
                    return A2uiValidationResult.Fail("component.id required");
                if (string.IsNullOrEmpty(comp["component"]?.Value<string>()))
                    return A2uiValidationResult.Fail($"component '{comp["id"]}' missing component type string");
            }

            return A2uiValidationResult.Pass();
        }

        static A2uiValidationResult ValidateV09UpdateDataModel(JObject body)
        {
            if (string.IsNullOrEmpty(body["surfaceId"]?.Value<string>()))
                return A2uiValidationResult.Fail("updateDataModel.surfaceId required");
            // v0.9: value 可选（缺省=删除 path），path 可选（缺省="/"）
            return A2uiValidationResult.Pass();
        }
    }
}
