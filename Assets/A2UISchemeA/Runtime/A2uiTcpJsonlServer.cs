using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace A2UISchemeA
{
    /// <summary>
    /// 台架可靠通道：TCP 127.0.0.1:port 收 JSONL（adb forward 友好）。
    /// 帧：可选 # prompt / # theme 行 + JSONL，以空行或连接关闭结束。
    /// </summary>
    public class A2uiTcpJsonlServer
    {
        readonly int _port;
        readonly Action<string, string> _onPayload;
        readonly Action<string> _onTheme;
        readonly ConcurrentQueue<Action> _mainThread = new ConcurrentQueue<Action>();
        TcpListener _listener;
        Thread _thread;
        volatile bool _running;

        public A2uiTcpJsonlServer(int port, Action<string, string> onPayload, Action<string> onTheme = null)
        {
            _port = port;
            _onPayload = onPayload;
            _onTheme = onTheme;
        }

        public void Start()
        {
            if (_running) return;
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                _running = true;
                _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "A2uiTcpJsonlServer" };
                _thread.Start();
                Debug.Log($"[A2UISchemeA] TCP 127.0.0.1:{_port} (adb forward ready)");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[A2UISchemeA] TCP start failed ({e.Message}). HTTP/inbox may still work.");
                _running = false;
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* ignore */ }
            _listener = null;
        }

        public void Pump()
        {
            while (_mainThread.TryDequeue(out var action))
            {
                try { action?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        void AcceptLoop()
        {
            while (_running && _listener != null)
            {
                TcpClient client = null;
                try { client = _listener.AcceptTcpClient(); }
                catch
                {
                    if (!_running) break;
                    continue;
                }

                try { HandleClient(client); }
                catch (Exception e)
                {
                    Debug.LogWarning("[A2UISchemeA] TCP client error: " + e.Message);
                }
                finally
                {
                    try { client?.Close(); } catch { /* ignore */ }
                }
            }
        }

        void HandleClient(TcpClient client)
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var sb = new StringBuilder();
            string prompt = "";
            string theme = null;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var t = line.Trim();
                if (t.Length == 0)
                {
                    if (sb.Length > 0) break;
                    continue;
                }

                if (t.StartsWith("# theme:", StringComparison.OrdinalIgnoreCase))
                {
                    theme = t.Substring("# theme:".Length).Trim();
                    continue;
                }

                if (t.StartsWith("#theme:", StringComparison.OrdinalIgnoreCase))
                {
                    theme = t.Substring("#theme:".Length).Trim();
                    continue;
                }

                if (t.StartsWith("# prompt:", StringComparison.OrdinalIgnoreCase))
                {
                    prompt = t.Substring("# prompt:".Length).Trim();
                    continue;
                }

                if (t.StartsWith("#prompt:", StringComparison.OrdinalIgnoreCase))
                {
                    prompt = t.Substring("#prompt:".Length).Trim();
                    continue;
                }

                sb.AppendLine(line);
            }

            var jsonl = sb.ToString();
            var themeCopy = theme;
            var promptCopy = prompt;
            var body = jsonl;
            _mainThread.Enqueue(() =>
            {
                if (!string.IsNullOrEmpty(themeCopy))
                    _onTheme?.Invoke(themeCopy);
                if (!string.IsNullOrWhiteSpace(body))
                    _onPayload?.Invoke(promptCopy, A2uiSchemeACommandServer.StripMetaLines(body));
            });

            var ack = Encoding.UTF8.GetBytes("{\"ok\":true,\"via\":\"tcp\"}");
            try
            {
                stream.Write(ack, 0, ack.Length);
            }
            catch { /* ignore */ }
        }
    }
}
