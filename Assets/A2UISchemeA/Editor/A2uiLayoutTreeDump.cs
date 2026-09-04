using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA.Editor
{
    /// <summary>
    /// 布局树转储（视觉诊断）：把当前 UIDocument 面板的整棵树
    /// （类型/文本/尺寸/位置/flex 参数/class）写进 TestResults/layout_tree.txt。
    /// 用于"眼见为实"后定位塌陷层级——比几何断言信息量大得多。
    /// </summary>
    public static class A2uiLayoutTreeDump
    {
        [MenuItem("A2UI Scheme A/转储布局树 (Layout Tree Dump)")]
        public static void Dump()
        {
            var host = UnityEngine.Object.FindObjectOfType<A2uiSchemeAHost>();
            if (host == null) { Debug.LogError("[Dump] no A2uiSchemeAHost"); return; }
            var doc = host.GetComponent<UIDocument>();
            var root = doc.rootVisualElement;
            if (root == null) { Debug.LogError("[Dump] root null"); return; }

            var sb = new StringBuilder();
            sb.AppendLine($"panel={root.panel?.visualTree.layout.width:F0}x{root.panel?.visualTree.layout.height:F0}");
            sb.AppendLine($"scaleMode={doc.panelSettings.scaleMode} refRes={doc.panelSettings.referenceResolution}");
            Walk(root, 0, sb);

            var dir = Path.Combine(Application.dataPath, "..", "TestResults");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "layout_tree.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[Dump] saved: " + path + "（也打印到 Console）\n" + sb);
        }

        static void Walk(VisualElement ve, int depth, StringBuilder sb)
        {
            if (ve.resolvedStyle.display == DisplayStyle.None) return;
            if (depth > 14) return;
            var pad = new string(' ', depth * 2);
            var te = ve as TextElement;
            var txt = te != null && !string.IsNullOrEmpty(te.text)
                ? " '" + (te.text.Length > 20 ? te.text.Substring(0, 20) : te.text) + "'" : "";
            sb.AppendLine(
                $"{pad}{ve.GetType().Name}{txt} " +
                $"rect=({ve.layout.x:F0},{ve.layout.y:F0} {ve.layout.width:F0}x{ve.layout.height:F0}) " +
                $"dir={ve.resolvedStyle.flexDirection} wrap={ve.resolvedStyle.flexWrap} " +
                $"minH={ve.resolvedStyle.minHeight} h={ve.resolvedStyle.height} " +
                $"grow={ve.resolvedStyle.flexGrow}");
            for (var i = 0; i < ve.childCount; i++) Walk(ve[i], depth + 1, sb);
        }
    }
}
