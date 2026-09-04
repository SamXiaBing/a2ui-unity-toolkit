using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

[assembly: InternalsVisibleTo("A2uiSchemeATests")]

namespace A2UISchemeA
{
    /// <summary>
    /// 回归测试公共入口：把「设主题 → 应用 JSONL → 取卡片 → 几何扫描 → 截图」
    /// 收敛成一个无反射的 API，供 PlayMode 测试床调用。
    /// 只在测试/工具场景使用；业务代码不应依赖本类。
    /// </summary>
    public static class A2uiTestApi
    {
        /// <summary>测试床场景里的 Host（找不到即抛错，避免静默空跑）。</summary>
        public static A2uiSchemeAHost Host
        {
            get
            {
                var host = UnityEngine.Object.FindObjectOfType<A2uiSchemeAHost>();
                if (host == null)
                    throw new InvalidOperationException(
                        "A2uiTestApi: 场景里没有 A2uiSchemeAHost。请用 Assets/Scenes/A2UITestBed.unity。");
                return host;
            }
        }

        /// <summary>设置主题并应用一份 JSONL（走 OnLivePayload 完整链路，与真实热推一致）。</summary>
        public static void Apply(string themeKey, string prompt, string jsonl)
        {
            Host.ApplyForTest(themeKey, prompt, jsonl);
        }

        /// <summary>当前渲染卡片的屏幕矩形；无卡片（被隐藏/未渲染）返回 null。</summary>
        public static Rect? GetCardRect()
        {
            var root = Host.GetComponent<UIDocument>().rootVisualElement;
            return FindCardRect(root);
        }

        internal static Rect? FindCardRect(VisualElement root)
        {
            Rect? found = null;
            Walk(root, ve =>
            {
                if (found == null && ve.ClassListContains("a2ui-overlay-card__inner"))
                    found = ve.worldBound;
            });
            return found;
        }

        /// <summary>一个越界问题：文本内容 + 它的世界边界 + 卡片边界。</summary>
        public struct OverflowIssue
        {
            public string Text;
            public Rect World;
            public Rect Card;
            public override string ToString()
                => $"'{Text}' x=({World.x:F0}~{World.xMax:F0}) card=({Card.x:F0}~{Card.xMax:F0})";
        }

        /// <summary>
        /// 几何扫描：找所有「渲染中且不在 ScrollView / overflow:hidden 容器内」的文本元素，
        /// 超出卡片边界 ±tolerance 即记录；另做纵向挤压检查（盒高比文本实测需要高度矮
        /// tolerance 以上，即团结 UITK 测量固化 bug 的特征，见 A2uiV08CatalogMapper.FixTextMeasure）。
        /// overflow:hidden 内的文本被视觉裁剪是有意设计（TextField 多行、长文本截断），
        /// 不算布局缺陷；但纵向检查不豁免 ScrollView——竖向滚动救不了盒高不足，
        /// 只豁免多行 TextField 内部（定高裁剪区）。
        /// </summary>
        public static List<OverflowIssue> ScanOverflow(float tolerance = 2f)
        {
            var result = new List<OverflowIssue>();
            var root = Host.GetComponent<UIDocument>().rootVisualElement;
            var card = FindCardRect(root);
            if (card == null) return result;
            var cardR = card.Value;
            const float MinImageHeight = 24f;

            Walk(root, ve =>
            {
                if (ve is TextElement te && !string.IsNullOrEmpty(te.text))
                {
                    var w = te.worldBound;
                    if (w.width < 1f && w.height < 1f) return;

                    // 纵向挤压检查必须在 InClippingContainer 早退之前：
                    // 覆盖层内容现整体包在 a2ui-overlay-card__scroll（ScrollView）里，
                    // 若先走 ScrollView 豁免会全量漏检。竖向滚动救不了盒高不足，
                    // 故纵向只豁免多行 TextField 内部（定高裁剪区）。
                    // 测宽用 content box（扣水平 padding/border），与 FixTextMeasure 同式
                    var lw = te.layout.width;
                    if (lw > 1f && !InMultilineField(te))
                    {
                        var contentW = lw
                            - te.resolvedStyle.paddingLeft - te.resolvedStyle.paddingRight
                            - te.resolvedStyle.borderLeftWidth - te.resolvedStyle.borderRightWidth;
                        if (contentW >= 1f)
                        {
                            var measured = te.MeasureTextSize(te.text, contentW,
                                VisualElement.MeasureMode.AtMost, 9999f, VisualElement.MeasureMode.Undefined);
                            var need = measured.y + te.resolvedStyle.paddingTop + te.resolvedStyle.paddingBottom;
                            // 阈值用比例而非绝对差：MeasureTextSize 含 MiSans 行距（1.65em），
                            // 而渲染盒只有字形度量（1.2em）——单行 20px 文本 h=24/need=33 是
                            // 正常渲染（ratio 0.73），历史塌陷事故是 h=8/need=23（ratio 0.35）。
                            // 0.6 分界线：塌陷/真裁剪必落其下，行距差永不触雷。
                            if (!float.IsNaN(need) && !float.IsInfinity(need) && need > 1f &&
                                te.layout.height < need * 0.6f)
                                result.Add(new OverflowIssue
                                {
                                    Text = "VCRAMP " + Trunc(te.text, 12) +
                                           $" h={te.layout.height:F0} need={need:F0}",
                                    World = te.worldBound,
                                    Card = cardR
                                });
                        }
                    }

                    if (InClippingContainer(te)) return;
                    if (w.xMax > cardR.xMax + tolerance || w.x < cardR.x - tolerance)
                        result.Add(new OverflowIssue { Text = Trunc(te.text, 16), World = w, Card = cardR });
                }
                if (ve.ClassListContains("a2ui-image"))
                {
                    var h = ve.layout.height;
                    if (h > 0f && h < MinImageHeight)
                        result.Add(new OverflowIssue
                        {
                            Text = "IMG:" + (int)h + "px",
                            World = ve.worldBound,
                            Card = cardR
                        });
                }
            });
            return result;
        }

