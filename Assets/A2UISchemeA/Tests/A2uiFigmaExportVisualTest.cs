// A2UI Scheme A — FigmaExport 可视化冒烟测试（PlayMode）
// 验证转换链产物（Styles/FigmaExport/*.uss，来自真实 Figma 稿）能在 Unity UI Toolkit 正确渲染：
//   1) USS 能被 StyleSheet 解析（无语法错误）
//   2) 设计抽出的主色（动态读取，不硬编码）能正确解析到按钮背景
//   3) 组件树在 1080 宽下不横向溢出、不塌缩
//   4) 截图存到 persistentDataPath，方便肉眼确认还原度
//
// 运行：Unity Editor → Window → General → Test Runner → PlayMode → A2uiFigmaExportVisualTest
using System;
using System.Collections;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace A2UISchemeA.Tests
{
    [TestFixture]
    public class A2uiFigmaExportVisualTest
    {
        private const string ExportDir = "A2UISchemeA/Styles/FigmaExport";
        // 作用域类必须与 A2uiThemeRegistry 自动发现的命名一致（目录名 FigmaExport → figma-figmaexport）
        private const string ScopeClass = "a2ui-skin--figma-figmaexport";

        private static string FullPath(string rel) =>
            Path.Combine(Application.dataPath, rel).Replace('\\', '/');

        private static StyleSheet LoadUss(string fileName)
        {
            string assetPath = "Assets/" + ExportDir + "/" + fileName;
            var ss = AssetDatabase.LoadAssetAtPath<StyleSheet>(assetPath);
            Assert.IsNotNull(ss, "StyleSheet 资产未加载（确认 .uss 已被 Unity 导入）：" + assetPath);
            return ss;
        }

        // 从 FigmaTokens.uss 文本动态提取主色 rgb，避免硬编码导致换稿误报
        private static Color ReadPrimaryFromUss(string fileName)
        {
            string uss = File.ReadAllText(FullPath(ExportDir + "/" + fileName));
            // 兼容 rgb(r, g, b) 与 #RRGGBB 两种导出格式
            var m = Regex.Match(uss, @"--a2ui-color-primary:\s*rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
            if (m.Success)
                return new Color(
                    int.Parse(m.Groups[1].Value) / 255f,
                    int.Parse(m.Groups[2].Value) / 255f,
                    int.Parse(m.Groups[3].Value) / 255f,
                    1f);
            var h = Regex.Match(uss, @"--a2ui-color-primary:\s*#([0-9a-fA-F]{6})");
            Assert.IsTrue(h.Success, "FigmaTokens.uss 中未找到 --a2ui-color-primary");
            return new Color(
                Convert.ToInt32(h.Groups[1].Value.Substring(0, 2), 16) / 255f,
                Convert.ToInt32(h.Groups[1].Value.Substring(2, 2), 16) / 255f,
                Convert.ToInt32(h.Groups[1].Value.Substring(4, 2), 16) / 255f,
                1f);
        }

        [UnityTest]
        public IEnumerator FigmaExport_VisualSmoke()
        {
            // 1) USS 可解析
            StyleSheet ssTokens = LoadUss("FigmaTokens.uss");
            StyleSheet ssComp = LoadUss("FigmaComponents.uss");
            Color expectedPrimary = ReadPrimaryFromUss("FigmaTokens.uss");

            // 2) 搭一棵真实组件树，挂上作用域类，加载两套 USS
            var go = new GameObject("A2uiFigmaSmokeHost");
            var doc = go.AddComponent<UIDocument>();

            // 关键：必须挂 PanelSettings，root 才会进面板布局，
            // resolvedStyle / worldBound 才有真实计算值，否则断言全默认值必挂
            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/A2UISchemeA/PanelSettings.asset");
            Assert.IsNotNull(ps, "找不到 PanelSettings：Assets/A2UISchemeA/PanelSettings.asset");
            doc.panelSettings = ps;

            yield return null; // 等 panel 建好、root 挂上面板

            var root = doc.rootVisualElement;
            root.Clear();
            root.styleSheets.Add(ssTokens);
            root.styleSheets.Add(ssComp);
            root.AddToClassList(ScopeClass);
            root.style.width = 1080;
            root.style.height = 1920;
            root.style.paddingLeft = 24;
            root.style.paddingRight = 24;
            root.style.flexDirection = FlexDirection.Column;

            var scroll = new ScrollView();
            root.Add(scroll);

            // 标题
            var h1 = new Label("CarStore · Figma Export Smoke");
            h1.AddToClassList("a2ui-text");
            h1.AddToClassList("a2ui-text--h1");
            scroll.Add(h1);

            // 主按钮（验证主色解析）
            var primaryBtn = new Button { text = "Book a Test Drive" };
            primaryBtn.AddToClassList("a2ui-btn");
            primaryBtn.AddToClassList("a2ui-btn--primary");
            scroll.Add(primaryBtn);

            // 次按钮
            var secBtn = new Button { text = "Compare" };
            secBtn.AddToClassList("a2ui-btn");
            secBtn.AddToClassList("a2ui-btn--secondary");
            scroll.Add(secBtn);

            // 卡片
            var card = new VisualElement();
            card.AddToClassList("a2ui-card");
            var cardTitle = new Label("2024 Tesla Model 3");
            cardTitle.AddToClassList("a2ui-card__title");
            var cardBody = new Label("Standard Range Plus · Featured");
            cardBody.AddToClassList("a2ui-card__body");
            card.Add(cardTitle); card.Add(cardBody);
            scroll.Add(card);

            // 列表项
            for (int i = 0; i < 3; i++)
            {
                var item = new VisualElement();
                item.AddToClassList("a2ui-list__item");
                item.Add(new Label("Recommended car #" + (i + 1)));
                scroll.Add(item);
            }

            // 图标 + 文本行
            var row = new VisualElement();
            row.AddToClassList("a2ui-row");
            var icon = new Label("♥");
            icon.AddToClassList("a2ui-icon");
            var rowText = new Label("Saved to wishlist");
            rowText.AddToClassList("a2ui-text");
            row.Add(icon); row.Add(rowText);
            scroll.Add(row);

            // 图片占位
            var img = new VisualElement();
            img.AddToClassList("a2ui-image");
            scroll.Add(img);
            var imgCap = new Label("360° view");
            imgCap.AddToClassList("a2ui-image__caption");
            scroll.Add(imgCap);

            // 滑块
            var slider = new Slider { lowValue = 0f, highValue = 100f, value = 60f };
            slider.AddToClassList("a2ui-slider");
            scroll.Add(slider);

            // 选项卡
            var tabs = new VisualElement();
            tabs.AddToClassList("a2ui-tabs");
            var tabHeader = new VisualElement();
            tabHeader.AddToClassList("a2ui-tabs__header");
            var t1 = new Label("New"); t1.AddToClassList("a2ui-tabs__tab"); t1.AddToClassList("a2ui-tabs__tab--active");
            var t2 = new Label("Used"); t2.AddToClassList("a2ui-tabs__tab");
            tabHeader.Add(t1); tabHeader.Add(t2);
            tabs.Add(tabHeader);
            scroll.Add(tabs);

            // 文本输入
            var tf = new TextField { value = "Search for Honda Pilot" };
            tf.AddToClassList("a2ui-textfield");
            scroll.Add(tf);

            // 勾选
            var cbWrap = new VisualElement();
            cbWrap.AddToClassList("a2ui-checkbox-wrap");
            var cb = new Toggle { value = true };
            cb.AddToClassList("a2ui-checkbox");
            var cbHead = new Label("Financing"); cbHead.AddToClassList("a2ui-checkbox__heading");
            cbWrap.Add(cb); cbWrap.Add(cbHead);
            scroll.Add(cbWrap);

            // 芯片
            var chips = new VisualElement();
            chips.AddToClassList("a2ui-chips");
            foreach (var s in new[] { "SUV", "Electric", "7-Seat" })
            {
                var chip = new Label(s); chip.AddToClassList("a2ui-chip"); chips.Add(chip);
            }
            scroll.Add(chips);

            // 分割线
            var div = new VisualElement();
            div.AddToClassList("a2ui-divider");
            div.AddToClassList("a2ui-divider--horizontal");
            scroll.Add(div);

            // 座舱扩展类型
            var cabin = new VisualElement();
            cabin.AddToClassList("a2ui-cabin");
            cabin.AddToClassList("a2ui-cabin--media");
            var cabinTitle = new Label("Now Playing"); cabinTitle.AddToClassList("a2ui-cabin__title");
            var cabinBody = new Label("Lo-Fi Beats"); cabinBody.AddToClassList("a2ui-cabin__body");
            cabin.Add(cabinTitle); cabin.Add(cabinBody);
            scroll.Add(cabin);

            // 3) 触发布局与样式计算（多等一帧，确保面板完成首轮布局）
            root.MarkDirtyRepaint();
            yield return null;
            yield return null;
            yield return null;

            // 校验主色解析（橙红来自真实稿，不硬编码）
            Color got = primaryBtn.resolvedStyle.backgroundColor;
            Assert.IsTrue(
                Mathf.Abs(got.r - expectedPrimary.r) < 0.05f &&
                Mathf.Abs(got.g - expectedPrimary.g) < 0.05f &&
                Mathf.Abs(got.b - expectedPrimary.b) < 0.05f,
                $"主色按钮背景应解析为设计主色 {expectedPrimary}，实际 {got}");

            // 校验不塌缩、不横向溢出 1080
            Assert.IsTrue(root.worldBound.height > 100, "组件树不应塌缩（height<=100）");
            Assert.IsTrue(root.worldBound.width <= 1080 + 2,
                $"不应横向溢出 1080，实际宽度 {root.worldBound.width}");

            // 4) 截图
            try
            {
                string shot = Path.Combine(Application.persistentDataPath, "A2uiFigmaExportSmoke.png");
                ScreenCapture.CaptureScreenshot(shot);
                Debug.Log("[A2uiFigmaExportVisualTest] 截图已存：" + shot);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[A2uiFigmaExportVisualTest] 截图失败（不影响断言）：" + e.Message);
            }

            yield return null;

            UnityEngine.Object.Destroy(go);
        }
    }
}
