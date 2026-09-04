using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA
{
    /// <summary>
    /// DS 设计系统（sinanata/unity-ui-toolkit-design-system，MIT）共享样式加载器。
    /// 所有界面（A2uiSchemeAHost / LauncherSurfaceHost / StyleWorkbench …）统一从这里取 DS 全套 USS，
    /// 避免 15 条路径在多处重复。加载逻辑与 A2uiSchemeAHost 一致：编辑器走 AssetDatabase。
    /// </summary>
    public static class A2uiDsStyles
    {
        public static readonly string[] DsStylePaths = new[]
        {
            "Assets/A2UISchemeA/Styles/DS/DesignTokens.uss",
            "Assets/A2UISchemeA/Styles/DS/Typography.uss",
            "Assets/A2UISchemeA/Styles/DS/Icons.uss",
            "Assets/A2UISchemeA/Styles/DS/Buttons.uss",
            "Assets/A2UISchemeA/Styles/DS/Inputs.uss",
            "Assets/A2UISchemeA/Styles/DS/TabsAndFilters.uss",
            "Assets/A2UISchemeA/Styles/DS/Cards.uss",
            "Assets/A2UISchemeA/Styles/DS/Navigation.uss",
            "Assets/A2UISchemeA/Styles/DS/Badges.uss",
            "Assets/A2UISchemeA/Styles/DS/Controls.uss",
            "Assets/A2UISchemeA/Styles/DS/Overlays.uss",
            "Assets/A2UISchemeA/Styles/DS/Feedback.uss",
            "Assets/A2UISchemeA/Styles/DS/Mobile.uss",
            "Assets/A2UISchemeA/Styles/DS/DropdownPopup.uss",
            "Assets/A2UISchemeA/Styles/DS/A2uiAlias.uss",
        };

        public static StyleSheet[] LoadAll()
        {
            var list = new List<StyleSheet>();
#if UNITY_EDITOR
            foreach (var p in DsStylePaths)
            {
                var s = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(p);
                if (s != null) list.Add(s);
                else Debug.LogWarning("[A2uiDsStyles] DS 样式表未找到: " + p);
            }
#endif
            return list.ToArray();
        }

        /// <summary>把 DS 全套样式表挂到 root，并加 ds-root 作用域类（别名层在此作用域下接管 a2ui 组件视觉）。</summary>
        public static void Apply(VisualElement root)
        {
            if (root == null) return;
            foreach (var s in LoadAll()) TryAdd(root, s);
            root.AddToClassList("ds-root");
        }

        static void TryAdd(VisualElement root, StyleSheet sheet)
        {
            if (sheet == null || root == null) return;
            for (var i = 0; i < root.styleSheets.count; i++)
                if (root.styleSheets[i] == sheet) return;
            root.styleSheets.Add(sheet);
        }
    }
}
