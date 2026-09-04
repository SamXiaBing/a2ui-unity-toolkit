using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using A2UISchemeA;

namespace A2UISchemeA.Tests
{
    /// <summary>
    /// 全矩阵布局回归：所有主题（A2uiThemeRegistry 自动发现，含 FigmaExport）× 所有 Samples。
    /// 每组合走 A2uiTestApi.Apply 完整热推链路，等两帧后做 worldBound 几何扫描断言零越界。
    /// expect-fail 样本（invalid_bad_packet / degrade_unknown）单独断言「拒收且保留」。
    /// 截图（可选，环境变量 A2UI_CAPTURE=1 开启）输出 TestResults/screenshots/。
    /// </summary>
    [TestFixture]
    public class A2uiLayoutRegressionTest
    {
        const string SamplesRoot = "Assets/A2UISchemeA/Samples";
        const string ResultsDir = "TestResults";

        static readonly string[] ExpectReject =
            { "bad_packet" };

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("A2UITestBed");
            yield return null; // 等场景加载 + Host OnEnable
            // Host 的 Boot 挂在 schedule.Execute(0) 上，再等一帧确保 rootVisualElement 就绪
            yield return null;
            Assert.IsNotNull(A2uiTestApi.Host, "测试床 Host 未就绪");
        }

        [UnityTest]
        [Timeout(1800000)] // 519 组合几何扫描约 15 分钟（batchmode 无显卡加速），截图另计
        public IEnumerator FullMatrix_NoTextOverflowsCard()
        {
            var themes = A2uiThemeRegistry.All();
            var samples = DiscoverSamples();
            TestContext.Out.WriteLine($"矩阵：{themes.Count} 主题 × {samples.Count} 样本 = {themes.Count * samples.Count} 组合");
            Assert.Greater(themes.Count, 0, "主题注册表为空");
            Assert.Greater(samples.Count, 0, "Samples 目录为空");

            Directory.CreateDirectory(ResultsDir);
            var summary = new StringBuilder();
            var failures = new List<string>();
            int combos = 0;

            foreach (var theme in themes)
            {
                foreach (var sample in samples)
                {
                    combos++;
                    var file = Path.GetFileNameWithoutExtension(sample);
                    var prompt = "# regression " + file;

                    string jsonl = File.ReadAllText(sample);
                    A2uiTestApi.Apply(theme.Key, prompt, jsonl);
                    // 等 FixTextMeasure 兜底收敛（2ms 起步退避重跑，实测第 4 帧起稳定），
                    // 2 帧会扫到尚未顶高的过渡态误报 VCRAMP
                    for (var i = 0; i < 10; i++)
                        yield return null;

                    if (ExpectReject.Contains(file))
                    {
                        // 坏包：卡片应不渲染或保持上一帧，且不抛异常（日志由 G0 断言覆盖）
                        TestContext.Out.WriteLine($"  [{theme.Key}] {file}: expect-reject OK");
                        continue;
                    }

                    var card = A2uiTestApi.GetCardRect();
                    if (card == null)
                    {
                        // 有些样本（如 degrade_unknown）渲染降级卡，仍应有卡片；真正无卡片的记失败
                        failures.Add($"[{theme.Key}] {file}: 无卡片渲染");
                        continue;
                    }

                    var issues = A2uiTestApi.ScanOverflow();
                    if (issues.Count > 0)
                    {
                        var head = string.Join(" | ", issues.Take(3));
                        failures.Add($"[{theme.Key}] {file}: {issues.Count} 处越界 → {head}");
                        TestContext.Out.WriteLine($"  FAIL [{theme.Key}] {file}: {issues.Count} 处");
                    }

                    // 落 layout 指标 JSON（供截图裁剪与趋势分析）
                    WriteLayoutJson(theme.Key, file, card.Value, issues);

                    // 截图（batchmode 下 CaptureScreenshotAsTexture 不产出；ReadPixels 需在
                    // 渲染帧内调用。本引擎 batchmode + WaitForEndOfFrame 协程会永久挂起——
                    // 这正是上一轮卡死的原因——改为每组合只用普通 yield 翻帧，截图用
                    // Camera.onPostRender 同步回调时机执行，确保在帧内）
                    if (System.Environment.GetEnvironmentVariable("A2UI_CAPTURE") == "1")
                    {
                        yield return CaptureViaPostRender(theme.Key, file);
                    }
                }
            }

            summary.AppendLine($"=== {combos} 组合完成，{failures.Count} 失败 ===");
            foreach (var f in failures) summary.AppendLine(f);
            File.WriteAllText(Path.Combine(ResultsDir, "layout_summary.txt"), summary.ToString());
            TestContext.Out.WriteLine(summary.ToString());

            Assert.Zero(failures.Count,
                "布局回归失败（明细见 TestResults/layout_summary.txt）：\n" +
                string.Join("\n", failures.Take(30)));
        }

