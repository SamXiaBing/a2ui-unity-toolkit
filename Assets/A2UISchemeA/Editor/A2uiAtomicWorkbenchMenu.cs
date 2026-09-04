using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA.Editor
{
    public static class A2uiAtomicWorkbenchMenu
    {
        const string ScenePath = "Assets/A2UISchemeA/Scenes/A2UIAtomicWorkbench.unity";
        const string PanelPath = "Assets/A2UISchemeA/PanelSettings.asset";
        const string StylesDir = "Assets/A2UISchemeA/Styles/";

        [MenuItem("A2UI Scheme A/创建原子组件样式工作台")]
        public static void CreateWorkbench()
        {
            EnsureFolder("Assets/A2UISchemeA/Scenes");
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ScenePath)))
                AssetDatabase.DeleteAsset(ScenePath);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                A2uiSchemeAMenu.EnsureScene(); // 会顺带建 PanelSettings
                panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Object.FindObjectOfType<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.06f, 0.08f, 0.11f);
            }

            var go = new GameObject("A2uiStyleWorkbench");
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            var wb = go.AddComponent<A2uiStyleWorkbench>();
            var so = new SerializedObject(wb);
            so.FindProperty("craftedStyle").objectReferenceValue = Load("Crafted.uss");
            so.FindProperty("tokensStyle").objectReferenceValue = Load("Tokens.uss");
            so.FindProperty("exportPath").stringValue = StylesDir + "WorkbenchOverrides.uss";
            so.ApplyModifiedPropertiesWithoutUndo();

            SpawnComponents();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[A2UISchemeA] 原子组件样式工作台就绪: " + ScenePath);
            EditorUtility.DisplayDialog(
                "A2UI Scheme A",
                "原子组件样式工作台已创建并打开。\n\nGame 视图：16 个原子组件平铺，顶栏切 USS 皮肤。\nHierarchy：每个组件一个 GameObject，Inspector 改参数即所见即所得。\n「导出 USS 覆盖」把样式写进 Styles/WorkbenchOverrides.uss。\n\n若 Game 视图没渲染，点一下 Play（编辑模式 UIDocument 也应显示）。",
                "OK");
        }

        static void SpawnComponents()
        {
            var names = new (A2uiAtomicComponent.AtomicType type, string goName, System.Action<A2uiAtomicComponent> cfg)[]
            {
                (A2uiAtomicComponent.AtomicType.Text, "Text-h1", c => { c.Text = "这是一段 h1 大标题"; c.UsageHint = A2uiAtomicComponent.Hint.h1; }),
                (A2uiAtomicComponent.AtomicType.Text, "Text-body", c => { c.Text = "body 正文，用于描述性内容"; c.UsageHint = A2uiAtomicComponent.Hint.body; }),
                (A2uiAtomicComponent.AtomicType.Text, "Text-caption", c => { c.Text = "caption 注释说明"; c.UsageHint = A2uiAtomicComponent.Hint.caption; }),
                (A2uiAtomicComponent.AtomicType.Button, "Button-primary", c => { c.Text = "主按钮"; c.Primary = true; }),
                (A2uiAtomicComponent.AtomicType.Button, "Button-secondary", c => { c.Text = "次按钮"; c.Primary = false; }),
                (A2uiAtomicComponent.AtomicType.CheckBox, "CheckBox", c => { c.Label = "脚部感应开启"; c.IsChecked = true; }),
                (A2uiAtomicComponent.AtomicType.Slider, "Slider", c => { c.Label = "开启高度"; c.MinValue = 0; c.MaxValue = 100; c.Value = 80; }),
                (A2uiAtomicComponent.AtomicType.MultipleChoice, "MultipleChoice", c => { c.Options = new System.Collections.Generic.List<string> { "导航 A", "导航 B", "导航 C" }; }),
                (A2uiAtomicComponent.AtomicType.Divider, "Divider", c => { }),
                (A2uiAtomicComponent.AtomicType.Card, "Card", c => { c.Label = "卡片内容"; }),
                (A2uiAtomicComponent.AtomicType.Row, "Row", c => { }),
                (A2uiAtomicComponent.AtomicType.Column, "Column", c => { }),
                (A2uiAtomicComponent.AtomicType.MediaMiniBar, "MediaMiniBar", c => { c.Title = "雨林白噪 - 鸟鸣"; }),
                (A2uiAtomicComponent.AtomicType.ClimateStep, "ClimateStep", c => { c.TempLabel = "24°C"; }),
                (A2uiAtomicComponent.AtomicType.RestBanner, "RestBanner", c => { c.Label = "小憩一下"; }),
                (A2uiAtomicComponent.AtomicType.Image, "Image", c => { c.Label = "图片占位"; }),
                (A2uiAtomicComponent.AtomicType.Tabs, "Tabs", c => { c.Options = new System.Collections.Generic.List<string> { "Tab 1", "Tab 2", "Tab 3" }; }),
                (A2uiAtomicComponent.AtomicType.TextField, "TextField", c => { c.Label = "目的地"; c.PlaceholderText = "输入地名…"; }),
                (A2uiAtomicComponent.AtomicType.DateTimeInput, "DateTimeInput", c => { c.Label = "时间"; c.PlaceholderText = "2026-08-12 14:30"; }),
            };

            var parent = new GameObject("原子组件").transform;
            foreach (var (type, goName, cfg) in names)
            {
                var go = new GameObject(goName);
                go.transform.SetParent(parent);
                var comp = go.AddComponent<A2uiAtomicComponent>();
                comp.Type = type;
                cfg(comp);
            }
        }

        static StyleSheet Load(string name) =>
            AssetDatabase.LoadAssetAtPath<StyleSheet>(StylesDir + name);

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            var cur = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
