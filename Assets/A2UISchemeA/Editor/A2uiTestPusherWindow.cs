using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace A2UISchemeA.Editor
{
    /// <summary>
    /// A2UI 测试发送面板（不依赖 Play，但只有 Play 时 Host 才会消费）。
    /// 常见测试项 = Samples/ 下所有 *.jsonl，点击即模拟发送：
    ///   - HTTP POST http://127.0.0.1:18766/a2ui（端口活时优先）
    ///   - 失败则写 Temp/A2UISchemeA/inbox.jsonl（Host 每帧轮询）
    /// 另提供：自定义 JSONL 粘贴、主题热切（POST /theme）。
    /// 菜单：A2UI Scheme A → 测试发送面板
    /// </summary>
    public class A2uiTestPusherWindow : EditorWindow
    {
        const string SamplesDir = "Assets/A2UISchemeA/Samples";
        const int Port = 18766;

        List<string> _jsonlFiles = new List<string>();
        Dictionary<string, string> _promptCache = new Dictionary<string, string>();
        // 协议版本（内容探测）与按目录分组，用于「常见测试项」的折叠分组 + v0.8/v0.9 徽标
        Dictionary<string, bool> _isV09 = new Dictionary<string, bool>();
        Dictionary<string, List<string>> _groups = new Dictionary<string, List<string>>();
        Dictionary<string, bool> _fold = new Dictionary<string, bool>();
        Vector2 _mainScroll;
        Vector2 _listScroll;
        Vector2 _logScroll;
        string _log = "";

        // 分组展示顺序；默认收起体量大的单元/时间轴组
        static readonly string[] GroupOrder = { "demos", "scenarios", "features", "edge", "timeline_bench", "components" };
        static readonly HashSet<string> DefaultCollapsed = new HashSet<string> { "components", "timeline_bench" };
        static GUIStyle _v09Badge, _v08Badge;
        static void EnsureBadgeStyles()
        {
            if (_v09Badge == null)
                _v09Badge = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.42f, 0.78f, 0.46f) } };
            if (_v08Badge == null)
                _v08Badge = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.55f, 0.55f, 0.55f) } };
        }

        string _customPrompt = "";
        string _customJsonl = "";
        bool _useHttp = true;
        bool _httpFallbackInbox = true;
        string _selectedFile = "";
        DateTime _lastInboxStamp = DateTime.MinValue;

        [MenuItem("A2UI Scheme A/测试发送面板")]
        public static A2uiTestPusherWindow Open()
        {
            var w = GetWindow<A2uiTestPusherWindow>("A2UI 测试发送");
            w.minSize = new Vector2(560, 320);
            return w;
        }

        void OnEnable()
        {
            RefreshFiles();
        }

        void RefreshFiles()
        {
            _jsonlFiles.Clear();
            _promptCache.Clear();
            _isV09.Clear();
            var root = Path.Combine(Application.dataPath, "A2UISchemeA", "Samples");
            if (!Directory.Exists(root))
            {
                Log("Samples 目录不存在: " + root);
                return;
            }

            foreach (var f in Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories))
            {
                var rel = "Assets" + f.Substring(Application.dataPath.Length).Replace('\\', '/');
                var text = File.ReadAllText(f, Encoding.UTF8);
                _jsonlFiles.Add(rel);
                _promptCache[rel] = ReadPromptFromText(text);
                _isV09[rel] = DetectV09(text);
            }

            _jsonlFiles.Sort();
            BuildGroups();
            Log($"已扫描 {_jsonlFiles.Count} 个样例（v0.9: {_isV09.Count(kv => kv.Value)} · v0.8: {_isV09.Count(kv => !kv.Value)}）");
        }

        /// <summary>按 Samples 下的第一级子目录分组；顺序按 GroupOrder，未列出的目录跟在后面。</summary>
        void BuildGroups()
        {
            _groups.Clear();
            foreach (var rel in _jsonlFiles)
            {
                var relToSamples = rel.Substring("Assets/A2UISchemeA/Samples/".Length);
                var slash = relToSamples.IndexOf('/');
                var group = slash > 0 ? relToSamples.Substring(0, slash) : "(root)";
                if (!_groups.TryGetValue(group, out var list))
                {
                    list = new List<string>();
                    _groups[group] = list;
                    if (!_fold.ContainsKey(group))
                        _fold[group] = !DefaultCollapsed.Contains(group);
                }
                list.Add(rel);
            }
        }

        /// <summary>协议版本探测：v0.9 平铺消息（createSurface/updateComponents/updateDataModel）优先，其次 v0.8 特征。</summary>
        static bool DetectV09(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                if (t.Contains("\"createSurface\"") || t.Contains("\"updateComponents\"") || t.Contains("\"updateDataModel\""))
                    return true;
                if (t.Contains("\"surfaceUpdate\"") || t.Contains("\"beginRendering\""))
                    return false;
            }
            return false;
        }

        void OnGUI()
        {
            using (var scope = new EditorGUILayout.ScrollViewScope(_mainScroll))
            {
                _mainScroll = scope.scrollPosition;
                EditorGUILayout.Space(4);
                DrawHeader();
                DrawTheme();
                DrawSamples();
                DrawCustom();
                DrawLog();
            }
        }

        void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("刷新样例列表", GUILayout.Width(120))) RefreshFiles();
                if (GUILayout.Button("打开 Samples 目录", GUILayout.Width(140)))
                    EditorUtility.RevealInFinder(Path.Combine(Application.dataPath, "A2UISchemeA", "Samples"));

                GUILayout.FlexibleSpace();
                var playing = EditorApplication.isPlaying;
                var color = playing ? Color.green : Color.yellow;
                var label = playing ? "● Play 中" : "○ 未 Play（Host 不在跑，发送不生效）";
                var old = GUI.color;
                GUI.color = color;
                GUILayout.Label(label);
                GUI.color = old;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _useHttp = EditorGUILayout.ToggleLeft("HTTP 发送（18766）", _useHttp, GUILayout.Width(140));
                if (_useHttp)
                    _httpFallbackInbox = EditorGUILayout.ToggleLeft("失败回退 inbox 文件", _httpFallbackInbox);
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.Space(2);
        }

        void DrawSamples()
        {
            EditorGUILayout.LabelField("常见测试项（点击即发送）", EditorStyles.boldLabel);
            EnsureBadgeStyles();
            using (var scope = new EditorGUILayout.ScrollViewScope(_listScroll, GUILayout.Height(220)))
            {
                _listScroll = scope.scrollPosition;

                foreach (var g in GroupOrder)
                    DrawGroup(g);
                foreach (var kv in _groups)
                    if (!GroupOrder.Contains(kv.Key))
                        DrawGroup(kv.Key);
            }
            EditorGUILayout.Space(6);
        }

        void DrawGroup(string group)
        {
            if (!_groups.TryGetValue(group, out var items)) return;
            _fold.TryGetValue(group, out var open);
            var newOpen = EditorGUILayout.Foldout(open, $"{group}  ({items.Count})", true);
            if (newOpen != open) _fold[group] = newOpen;
            if (!newOpen) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (var rel in items)
                    DrawSampleRow(rel);
            }
        }

        void DrawSampleRow(string rel)
        {
            var name = Path.GetFileName(rel);
            var prompt = _promptCache.TryGetValue(rel, out var p) ? p : "";
            var isV09 = _isV09.TryGetValue(rel, out var v9) && v9;
            using (new EditorGUILayout.HorizontalScope())
            {
                var selected = GUILayout.Toggle(_selectedFile == rel, "", GUILayout.Width(16));
                if (selected && _selectedFile != rel) { _selectedFile = rel; FillCustomFrom(rel); }

                GUILayout.Label(isV09 ? "v0.9" : "v0.8", isV09 ? _v09Badge : _v08Badge, GUILayout.Width(28));
                GUILayout.Label(name, GUILayout.MinWidth(120), GUILayout.MaxWidth(250));
                var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.6f, 0.6f, 0.6f) } };
                GUILayout.Label(string.IsNullOrEmpty(prompt) ? "(无 prompt)" : prompt, style, GUILayout.MinWidth(60));
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("发送", GUILayout.Width(52)))
                    SendFile(rel);
            }
        }

        void DrawCustom()
        {
            EditorGUILayout.LabelField("自定义 JSONL", EditorStyles.boldLabel);
            _customPrompt = EditorGUILayout.TextField("Prompt（可选）", _customPrompt);

            var h = EditorStyles.textArea.CalcHeight(new GUIContent(_customJsonl), EditorGUIUtility.currentViewWidth - 30);
            _customJsonl = EditorGUILayout.TextArea(
                _customJsonl,
                GUILayout.Height(Mathf.Clamp(h + 16, 60, 180)));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("从选中样例填入", GUILayout.Width(140))) FillCustomFrom(_selectedFile);
                if (GUILayout.Button("发送自定义 JSONL", GUILayout.Width(150))) SendText(_customPrompt, _customJsonl);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("清空", GUILayout.Width(60))) { _customJsonl = ""; _customPrompt = ""; }
            }
            EditorGUILayout.Space(6);
        }

        int _themeIndex;
        string[] _themeLabels;
        string[] _themeKeys;

        void DrawTheme()
        {
            EditorGUILayout.LabelField("主题热切（POST /theme）", EditorStyles.boldLabel);
            var entries = A2uiThemeRegistry.All();
            if (_themeLabels == null || _themeLabels.Length != entries.Count)
            {
                _themeLabels = entries.Select(e => e.Label).ToArray();
                _themeKeys = entries.Select(e => e.Key).ToArray();
                _themeIndex = System.Math.Max(0, System.Array.IndexOf(_themeKeys, "ds"));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("主题：", GUILayout.Width(48));
                int sel = EditorGUILayout.Popup(_themeIndex, _themeLabels, GUILayout.Width(220));
                if (sel != _themeIndex)
                {
                    _themeIndex = sel;
                    SendTheme(_themeKeys[sel]);
                }
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.Space(6);
        }

        void DrawLog()
        {
            EditorGUILayout.LabelField("日志", EditorStyles.boldLabel);
            using (var scope = new EditorGUILayout.ScrollViewScope(_logScroll, GUILayout.Height(120)))
            {
                _logScroll = scope.scrollPosition;
                EditorGUILayout.SelectableLabel(_log, EditorStyles.textArea, GUILayout.ExpandHeight(true));
            }
        }

        // ---------- 发送 ----------

        void SendFile(string rel)
        {
            var full = Path.Combine(Application.dataPath, rel.Substring("Assets/".Length));
            if (!File.Exists(full))
            {
                Log($"文件不存在: {rel}");
                return;
            }

            var text = File.ReadAllText(full, Encoding.UTF8);
            var prompt = ReadPrompt(text);
            var jsonl = StripMetaLines(text);
            if (string.IsNullOrEmpty(jsonl.Trim()))
            {
                Log($"文件为空或只有注释: {rel}");
                return;
            }
            DoSend(prompt, jsonl, rel);
        }

        void SendText(string prompt, string jsonlText)
        {
            var jsonl = StripMetaLines(jsonlText);
            if (string.IsNullOrEmpty(jsonl.Trim()))
            {
                Log("自定义 JSONL 为空，未发送");
                return;
            }
            DoSend(prompt, jsonl, "自定义");
        }

        void DoSend(string prompt, string jsonl, string source)
        {
            Log($"── 发送 [{source}] prompt={prompt ?? "(无)"} · jsonl={jsonl.Length} 字符");

            if (_useHttp)
            {
                var ok = TryPostHttp(prompt, jsonl);
                if (ok)
                {
                    Log("HTTP OK → 127.0.0.1:18766/a2ui");
                    return;
                }
                if (!_httpFallbackInbox)
                {
                    Log("HTTP 失败，且未勾选回退 inbox，已放弃");
                    return;
                }
            }

            var path = WriteInbox(prompt, jsonl);
            Log($"已写 inbox: {path}\n（Host 每帧轮询，Play 模式下一两帧内触发）");
        }

        bool TryPostHttp(string prompt, string jsonl)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create($"http://127.0.0.1:{Port}/a2ui");
                req.Method = "POST";
                req.ContentType = "application/jsonl; charset=utf-8";
                req.Timeout = 3000;
                var bodyText = (string.IsNullOrEmpty(prompt) ? "" : "# prompt: " + prompt + "\n") + jsonl;
                var body = Encoding.UTF8.GetBytes(bodyText);
                req.ContentLength = body.Length;
                using (var s = req.GetRequestStream())
                    s.Write(body, 0, body.Length);

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
                {
                    var reply = reader.ReadToEnd();
                    return resp.StatusCode == HttpStatusCode.OK && reply.Contains("\"ok\":true");
                }
            }
            catch (Exception e)
            {
                Log($"HTTP 失败: {e.Message}");
                return false;
            }
        }

        string WriteInbox(string prompt, string jsonl)
        {
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "A2UISchemeA"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "inbox.jsonl");

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(prompt))
                sb.Append("# prompt: ").AppendLine(prompt);
            sb.AppendLine(jsonl.TrimEnd('\r', '\n'));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

            // Host 按文件最后修改时间判断是否有新内容；同一 tick 内连续写会被跳过，强制推进时间戳。
            var stamp = DateTime.UtcNow;
            if (stamp <= _lastInboxStamp)
                stamp = _lastInboxStamp.AddMilliseconds(1);
            _lastInboxStamp = stamp;
            File.SetLastWriteTimeUtc(path, stamp);
            return path;
        }

        void SendTheme(string theme)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create($"http://127.0.0.1:{Port}/theme");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 3000;
                var body = Encoding.UTF8.GetBytes("{\"theme\":\"" + theme + "\"}");
                req.ContentLength = body.Length;
                using (var s = req.GetRequestStream())
                    s.Write(body, 0, body.Length);

                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream() ?? Stream.Null, Encoding.UTF8))
                {
                    var reply = reader.ReadToEnd();
                    Log($"主题 {theme} → {(resp.StatusCode == HttpStatusCode.OK && reply.Contains("\"ok\":true") ? "OK" : reply)}");
                }
            }
            catch (Exception e)
            {
                Log($"主题热切失败（需 Play 且 HTTP 端口活）: {e.Message}");
            }
        }

        // ---------- 工具 ----------

        static string ReadPrompt(string relOrText)
        {
            string text;
            if (relOrText.StartsWith("Assets/") || relOrText.StartsWith("Assets\\"))
            {
                var full = Path.Combine(Application.dataPath, relOrText.Substring("Assets/".Length));
                if (!File.Exists(full)) return "";
                text = File.ReadAllText(full, Encoding.UTF8);
            }
            else
            {
                text = relOrText;
            }
            return ReadPromptFromText(text);
        }

        static string ReadPromptFromText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var t = line.Trim();
                if (t.StartsWith("# prompt:", StringComparison.OrdinalIgnoreCase))
                    return t.Substring("# prompt:".Length).Trim();
                if (t.StartsWith("#prompt:", StringComparison.OrdinalIgnoreCase))
                    return t.Substring("#prompt:".Length).Trim();
            }
            return "";
        }

        static string StripMetaLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder();
            using var reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var t = line.Trim();
                if (t.StartsWith("# prompt:", StringComparison.OrdinalIgnoreCase)) continue;
                if (t.StartsWith("#prompt:", StringComparison.OrdinalIgnoreCase)) continue;
                sb.AppendLine(line);
            }
            return sb.ToString();
        }

        void FillCustomFrom(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return;
            var full = Path.Combine(Application.dataPath, rel.Substring("Assets/".Length));
            if (!File.Exists(full)) return;
            _customJsonl = File.ReadAllText(full, Encoding.UTF8);
            _customPrompt = ReadPromptFromText(_customJsonl);
        }

        void Log(string msg)
        {
            _log = _log.Length > 8000 ? _log.Substring(_log.Length - 8000) : _log;
            _log += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            _logScroll.y = float.MaxValue;
            Debug.Log("[A2UITestPusher] " + msg);
        }
    }
}
