using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace A2UISchemeA
{
    /// <summary>
    /// A2UI v0.8 消息处理器：surfaceUpdate / dataModelUpdate / beginRendering / deleteSurface。
    /// </summary>
    public class A2uiV08SurfaceState
    {
        public string SurfaceId;
        public string RootId;
        public string CatalogId;
        public readonly Dictionary<string, JObject> Components = new Dictionary<string, JObject>();
        public JObject DataModel = new JObject();
        public bool ReadyToRender;
    }

    public class A2uiV08Processor
    {
        readonly Dictionary<string, A2uiV08SurfaceState> _surfaces = new Dictionary<string, A2uiV08SurfaceState>();

        public IReadOnlyDictionary<string, A2uiV08SurfaceState> Surfaces => _surfaces;

        public event Action<string> SurfaceReady;
        public event Action<string> SurfaceDeleted;
        public event Action<string> SurfaceDataChanged;

        public void Clear()
        {
            _surfaces.Clear();
        }

        public void IngestJsonlFile(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException(path);
            IngestJsonlText(File.ReadAllText(path, Encoding.UTF8));
        }

        public void IngestJsonlText(string text)
        {
            using var reader = new StringReader(text ?? "");
            string line;
            var n = 0;
            while ((line = reader.ReadLine()) != null)
            {
                n++;
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                try
                {
                    IngestMessage(JObject.Parse(t));
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException($"A2UI JSONL line {n}: {e.Message}", e);
                }
            }
        }

        public void IngestMessage(JObject msg)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));

            // v0.9 双栈：createSurface/updateComponents 等平铺格式
            // 先归一化为 v0.8 内部格式，再走同一条处理管线
            if (A2uiV09Normalizer.IsV09(msg))
            {
                var normalized = A2uiV09Normalizer.Normalize(msg);
                foreach (var m in normalized)
                    IngestV08Message(m);
                return;
            }

            IngestV08Message(msg);
        }

        void IngestV08Message(JObject msg)
        {
            if (msg["surfaceUpdate"] != null)
            {
                HandleSurfaceUpdate((JObject)msg["surfaceUpdate"]);
                return;
            }

            if (msg["dataModelUpdate"] != null)
            {
                HandleDataModelUpdate((JObject)msg["dataModelUpdate"]);
                return;
            }

            if (msg["beginRendering"] != null)
            {
                HandleBeginRendering((JObject)msg["beginRendering"]);
                return;
            }

            if (msg["deleteSurface"] != null)
            {
                HandleDeleteSurface((JObject)msg["deleteSurface"]);
                return;
            }

            throw new InvalidOperationException(
                "Message must contain exactly one of: beginRendering, surfaceUpdate, dataModelUpdate, deleteSurface");
        }

        void HandleSurfaceUpdate(JObject body)
        {
            var surfaceId = body["surfaceId"]?.Value<string>()
                            ?? throw new InvalidOperationException("surfaceUpdate.surfaceId required");
            var components = body["components"] as JArray
                             ?? throw new InvalidOperationException("surfaceUpdate.components required");

            var state = GetOrCreate(surfaceId);
            foreach (var token in components)
            {
                if (token is not JObject comp) continue;
                var id = comp["id"]?.Value<string>();
                if (string.IsNullOrEmpty(id)) continue;
                if (comp["component"] == null)
                    throw new InvalidOperationException($"component '{id}' missing component wrapper");
                state.Components[id] = (JObject)comp.DeepClone();
            }
        }

        void HandleDataModelUpdate(JObject body)
        {
            var surfaceId = body["surfaceId"]?.Value<string>()
                            ?? throw new InvalidOperationException("dataModelUpdate.surfaceId required");
            var contents = body["contents"] as JArray
                           ?? throw new InvalidOperationException("dataModelUpdate.contents required");
            var path = body["path"]?.Value<string>();
            var state = GetOrCreate(surfaceId);

            var patch = ContentsToJToken(contents);
            if (string.IsNullOrEmpty(path) || path == "/")
                state.DataModel = patch as JObject ?? new JObject { ["_root"] = patch };
            else
                SetByPath(state.DataModel, path, patch);

            if (state.ReadyToRender)
                SurfaceDataChanged?.Invoke(surfaceId);
        }

        void HandleBeginRendering(JObject body)
        {
            var surfaceId = body["surfaceId"]?.Value<string>()
                            ?? throw new InvalidOperationException("beginRendering.surfaceId required");
            var root = body["root"]?.Value<string>()
                       ?? throw new InvalidOperationException("beginRendering.root required");
            var state = GetOrCreate(surfaceId);
            state.RootId = root;
            state.CatalogId = body["catalogId"]?.Value<string>();
            state.ReadyToRender = true;
            SurfaceReady?.Invoke(surfaceId);
        }

        void HandleDeleteSurface(JObject body)
        {
            var surfaceId = body["surfaceId"]?.Value<string>()
                            ?? throw new InvalidOperationException("deleteSurface.surfaceId required");
            if (_surfaces.Remove(surfaceId))
                SurfaceDeleted?.Invoke(surfaceId);
        }

        A2uiV08SurfaceState GetOrCreate(string surfaceId)
        {
            if (!_surfaces.TryGetValue(surfaceId, out var state))
            {
                state = new A2uiV08SurfaceState { SurfaceId = surfaceId };
                _surfaces[surfaceId] = state;
            }

            return state;
        }

        public static JToken ContentsToJToken(JArray contents)
        {
            var obj = new JObject();
            foreach (var token in contents)
            {
                if (token is not JObject entry) continue;
                var key = entry["key"]?.Value<string>();
                if (string.IsNullOrEmpty(key)) continue;
                obj[key] = ReadTypedValue(entry);
            }

            return obj;
        }

        public static JToken ReadTypedValue(JObject entry)
        {
            if (entry["valueString"] != null) return entry["valueString"].Value<string>();
            if (entry["valueNumber"] != null) return entry["valueNumber"].Value<double>();
            if (entry["valueBoolean"] != null) return entry["valueBoolean"].Value<bool>();
            if (entry["valueMap"] is JArray map)
                return ContentsToJToken(map);
            return JValue.CreateNull();
        }

        public static JToken ResolveBound(JToken bound, JObject dataModel)
        {
            if (bound == null) return null;
            if (bound.Type != JTokenType.Object) return bound;
            var o = (JObject)bound;
            if (o["literalString"] != null) return o["literalString"].Value<string>();
            if (o["literalNumber"] != null) return o["literalNumber"].Value<double>();
            if (o["literalBoolean"] != null) return o["literalBoolean"].Value<bool>();
            if (o["literalArray"] is JArray arr) return arr;
            if (o["path"] != null)
                return GetByPath(dataModel, o["path"].Value<string>());
            return null;
        }

        public static JToken GetByPath(JObject root, string path)
        {
            return GetByPathFromToken(root, path);
        }

        /// <summary>
        /// 支持绝对 path（以 / 开头时由调用方剥层）与相对 path；可走数组下标。
        /// </summary>
        public static JToken GetByPathFromToken(JToken root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;
            if (path == "/" || path == ".") return root;
            var p = path.StartsWith("/") ? path.Substring(1) : path;
            if (string.IsNullOrEmpty(p)) return root;
            JToken cur = root;
            foreach (var part in p.Split('/'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                if (cur is JObject jo)
                {
                    if (jo[part] == null) return null;
                    cur = jo[part];
                }
                else if (cur is JArray arr && int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx))
                {
                    if (idx < 0 || idx >= arr.Count) return null;
                    cur = arr[idx];
                }
                else
                {
                    return null;
                }
            }

            return cur;
        }

        public static void SetByPath(JObject root, string path, JToken value)
        {
            var p = path.StartsWith("/") ? path.Substring(1) : path;
            var parts = p.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                if (value is JObject vo)
                {
                    root.RemoveAll();
                    foreach (var prop in vo.Properties())
                        root[prop.Name] = prop.Value;
                }

                return;
            }

            JObject cur = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (cur[parts[i]] is not JObject next)
                {
                    next = new JObject();
                    cur[parts[i]] = next;
                }

                cur = next;
            }

            cur[parts[^1]] = value;
        }

        /// <summary>从 component 包装对象取出唯一类型名与属性。</summary>
        public static bool TryGetComponentType(JObject componentDef, out string typeName, out JObject props)
        {
            typeName = null;
            props = null;
            var wrapper = componentDef["component"] as JObject;
            if (wrapper == null) return false;
            foreach (var prop in wrapper.Properties())
            {
                typeName = prop.Name;
                props = prop.Value as JObject ?? new JObject();
                return true;
            }

            return false;
        }
    }
}
