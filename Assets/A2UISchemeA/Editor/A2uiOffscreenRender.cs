using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA.Editor
{
    /// <summary>
    /// 离屏真渲染诊断：打开测试床 → Play → 推 JSONL → 等 UITK 渲染 →
    /// 把 panel 的 targetTexture 位图写 PNG。不依赖 GameView，batchmode 可用。
    ///
    /// batchmode: -executeMethod A2UISchemeA.Editor.A2uiOffscreenRender.Capture
    ///   （可选参数通过环境变量：A2UI_DIAG_SAMPLE / A2UI_DIAG_THEME / A2UI_DIAG_OUT）
    /// </summary>
    public static class A2uiOffscreenRender
    {
        public static void Capture()
        {
            var sample = Environment.GetEnvironmentVariable("A2UI_DIAG_SAMPLE")
                         ?? "00_full_control_center.v0.8";
            var theme = Environment.GetEnvironmentVariable("A2UI_DIAG_THEME") ?? "ds";
            var outPath = Environment.GetEnvironmentVariable("A2UI_DIAG_OUT")
                          ?? Path.Combine("TestResults", "offscreen", sample + ".png");

            _pending = new PendingCapture { Sample = sample, Theme = theme, Out = outPath };

            EditorSceneManager.OpenScene("Assets/Scenes/A2UITestBed.unity");
            var hostGo = GameObject.Find("A2uiSchemeAHost");
            if (hostGo == null) { Debug.LogError("[Offscreen] no host in scene"); EditorApplication.Exit(2); return; }

            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.isPlaying = true;
        }

        struct PendingCapture { public string Sample; public string Theme; public string Out; }
        static PendingCapture _pending;

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;

            // 等 10 帧让 Host 初始化，然后推 JSONL
            CoroutineRunner.Run(ApplyAfterFrames(_pending));
        }

        static IEnumerator ApplyAfterFrames(PendingCapture pc)
        {
            for (var i = 0; i < 10; i++) yield return null;

            var hostGo = GameObject.Find("A2uiSchemeAHost");
            var host = hostGo != null ? hostGo.GetComponent<A2uiSchemeAHost>() : null;
            if (host == null) { Debug.LogError("[Offscreen] host gone in playmode"); EditorApplication.Exit(3); yield break; }

            var mi = typeof(A2uiSchemeAHost).GetMethod("ApplyForTest",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var jsonlPath = Path.Combine(Application.dataPath, "A2UISchemeA/Samples/" + pc.Sample + ".jsonl");
            var jsonl = File.ReadAllText(jsonlPath);
            mi.Invoke(host, new object[] { pc.Theme, "# offscreen " + pc.Sample, jsonl });
            Debug.Log("[Offscreen] jsonl applied, waiting paint...");

            // 再等 15 帧完成 layout+paint
            for (var i = 0; i < 15; i++) yield return null;

            SavePanelBitmap(host, pc.Out);
        }

        static void SavePanelBitmap(A2uiSchemeAHost host, string outPath)
        {
            try
            {
                var doc = host.GetComponent<UIDocument>();
                var ps = doc.panelSettings;
                if (ps.targetTexture == null)
                {
                    // 强制离屏渲染到 RT（UITK 会把面板画进这张纹理）
                    ps.targetTexture = new RenderTexture(1920, 1080, 0, RenderTextureFormat.ARGB32);
                    Debug.Log("[Offscreen] assigned RT to PanelSettings, next frame readback");
                    // 下一帧再读（给 UITK 一帧时间把内容画进 RT）
                    CoroutineRunner.Run(ReadNextFrame(ps.targetTexture, outPath));
                    return;
                }
                ReadAndSave(ps.targetTexture, outPath);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[Offscreen] save failed: " + e.Message);
                EditorApplication.Exit(4);
            }
        }

        static IEnumerator ReadNextFrame(RenderTexture rt, string outPath)
        {
            yield return null;
            yield return null;
            ReadAndSave(rt, outPath);
            EditorApplication.Exit(0);
        }

        static void ReadAndSave(RenderTexture rt, string outPath)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)) ?? ".");
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
            Debug.Log("[Offscreen] saved: " + outPath + " (" + rt.width + "x" + rt.height + ")");
        }
    }

    /// <summary>batchmode 下驱动协程的最小宿主。</summary>
    public class CoroutineRunner : MonoBehaviour
    {
        static CoroutineRunner _inst;
        public static Coroutine Run(IEnumerator routine)
        {
            if (_inst == null)
            {
                var go = new GameObject("~CoroutineRunner");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<CoroutineRunner>();
            }
            return _inst.StartCoroutine(routine);
        }
    }
}
