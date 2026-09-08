using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace A2UISchemeA
{
    public sealed class A2uiValidationResult
    {
        public bool Ok;
        public string Error;

        /// <summary>失败位置的 JSON Pointer（指向被拒报文内的字段，如 /surfaceUpdate/components/0/id）；Ok 时为 null。</summary>
        public string Path;

        public static A2uiValidationResult Pass() => new A2uiValidationResult { Ok = true };

        public static A2uiValidationResult Fail(string error, string path = null) =>
            new A2uiValidationResult { Ok = false, Error = error, Path = path ?? "/" };
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

        // ---------- 防御性规模上限 ----------
        // 对齐 Compose 参考渲染器的同类防护（1MB / 1000 / 50 / 深度 10 / 键长 50），属加固项而非协议规范要求。
        // 拒收路径与 G0 一致：整体拒收、保留上一帧，宿主按 D3 官方 error 封套上报。

        /// <summary>单条 JSONL 载荷最大字节数（UTF-8）。</summary>
        public const int MaxMessageBytes = 1_048_576;

        /// <summary>单条 surfaceUpdate / updateComponents 消息最大组件数。</summary>
        public const int MaxComponentsPerUpdate = 1000;

        /// <summary>DataModel path 最大段数（JSON Pointer 层数）。</summary>
        public const int MaxPathDepth = 10;

        /// <summary>DataModel 键最大字符数。</summary>
        public const int MaxKeyLength = 50;

        /// <summary>单次校验最多收集的错误条数（超出截断，防错误风暴拖垮宿主与 agent 回写）。</summary>
        public const int MaxValidationErrors = 100;

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

            var byteCount = Encoding.UTF8.GetByteCount(text);
            if (byteCount > MaxMessageBytes)
                return A2uiValidationResult.Fail(
                    $"payload exceeds max message size {MaxMessageBytes} bytes (got {byteCount})");

            // 错误收集而非首错即停：一次校验最多上报 MaxValidationErrors 条，
            // 让 agent 侧一轮回看清全部问题（D2 错误条数上限）。
            var failures = new List<string>();
            string firstErrorPath = null;
            var truncated = false;
            // surfaceId 会话内唯一（官方 Processing rules）：同一载荷内 createSurface
            // 未删先建 → 整帧拒绝；跨载荷的重复由 Processor 持久状态兜底。
            var createdSurfaces = new HashSet<string>();

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
                    RecordFailure($"line {n}: JSON parse error: {e.Message}", "/");
                    if (truncated) break;
                    continue;
                }

                var r = ValidateMessage(obj);
                List<JObject> unit;
                List<JObject> toAdd;
                if (r.Ok)
                {
                    // v0.9 消息归一化为 v0.8 格式后加入 messages，
                    // 保证 Processor 收到的始终是内部格式。
                    // 生命周期跟踪必须用原始 v0.9 消息（归一化会丢掉 createSurface 键）。
                    unit = new List<JObject> { obj };
                    toAdd = A2uiV09Normalizer.IsV09(obj) ? A2uiV09Normalizer.Normalize(obj) : unit;
                }
                else
                {
                    // 容错：生成端偶尔会把多条消息合并进同一个 JSON 对象
                    // （例如 surfaceUpdate + beginRendering 并在一行）。这里按
                    // MessageKeys 顺序拆成多条单类型消息分别校验，行为等价于
                    // "每行一个消息类型" 的合法报文，避免整帧被拒、界面空白。
                    var split = TrySplitMultiMessage(obj, out var splitErr);
                    if (split == null)
                    {
                        RecordFailure($"line {n}: {r.Error}", r.Path);
                        if (truncated) break;
                        continue;
                    }

                    unit = split;
                    toAdd = split;
                }

                var dup = ScanSurfaceLifecycle(unit, createdSurfaces);
                if (dup != null)
                {
                    RecordFailure($"line {n}: {dup}", "/createSurface/surfaceId");
                    if (truncated) break;
                    continue;
                }

                messages.AddRange(toAdd);
            }

            if (failures.Count > 0)
            {
                var joined = string.Join(" | ", failures);
                if (truncated) joined += $" | errors truncated at {MaxValidationErrors}";
                return A2uiValidationResult.Fail(joined, firstErrorPath);
            }

            if (messages.Count == 0)
                return A2uiValidationResult.Fail("no messages");
            return A2uiValidationResult.Pass();

            void RecordFailure(string error, string path)
            {
                failures.Add(error);
                firstErrorPath ??= path;
                if (failures.Count >= MaxValidationErrors) truncated = true;
            }
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
                return A2uiValidationResult.Fail("surfaceUpdate.surfaceId required", "/surfaceUpdate/surfaceId");
            if (body["components"] is not JArray comps)
                return A2uiValidationResult.Fail("surfaceUpdate.components required array", "/surfaceUpdate/components");
            if (comps.Count > MaxComponentsPerUpdate)
                return A2uiValidationResult.Fail(
                    $"surfaceUpdate.components exceeds max component count {MaxComponentsPerUpdate} (got {comps.Count})",
                    "/surfaceUpdate/components");

            for (var i = 0; i < comps.Count; i++)
            {
                if (comps[i] is not JObject comp)
                    return A2uiValidationResult.Fail("component entry must be object", $"/surfaceUpdate/components/{i}");
                if (string.IsNullOrEmpty(comp["id"]?.Value<string>()))
                    return A2uiValidationResult.Fail("component.id required", $"/surfaceUpdate/components/{i}/id");
                if (comp["component"] is not JObject wrapper)
                    return A2uiValidationResult.Fail($"component '{comp["id"]}' missing component wrapper",
                        $"/surfaceUpdate/components/{i}/component");
                var count = 0;
                foreach (var _ in wrapper.Properties()) count++;
                if (count != 1)
                    return A2uiValidationResult.Fail($"component '{comp["id"]}' must have exactly one type key",
                        $"/surfaceUpdate/components/{i}/component");
            }

            return A2uiValidationResult.Pass();
        }

        static A2uiValidationResult ValidateDataModelUpdate(JObject body)
        {
            if (string.IsNullOrEmpty(body["surfaceId"]?.Value<string>()))
                return A2uiValidationResult.Fail("dataModelUpdate.surfaceId required", "/dataModelUpdate/surfaceId");
            if (body["contents"] is not JArray contents)
                return A2uiValidationResult.Fail("dataModelUpdate.contents required array", "/dataModelUpdate/contents");

            var path = body["path"]?.Value<string>();
            var depth = CountPathSegments(path);
            if (depth > MaxPathDepth)
                return A2uiValidationResult.Fail(
                    $"dataModelUpdate.path exceeds max path depth {MaxPathDepth} (got {depth})",
                    "/dataModelUpdate/path");

            for (var i = 0; i < contents.Count; i++)
            {
                if (contents[i] is not JObject entry) continue;
                var key = entry["key"]?.Value<string>();
                if (key != null && key.Length > MaxKeyLength)
                    return A2uiValidationResult.Fail(
                        $"data model key exceeds max key length {MaxKeyLength} (got {key.Length})",
                        $"/dataModelUpdate/contents/{i}/key");
                if (entry["valueMap"] != null)
                {
                    var treeErr = CheckContentsEntry(entry, 1, $"/dataModelUpdate/contents/{i}");
                    if (treeErr != null)
                        return A2uiValidationResult.Fail(treeErr, $"/dataModelUpdate/contents/{i}");
                }
            }

            return A2uiValidationResult.Pass();
        }

        static A2uiValidationResult ValidateBeginRendering(JObject body)
        {
            if (string.IsNullOrEmpty(body["surfaceId"]?.Value<string>()))
                return A2uiValidationResult.Fail("beginRendering.surfaceId required", "/beginRendering/surfaceId");
            if (string.IsNullOrEmpty(body["root"]?.Value<string>()))
                return A2uiValidationResult.Fail("beginRendering.root required", "/beginRendering/root");
            return A2uiValidationResult.Pass();
        }

        static A2uiValidationResult ValidateDeleteSurface(JObject body)
        {
            if (string.IsNullOrEmpty(body["surfaceId"]?.Value<string>()))
                return A2uiValidationResult.Fail("deleteSurface.surfaceId required", "/deleteSurface/surfaceId");
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
                return A2uiValidationResult.Fail("createSurface.surfaceId required", "/createSurface/surfaceId");
            // catalogId 必填（协议 v0.9 语义；转换器 Tools/v08_to_v09.py 与全部样例均已带值）
            if (string.IsNullOrEmpty(body["catalogId"]?.Value<string>()))
                return A2uiValidationResult.Fail("createSurface.catalogId required", "/createSurface/catalogId");
            return A2uiValidationResult.Pass();
        }

        static A2uiValidationResult ValidateV09UpdateComponents(JObject body)
        {
            if (string.IsNullOrEmpty(body["surfaceId"]?.Value<string>()))
                return A2uiValidationResult.Fail("updateComponents.surfaceId required", "/updateComponents/surfaceId");
            if (body["components"] is not JArray comps)
                return A2uiValidationResult.Fail("updateComponents.components required array", "/updateComponents/components");
            if (comps.Count > MaxComponentsPerUpdate)
                return A2uiValidationResult.Fail(
                    $"updateComponents.components exceeds max component count {MaxComponentsPerUpdate} (got {comps.Count})",
                    "/updateComponents/components");

            for (var i = 0; i < comps.Count; i++)
            {
                if (comps[i] is not JObject comp)
                    return A2uiValidationResult.Fail("component entry must be object", $"/updateComponents/components/{i}");
                if (string.IsNullOrEmpty(comp["id"]?.Value<string>()))
                    return A2uiValidationResult.Fail("component.id required", $"/updateComponents/components/{i}/id");
                if (string.IsNullOrEmpty(comp["component"]?.Value<string>()))
                    return A2uiValidationResult.Fail($"component '{comp["id"]}' missing component type string",
                        $"/updateComponents/components/{i}/component");
            }

            return A2uiValidationResult.Pass();
        }

        static A2uiValidationResult ValidateV09UpdateDataModel(JObject body)
        {
            if (string.IsNullOrEmpty(body["surfaceId"]?.Value<string>()))
                return A2uiValidationResult.Fail("updateDataModel.surfaceId required", "/updateDataModel/surfaceId");

            // v0.9: value 可选（缺省=删除 path），path 可选（缺省="/"）
            var path = body["path"]?.Value<string>();
            var depth = CountPathSegments(path);
            if (depth > MaxPathDepth)
                return A2uiValidationResult.Fail(
                    $"updateDataModel.path exceeds max path depth {MaxPathDepth} (got {depth})",
                    "/updateDataModel/path");

            var value = body["value"];
            if (value != null && value.Type != JTokenType.Null)
            {
                var treeErr = CheckDataTree(value, 0, "/updateDataModel/value");
                if (treeErr != null)
                    return A2uiValidationResult.Fail(treeErr, "/updateDataModel/value");
            }

            return A2uiValidationResult.Pass();
        }

        /// <summary>JSON Pointer 段数（忽略空段；"/" = 0 段）。</summary>
        static int CountPathSegments(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            var p = path.StartsWith("/") ? path.Substring(1) : path;
            var count = 0;
            foreach (var seg in p.Split('/'))
                if (seg.Length > 0) count++;
            return count;
        }

        /// <summary>
        /// 递归检查数据树：键长 ≤ MaxKeyLength、嵌套 ≤ MaxPathDepth 层。
        /// depth 从 0 计（数据值根的直接子键为第 1 层，与 path 段数同口径）。
        /// 返回错误描述或 null。
        /// </summary>
        static string CheckDataTree(JToken token, int depth, string pointer)
        {
            if (token is JObject o)
            {
                foreach (var prop in o.Properties())
                {
                    if (depth + 1 > MaxPathDepth)
                        return $"data model nesting exceeds max path depth {MaxPathDepth} at {pointer}/{prop.Name}";
                    if (prop.Name.Length > MaxKeyLength)
                        return $"data model key exceeds max key length {MaxKeyLength} at {pointer}/{prop.Name}";
                    var err = CheckDataTree(prop.Value, depth + 1, $"{pointer}/{prop.Name}");
                    if (err != null) return err;
                }
            }
            else if (token is JArray arr)
            {
                for (var i = 0; i < arr.Count; i++)
                {
                    if (depth + 1 > MaxPathDepth)
                        return $"data model nesting exceeds max path depth {MaxPathDepth} at {pointer}/{i}";
                    var err = CheckDataTree(arr[i], depth + 1, $"{pointer}/{i}");
                    if (err != null) return err;
                }
            }

            return null;
        }

        /// <summary>
        /// 递归检查 v0.8 contents 数据树（key/valueMap 模式包裹的裸数据）：
        /// 键长 ≤ MaxKeyLength、嵌套 ≤ MaxPathDepth 层。level = 该 entry 的 key 所在层号（顶层为 1）。
        /// 返回错误描述或 null。
        /// </summary>
        static string CheckContentsEntry(JObject entry, int level, string pointer)
        {
            var key = entry["key"]?.Value<string>();
            if (key != null && key.Length > MaxKeyLength)
                return $"data model key exceeds max key length {MaxKeyLength} (got {key.Length}) at {pointer}/key";

            if (entry["valueMap"] is JArray map)
            {
                if (level + 1 > MaxPathDepth)
                    return $"data model nesting exceeds max path depth {MaxPathDepth} at {pointer}/valueMap";
                for (var i = 0; i < map.Count; i++)
                {
                    if (map[i] is not JObject child) continue;
                    var err = CheckContentsEntry(child, level + 1, $"{pointer}/valueMap/{i}");
                    if (err != null) return err;
                }
            }

            return null;
        }

        /// <summary>
        /// surfaceId 生命周期跟踪：createSurface 登记、deleteSurface 释放。
        /// 返回首个违规（重复 createSurface 未删先建）描述或 null。
        /// </summary>
        static string ScanSurfaceLifecycle(List<JObject> msgs, HashSet<string> created)
        {
            foreach (var m in msgs)
            {
                var deleted = (m["deleteSurface"] as JObject)?["surfaceId"]?.Value<string>();
                if (!string.IsNullOrEmpty(deleted)) created.Remove(deleted);

                var createdId = (m["createSurface"] as JObject)?["surfaceId"]?.Value<string>();
                if (string.IsNullOrEmpty(createdId)) continue;
                if (!created.Add(createdId))
                    return $"duplicate createSurface for surfaceId '{createdId}' (delete it before recreating)";
            }

            return null;
        }
    }
}
