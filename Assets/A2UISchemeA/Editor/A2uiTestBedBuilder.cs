using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace A2UISchemeA.Editor
{
    /// <summary>
    /// 一键生成测试床场景：纯色背景 Camera + A2uiSchemeAHost。
    /// batchmode: -executeMethod A2UISchemeA.Editor.A2uiTestBedBuilder.Build
    /// </summary>
    public static class A2uiTestBedBuilder
    {
        const string ScenePath = "Assets/Scenes/A2UITestBed.unity";

        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.09f, 0.11f, 0.15f, 1f); // 深灰蓝，与主题卡片区分明显
            cam.orthographic = false;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<UniversalAdditionalCameraData>();

            var hostGo = new GameObject("A2uiSchemeAHost");
            var host = hostGo.AddComponent<A2uiSchemeAHost>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[TestBedBuilder] scene saved: " + ScenePath + " host=" + (host != null));
        }
    }
}
