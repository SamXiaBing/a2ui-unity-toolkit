using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace A2UISchemeA
{
    /// <summary>
    /// HTTP + 文件 inbox：把标准 A2UI v0.8 JSONL 热推到 Scheme A Host。
    /// </summary>
    public class A2uiSchemeACommandServer
    {
        public const int DefaultPort = 18766;

        readonly int _port;
        readonly Action<string, string> _onPayload;
        readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        HttpListener _listener;
        Thread _thread;
        volatile bool _running;
        string _inboxPath;
        DateTime _inboxStamp = DateTime.MinValue;

        public A2uiSchemeACommandServer(int port, Action<string, string> onPayload)
        {
            _port = port;
            _onPayload = onPayload;
            _inboxPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "A2UISchemeA", "inbox.jsonl"));
        }

        public string InboxPath => _inboxPath;
        public bool IsRunning => _running;

        public Action<string> OnTheme;

        public void Start()
        {
            if (_running) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_inboxPath) ?? ".");
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();
                _running = true;
                _thread = new Thread(ListenLoop) { IsBackground = true, Name = "A2uiSchemeACommandServer" };
                _thread.Start();
                Debug.Log($"[A2UISchemeA] HTTP http://127.0.0.1:{_port}/a2ui  inbox={_inboxPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[A2UISchemeA] HTTP start failed ({e.Message}). Use file inbox.");
                _running = false;
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* ignore */ }
            try { _listener?.Close(); } catch { /* ignore */ }
            _listener = null;
        }

        public void Pump()
        {
            while (_mainThread.TryDequeue(out var action))
            {
                try { action?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }

            PumpInbox();
        }

        void PumpInbox()
        {
            try
            {
                if (!File.Exists(_inboxPath)) return;
                var stamp = File.GetLastWriteTimeUtc(_inboxPath);
                if (stamp <= _inboxStamp) return;
                _inboxStamp = stamp;
                var text = File.ReadAllText(_inboxPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return;
                _onPayload?.Invoke(ExtractPrompt(text), StripMetaLines(text));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[A2UISchemeA] inbox read failed: {e.Message}");
            }
        }

        void ListenLoop()
        {
            while (_running && _listener != null)
            {
                HttpListenerContext ctx = null;
                try { ctx = _listener.GetContext(); }
                catch
                {
                    if (!_running) break;
                    continue;
                }

                try { Handle(ctx); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[A2UISchemeA] request error: {e.Message}");
                    try
                    {
                        ctx.Response.StatusCode = 500;
                        ctx.Response.Close();
                    }
                    catch { /* ignore */ }
                }
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            if (ctx.Request.HttpMethod == "GET" && (path == "" || path == "/health"))
            {
                WriteText(ctx, 200, "{\"ok\":true,\"service\":\"a2ui-scheme-a\"}");
                return;
            }

            if (ctx.Request.HttpMethod == "POST" && path == "/theme")
            {
                string body;
                using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                    body = reader.ReadToEnd();
                var theme = "a";
                try
                {
                    var jo = Newtonsoft.Json.Linq.JObject.Parse(body);
                    theme = jo["theme"]?.ToString() ?? "a";
                }
                catch { /* ignore */ }

                var t = theme;
                _mainThread.Enqueue(() => OnTheme?.Invoke(t));
                WriteText(ctx, 200, "{\"ok\":true,\"theme\":\"" + theme + "\"}");
                return;
            }

            if (ctx.Request.HttpMethod == "POST" && path == "/a2ui")
            {
                string body;
                using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
                    body = reader.ReadToEnd();

                var prompt = ctx.Request.Headers["X-A2UI-Prompt"] ?? ExtractPrompt(body);
                var jsonl = StripMetaLines(body);
                _mainThread.Enqueue(() => _onPayload?.Invoke(prompt, jsonl));
                WriteText(ctx, 200, "{\"ok\":true}");
                return;
            }

            WriteText(ctx, 404, "{\"ok\":false,\"error\":\"use POST /a2ui or /theme\"}");
        }

        public static string ExtractPrompt(string text)
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

        public static string StripMetaLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
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

        static void WriteText(HttpListenerContext ctx, int code, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
    }
}