        [UnityTest]
        public IEnumerator BadPacket_RejectedAndKeepsPreviousFrame()
        {
            // 先渲染一个正常样本
            var good = Path.Combine(SamplesRoot, "components", "text.v0.8.jsonl");
            A2uiTestApi.Apply("ds", "good", File.ReadAllText(good));
            yield return null;
            var cardBefore = A2uiTestApi.GetCardRect();
            Assert.IsNotNull(cardBefore, "前置样本必须已渲染");

            // 再推坏包（G0 拒收会打 LogError，属预期，向测试框架声明）
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*ApplyJsonl validation FAIL.*"));
            var bad = FindSample("bad_packet");
            A2uiTestApi.Apply("ds", "bad", File.ReadAllText(bad));
            yield return null;
            yield return null;

            var cardAfter = A2uiTestApi.GetCardRect();
            Assert.IsNotNull(cardAfter, "坏包必须被拒绝并保留上一帧（卡片不应消失）");
        }

        // —— 工具 ——

        static List<string> DiscoverSamples()
        {
            var root = Path.Combine(Application.dataPath, "..", SamplesRoot);
            var list = Directory.GetFiles(root, "*.jsonl", SearchOption.AllDirectories)
                .Where(p => !Path.GetFileName(p).StartsWith("bad_packet"))
                .OrderBy(p => p)
                .ToList();
            // bad_packet 单独在 expect-reject 用例处理，主矩阵跳过
            return list;
        }

        static string FindSample(string name)
        {
            var root = Path.Combine(Application.dataPath, "..", SamplesRoot);
            return Directory.GetFiles(root, name + "*.jsonl", SearchOption.AllDirectories).First();
        }

        static void WriteLayoutJson(string theme, string sample, Rect card, List<A2uiTestApi.OverflowIssue> issues)
        {
            // InvariantCulture：F1 在某些本地化下会输出非法 JSON（如逗号小数分隔符）
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string F(float v) => v.ToString("F1", inv);
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"theme\": \"" + theme + "\", \"sample\": \"" + sample + "\",");
            sb.AppendLine("  \"card\": {\"x\":" + F(card.x) + ",\"y\":" + F(card.y) +
                          ",\"w\":" + F(card.width) + ",\"h\":" + F(card.height) + "},");
            sb.AppendLine("  \"overflowCount\": " + issues.Count);
            sb.AppendLine("}");
            Directory.CreateDirectory(Path.Combine(ResultsDir, "layout"));
            File.WriteAllText(Path.Combine(ResultsDir, "layout", theme + "__" + sample + ".json"), sb.ToString());
        }

        /// <summary>
        /// 帧内截图：等一帧（普通 yield，batchmode 安全），再经 Camera.onPostRender
        /// 一次性回调在渲染帧内执行 ReadPixels。WaitForEndOfFrame 在本引擎 batchmode
        /// 下协程永不恢复，会导致整个测试挂死，禁止使用。
        /// </summary>
        static IEnumerator CaptureViaPostRender(string theme, string sample)
        {
            yield return null;
            var done = false;
            Camera cam = Camera.main;
            if (cam == null) yield break;
            void OnPost(Camera c)
            {
                if (done) return;
                done = true;
                Camera.onPostRender -= OnPost;
                var tex = A2uiTestApi.CaptureFrame();
                var dir = Path.Combine(ResultsDir, "screenshots", theme);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, sample + ".png"), tex.EncodeToPNG());
                Object.Destroy(tex);
            }
            Camera.onPostRender += OnPost;
            // 等最多 3 帧让回调命中
            for (int i = 0; i < 3 && !done; i++) yield return null;
            if (!done) Camera.onPostRender -= OnPost;
        }
    }
}
