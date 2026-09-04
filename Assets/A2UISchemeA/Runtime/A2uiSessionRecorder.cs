using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace A2UISchemeA
{
    /// <summary>
    /// G5：会话回放包——意图 / 报文 / action / 快照。
    /// </summary>
    public class A2uiSessionRecorder
    {
        readonly List<JObject> _events = new List<JObject>();
        string _sessionId;

        public string SessionId => _sessionId;

        public void Begin(string sessionId = null)
        {
            _sessionId = string.IsNullOrEmpty(sessionId)
                ? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss")
                : sessionId;
            _events.Clear();
            Record("session_begin", new JObject { ["sessionId"] = _sessionId });
        }

        public void RecordPrompt(string prompt) =>
            Record("prompt", new JObject { ["text"] = prompt ?? "" });

        public void RecordJsonl(string jsonl) =>
            Record("jsonl", new JObject { ["text"] = jsonl ?? "" });

        public void RecordValidation(bool ok, string error) =>
            Record("validation", new JObject { ["ok"] = ok, ["error"] = error ?? "" });

        public void RecordAction(string name, JObject context, string result) =>
            Record("action", new JObject
            {
                ["name"] = name ?? "",
                ["context"] = context ?? new JObject(),
                ["result"] = result ?? ""
            });

        public void RecordRender(string surfaceId, int componentCount) =>
            Record("render", new JObject
            {
                ["surfaceId"] = surfaceId ?? "",
                ["componentCount"] = componentCount
            });

        public void RecordGate(string status) =>
            Record("gate", new JObject { ["status"] = status ?? "" });

        public void RecordDegrade(string reason) =>
            Record("degrade", new JObject { ["reason"] = reason ?? "" });

        void Record(string type, JObject payload)
        {
            _events.Add(new JObject
            {
                ["ts"] = DateTime.UtcNow.ToString("o"),
                ["type"] = type,
                ["payload"] = payload
            });
        }

        public string ExportPath()
        {
            if (string.IsNullOrEmpty(_sessionId)) Begin();
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "A2UISchemeA", "sessions"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"session_{_sessionId}.json");
            var root = new JObject
            {
                ["sessionId"] = _sessionId,
                ["events"] = new JArray(_events)
            };
            File.WriteAllText(path, root.ToString(Formatting.Indented), Encoding.UTF8);
            Debug.Log($"[A2UISchemeA] session exported: {path}");
            return path;
        }

        public bool TryLoadForReplay(string path, out string jsonl, out string prompt)
        {
            jsonl = null;
            prompt = null;
            if (!File.Exists(path)) return false;
            var root = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
            if (root["events"] is not JArray events) return false;
            var sb = new StringBuilder();
            foreach (var e in events)
            {
                var type = e["type"]?.Value<string>();
                var payload = e["payload"] as JObject;
                if (type == "prompt") prompt = payload?["text"]?.Value<string>();
                if (type == "jsonl")
                {
                    var t = payload?["text"]?.Value<string>();
                    if (!string.IsNullOrEmpty(t)) sb.AppendLine(t.TrimEnd());
                }
            }

            jsonl = sb.ToString();
            return !string.IsNullOrWhiteSpace(jsonl);
        }
    }

    /// <summary>
    /// AG-UI 传输假适配：把 A2UI JSONL 包进自定义事件，量产时替换真实 SSE。
    /// </summary>
    public static class A2uiAgUiAdapter
    {
        public static JObject WrapJsonl(string jsonl, string prompt = null)
        {
            return new JObject
            {
                ["type"] = "A2UI_PAYLOAD",
                ["timestamp"] = DateTime.UtcNow.ToString("o"),
                ["prompt"] = prompt ?? "",
                ["a2uiJsonl"] = jsonl ?? ""
            };
        }

        public static bool TryUnwrap(string body, out string jsonl, out string prompt)
        {
            jsonl = null;
            prompt = null;
            if (string.IsNullOrWhiteSpace(body)) return false;
            var t = body.Trim();
            if (!t.StartsWith("{")) return false;
            try
            {
                var o = JObject.Parse(t);
                if (o["type"]?.Value<string>() != "A2UI_PAYLOAD") return false;
                jsonl = o["a2uiJsonl"]?.Value<string>();
                prompt = o["prompt"]?.Value<string>();
                return !string.IsNullOrWhiteSpace(jsonl);
            }
            catch
            {
                return false;
            }
        }
    }
}
