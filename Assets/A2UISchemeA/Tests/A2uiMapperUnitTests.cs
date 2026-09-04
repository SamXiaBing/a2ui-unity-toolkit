using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA.Tests
{
    /// <summary>
    /// Mapper 规则单元测试：固化历史上真实踩过的 7 类转换 bug。
    /// 每个用例对应一次真实线上事故，改 Mapper 前先在这里看到红，修复后必须回到绿。
    /// </summary>
    [TestFixture]
    public class A2uiMapperUnitTests
    {
        static A2uiV08SurfaceState BuildState(string jsonl)
        {
            var v = A2uiV08Validator.ValidateJsonl(jsonl, out var msgs);
            Assert.IsTrue(v.Ok, "测试 JSONL 必须先通过校验");
            var p = new A2uiV08Processor();
            foreach (var m in msgs) p.IngestMessage(m);
            A2uiV08SurfaceState state = null;
            foreach (var kv in p.Surfaces) { state = kv.Value; break; }
            Assert.IsNotNull(state, "必须有就绪 surface");
            return state;
        }

        // —— 事故 1：Column 容器 wrap 导致假并列布局（02_login bottomRow 跑到 C 右侧）——
        [Test]
        public void MapFlex_Column_NoWrap()
        {
            var state = BuildState(
                "{\"surfaceUpdate\":{\"surfaceId\":\"t\",\"components\":[" +
                "{\"id\":\"r\",\"component\":{\"Column\":{\"children\":{\"explicitList\":[\"a\",\"b\"]}}}}," +
                "{\"id\":\"a\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"A\"}}}}," +
                "{\"id\":\"b\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"B\"}}}}]}}\n" +
                "{\"beginRendering\":{\"surfaceId\":\"t\",\"root\":\"r\",\"catalogId\":\"demo\"}}");
            var ve = new A2uiV08CatalogMapper((n, c) => { }).Build(state);
            var col = FindByClass(ve, "a2ui-column");
            Assert.IsNotNull(col, "Column 容器必须存在");
            Assert.AreEqual(Wrap.NoWrap, col.resolvedStyle.flexWrap,
                "Column 禁止换行：wrap 会把溢出子元素横向堆到右侧");
        }

        // —— 事故 1 姊妹：Row 必须允许换行（按钮组挤不下时掉行而不是撑破）——
        [Test]
        public void MapFlex_Row_Wrap()
        {
            var state = BuildState(
                "{\"surfaceUpdate\":{\"surfaceId\":\"t\",\"components\":[" +
                "{\"id\":\"r\",\"component\":{\"Row\":{\"children\":{\"explicitList\":[\"a\",\"b\"]}}}}," +
                "{\"id\":\"a\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"A\"}}}}," +
                "{\"id\":\"b\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"B\"}}}}]}}\n" +
                "{\"beginRendering\":{\"surfaceId\":\"t\",\"root\":\"r\",\"catalogId\":\"demo\"}}");
            var ve = new A2uiV08CatalogMapper((n, c) => { }).Build(state);
            var row = FindByClass(ve, "a2ui-row");
            Assert.IsNotNull(row);
            Assert.AreEqual(Wrap.Wrap, row.resolvedStyle.flexWrap);
        }

        // —— 事故 2：DS 主题 tab flex-grow:1 均分，窄卡撑破（tab 按钮 NoWrap + 不均分）——
        [Test]
        public void MapTabs_ButtonNoGrow_NoWrap()
        {
            var state = BuildState(
                "{\"surfaceUpdate\":{\"surfaceId\":\"t\",\"components\":[" +
                "{\"id\":\"r\",\"component\":{\"Tabs\":{\"tabItems\":[" +
                "{\"title\":{\"literalString\":\"T1\"},\"child\":\"a\"}," +
                "{\"title\":{\"literalString\":\"T2\"},\"child\":\"b\"}]}}}," +
                "{\"id\":\"a\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"A\"}}}}," +
                "{\"id\":\"b\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"B\"}}}}]}}\n" +
                "{\"beginRendering\":{\"surfaceId\":\"t\",\"root\":\"r\",\"catalogId\":\"demo\"}}");
            var ve = new A2uiV08CatalogMapper((n, c) => { }).Build(state);
            var header = FindByClass(ve, "a2ui-tabs__header");
            Assert.IsNotNull(header);
            Assert.AreEqual(Wrap.Wrap, header.resolvedStyle.flexWrap, "tab 行可换行");
            foreach (var btn in header.Children().OfType<Button>())
            {
                Assert.AreEqual(0f, btn.resolvedStyle.flexGrow, "tab 按钮不参与均分（inline 覆盖 USS flexGrow）");
                Assert.AreEqual(WhiteSpace.NoWrap, btn.resolvedStyle.whiteSpace, "tab 文本不折行");
            }
        }

        // —— 事故 3：:last-child 伪类不被引擎支持，末位子元素用 class 标记 ——
        [TestCase("Column")]
        [TestCase("Row")]
        public void MapFlex_LastChildMarked(string container)
        {
            var state = BuildState(
                "{\"surfaceUpdate\":{\"surfaceId\":\"t\",\"components\":[" +
                $"{{\"id\":\"r\",\"component\":{{{container}:{{\"children\":{{\"explicitList\":[\"a\",\"b\"]}}}}}}}}," +
                "{\"id\":\"a\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"A\"}}}}," +
                "{\"id\":\"b\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"B\"}}}}]}}\n" +
                "{\"beginRendering\":{\"surfaceId\":\"t\",\"root\":\"r\",\"catalogId\":\"demo\"}}");
            var ve = new A2uiV08CatalogMapper((n, c) => { }).Build(state);
            var cont = FindByClass(ve, container == "Row" ? "a2ui-row" : "a2ui-column");
            var last = cont.Children().Last();
            Assert.IsTrue(last.ClassListContains("a2ui-last-child"),
                "末位子元素必须挂 a2ui-last-child（引擎不支持 :last-child 伪类）");
            Assert.IsFalse(cont.Children().First().ClassListContains("a2ui-last-child"));
        }

        // —— 事故 4：入场动画在 detached 元素上挂 class → 引擎样式遍历越界崩溃 ——
        [Test]
        public void Build_DoesNotApplyEntranceAnimation_Detached()
        {
            var state = BuildState(
                "{\"surfaceUpdate\":{\"surfaceId\":\"t\",\"components\":[" +
                "{\"id\":\"r\",\"component\":{\"Column\":{\"children\":{\"explicitList\":[\"a\"]}}}}," +
                "{\"id\":\"a\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"A\"}}}}]}}\n" +
                "{\"beginRendering\":{\"surfaceId\":\"t\",\"root\":\"r\",\"catalogId\":\"demo\"}}");
            var ve = new A2uiV08CatalogMapper((n, c) => { }).Build(state);
            // Build 产物在挂到 panel 前不应携带动画类（动画须在 attach 后调用）
            Assert.IsFalse(ve.ClassListContains("a2ui-anim--enter"),
                "Build() 不得内置入场动画类：detached 元素挂 USS 规则类会触发引擎崩溃");
        }

        // —— 事故 5：按钮文字只认 child=Text，text 字段/非 Text child 不认 ——
        [Test]
        public void MapButton_TextFromChildTextOnly()
        {
            var state = BuildState(
                "{\"surfaceUpdate\":{\"surfaceId\":\"t\",\"components\":[" +
                "{\"id\":\"r\",\"component\":{\"Column\":{\"children\":{\"explicitList\":[\"ok\",\"bad\"]}}}}," +
                "{\"id\":\"ok\",\"component\":{\"Button\":{\"child\":\"ok-t\",\"action\":{\"name\":\"go\"}}}}," +
                "{\"id\":\"ok-t\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"正确的文案\"}}}}," +
                "{\"id\":\"bad\",\"component\":{\"Button\":{\"child\":\"bad-i\",\"action\":{\"name\":\"fallback\"}}}}," +
                "{\"id\":\"bad-i\",\"component\":{\"Icon\":{\"name\":\"star\"}}}]}}\n" +
                "{\"beginRendering\":{\"surfaceId\":\"t\",\"root\":\"r\",\"catalogId\":\"demo\"}}");
            var ve = new A2uiV08CatalogMapper((n, c) => { }).Build(state);
            var buttons = Collect<Button>(ve).ToList();
            Assert.AreEqual(2, buttons.Count);
            Assert.AreEqual("正确的文案", buttons[0].text, "child=Text 时按钮文字取 Text 内容");
            // child 是 Icon（非 Text）时回退 action 名
            Assert.AreEqual("fallback", buttons[1].text);
        }

        // —— 事故 6：ResolveBound path 优先、literal 兜底；非标量拒绝倒灌 ——
        [Test]
        public void ResolveBound_PathFirst_LiteralFallback()
        {
            var state = BuildState(
                "{\"surfaceUpdate\":{\"surfaceId\":\"t\",\"components\":[" +
                "{\"id\":\"r\",\"component\":{\"Column\":{\"children\":{\"explicitList\":[\"a\"]}}}}," +
                "{\"id\":\"a\",\"component\":{\"Text\":{\"text\":{\"path\":\"/media/title\",\"literalString\":\"兜底文案\"}}}}]}}\n" +
                "{\"dataModelUpdate\":{\"surfaceId\":\"t\",\"contents\":[" +
                "{\"key\":\"media\",\"valueMap\":[{\"key\":\"title\",\"valueString\":\"来自数据\"}]}]}}\n" +
                "{\"beginRendering\":{\"surfaceId\":\"t\",\"root\":\"r\",\"catalogId\":\"demo\"}}");
            var ve = new A2uiV08CatalogMapper((n, c) => { }).Build(state);
            var label = FindText(ve, "来自数据");
            Assert.IsNotNull(label, "path 命中时应取数据模型的值");

            // 无数据时 literal 兜底
            var state2 = BuildState(
                "{\"surfaceUpdate\":{\"surfaceId\":\"t2\",\"components\":[" +
                "{\"id\":\"r\",\"component\":{\"Column\":{\"children\":{\"explicitList\":[\"a\"]}}}}," +
                "{\"id\":\"a\",\"component\":{\"Text\":{\"text\":{\"path\":\"/missing/title\",\"literalString\":\"兜底文案\"}}}}]}}\n" +
                "{\"beginRendering\":{\"surfaceId\":\"t2\",\"root\":\"r\",\"catalogId\":\"demo\"}}");
            var ve2 = new A2uiV08CatalogMapper((n, c) => { }).Build(state2);
            Assert.IsNotNull(FindText(ve2, "兜底文案"), "path 未命中时回退 literal");
        }

        // —— 事故 7：MapList 末项同样要挂 last-child（列表项间距收尾）——
        [Test]
        public void MapList_LastItemMarked()
        {
            var state = BuildState(
                "{\"surfaceUpdate\":{\"surfaceId\":\"t\",\"components\":[" +
                "{\"id\":\"r\",\"component\":{\"List\":{\"children\":{\"explicitList\":[\"a\",\"b\"]}}}}," +
                "{\"id\":\"a\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"A\"}}}}," +
                "{\"id\":\"b\",\"component\":{\"Text\":{\"text\":{\"literalString\":\"B\"}}}}]}}\n" +
                "{\"beginRendering\":{\"surfaceId\":\"t\",\"root\":\"r\",\"catalogId\":\"demo\"}}");
            var ve = new A2uiV08CatalogMapper((n, c) => { }).Build(state);
            var list = FindByClass(ve, "a2ui-list") as ScrollView;
            Assert.IsNotNull(list);
            var items = list.contentContainer.Children().ToList();
            Assert.IsTrue(items.Last().ClassListContains("a2ui-last-child"));
        }

        // —— 工具 ——
        static VisualElement FindByClass(VisualElement root, string cls)
        {
            VisualElement found = null;
            Walk(root, ve => { if (found == null && ve.ClassListContains(cls)) found = ve; });
            return found;
        }

        static Label FindText(VisualElement root, string text)
        {
            Label found = null;
            Walk(root, ve =>
            {
                if (found == null && ve is Label l && l.text == text) found = l;
            });
            return found;
        }

        static IEnumerable<T> Collect<T>(VisualElement root) where T : VisualElement
        {
            var list = new List<T>();
            Walk(root, ve => { if (ve is T t) list.Add(t); });
            return list;
        }

        static void Walk(VisualElement ve, System.Action<VisualElement> visit)
        {
            if (ve == null) return;
            visit(ve);
            for (var i = 0; i < ve.childCount; i++) Walk(ve[i], visit);
        }
    }

    /// <summary>
    /// 主题键解析单元测试：固化 2026-09-03 的事故——figma 皮肤注册表键是
    /// figma-figmaexport（目录名 FigmaExport），但调用方常发简写/变体，
    /// 旧行为静默回退 ds（或误绑别的皮肤）。依赖工程内真实 Styles/ 目录
    /// （含 FigmaExport/FigmaTokens.uss），在 Editor 与 PlayMode 均可解析。
    /// </summary>
    [TestFixture]
    public class A2uiThemeKeyUnitTests
    {
        static string FigmayaKey()
        {
            var e = A2uiThemeRegistry.FindByKey("figma-figmaexport");
            return e.Key;
        }

        // —— 别名一律解析到 FigmaExport 皮肤的规范键 ——
        [TestCase("figma-export")]
        [TestCase("figmaexport")]
        [TestCase("figma export")]
        [TestCase("Figma-Export")]
        [TestCase("figma-figmaexport")]
        public void FindByKey_FigmaAliases_ResolveToRegistryKey(string alias)
        {
            Assert.AreEqual(FigmayaKey(), A2uiThemeRegistry.FindByKey(alias).Key,
                $"别名 '{alias}' 必须解析到 FigmaExport 皮肤");
        }

        // —— 内置键不被别名容错劫持 ——
        [TestCase("ds")]
        [TestCase("a")]
        [TestCase("dark")]
        public void FindByKey_BuiltinKeys_Exact(string key)
        {
            Assert.AreEqual(key, A2uiThemeRegistry.FindByKey(key).Key);
        }

        // —— 不存在的 figma 皮肤不误绑（figma-dark 无目录 → 回退 ds）——
        [Test]
        public void FindByKey_UnknownFigmaKey_FallsBackToDs()
        {
            Assert.AreEqual("ds", A2uiThemeRegistry.FindByKey("figma-dark").Key);
        }

        // —— 旧键兼容与空值；无分隔别名在 NormalizeTheme 入口即规范化（B1 修复）——
        [Test]
        public void NormalizeTheme_LegacyEmptyAndAliases()
        {
            Assert.AreEqual("figma-figmaexport", A2uiSchemeAHost.NormalizeTheme("figma"));
            Assert.AreEqual("figma-figmaexport", A2uiSchemeAHost.NormalizeTheme("figmaexport"));
            Assert.AreEqual("figma-figmaexport", A2uiSchemeAHost.NormalizeTheme("figma export"));
            Assert.AreEqual("ds", A2uiSchemeAHost.NormalizeTheme(""));
            Assert.AreEqual("ds", A2uiSchemeAHost.NormalizeTheme(null));
        }

        // —— 不存在的皮肤不得被更长的皮肤名劫持（EndsWith 而非 Contains）——
        [Test]
        public void NormalizeTheme_UnknownFigmaVariant_DoesNotHijackLongerSkin()
        {
            // 当前只有 FigmaExport 目录；figma-m3template 若存在也不含该后缀。
            // figma-dark 不匹配任何候选 → 维持旧回退 ds。
            Assert.AreEqual("ds", A2uiSchemeAHost.NormalizeTheme("figma-dark"));
        }
    }
}