        /// <summary>是否在多行 TextField（a2ui-textfield--long）内部——定高裁剪区，纵向不检查。</summary>
        static bool InMultilineField(VisualElement ve)
        {
            var p = ve.parent;
            while (p != null)
            {
                if (p.ClassListContains("a2ui-textfield--long")) return true;
                p = p.parent;
            }
            return false;
        }

        /// <summary>
        /// 文本是否在 ScrollView 或 overflow:hidden 容器的后代——这些是有意的视觉裁剪。
        /// 检查 resolvedStyle.overflow == Overflow.Hidden（排除卡片自身，
        /// 卡片的 overflow:hidden 是最外层兜底，不应让内部所有越界都被豁免）。
        /// </summary>
        static bool InClippingContainer(VisualElement ve)
        {
            if (InScrollView(ve)) return true;
            var p = ve.parent;
            while (p != null)
            {
                // 到卡片层为止：卡片自身的 overflow:hidden 是最终兜底，不豁免内部
                if (p.ClassListContains("a2ui-overlay-card")) break;
                // .a2ui-textfield 有 overflow:hidden（多行文本有意裁剪）
                if (p.ClassListContains("a2ui-textfield")) return true;
                p = p.parent;
            }
            return false;
        }

        /// <summary>把当前 Game View 截成 Texture2D（含 UITK 叠层）。</summary>
        public static Texture2D CaptureFrame()
        {
            // batchmode 下 CaptureScreenshotAsTexture 不产出内容；ReadPixels 直接读
            // backbuffer。注意：ReadPixels 只能在渲染帧内调用（本方法在测试主线程的
            // 帧回调里执行，非 WaitForEndOfFrame 上下文时引擎会报错——调用方需在
            // yield return null 之后、下一帧渲染前的同步段调用）。
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            return tex;
        }

        static bool InScrollView(VisualElement ve)
        {
            var p = ve.parent;
            while (p != null)
            {
                if (p is ScrollView) return true;
                p = p.parent;
            }
            return false;
        }

        static void Walk(VisualElement ve, Action<VisualElement> visit)
        {
            if (ve == null) return;
            if (ve.resolvedStyle.display == DisplayStyle.None) return;
            visit(ve);
            for (var i = 0; i < ve.childCount; i++)
                Walk(ve[i], visit);
        }

        static string Trunc(string s, int n)
            => s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
