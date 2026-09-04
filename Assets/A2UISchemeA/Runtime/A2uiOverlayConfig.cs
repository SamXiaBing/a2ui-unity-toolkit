using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA
{
    /// <summary>
    /// Player 构建引用：Resources/A2uiOverlayConfig.asset
    /// </summary>
    [CreateAssetMenu(menuName = "A2UI Scheme A/Overlay Config", fileName = "A2uiOverlayConfig")]
    public class A2uiOverlayConfig : ScriptableObject
    {
        public PanelSettings panelSettings;
        public ThemeStyleSheet runtimeTheme;
        public StyleSheet craftedStyle;
        public StyleSheet tokensStyle;
        public string defaultJsonlRelativePath = "Assets/A2UISchemeA/Samples/timeline_bench/01_media.v0.8.jsonl";
    }
}
