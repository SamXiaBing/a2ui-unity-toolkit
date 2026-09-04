using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace A2UISchemeA
{
    /// <summary>
    /// 台架时间轴：按秒推送预制 A2UI JSONL（无语音），证明随时序变 UI。
    /// </summary>
    public class A2uiTimelineDriver : MonoBehaviour
    {
        [Serializable]
        public class Beat
        {
            public float atSeconds;
            public string jsonlRelativePath;
            public string label;
        }

        public bool loop;
        public List<Beat> beats = new List<Beat>
        {
            new Beat
            {
                atSeconds = 0f,
                jsonlRelativePath = "Assets/A2UISchemeA/Samples/timeline_bench/01_media.v0.8.jsonl",
                label = "媒体条"
            },
            new Beat
            {
                atSeconds = 8f,
                jsonlRelativePath = "Assets/A2UISchemeA/Samples/timeline_bench/02_low_battery.v0.8.jsonl",
                label = "低电建议"
            },
            new Beat
            {
                atSeconds = 16f,
                jsonlRelativePath = "Assets/A2UISchemeA/Samples/timeline_bench/03_poi_list.v0.8.jsonl",
                label = "POI 列表"
            },
            new Beat
            {
                atSeconds = 24f,
                jsonlRelativePath = "Assets/A2UISchemeA/Samples/timeline_bench/04_rest.v0.8.jsonl",
                label = "休息横幅"
            }
        };

        Action<string, string> _apply;
        Coroutine _co;

        public void Bind(Action<string, string> applyJsonl) => _apply = applyJsonl;

        public void StartTimeline()
        {
            if (_apply == null)
            {
                var host = GetComponent<A2uiLauncherSurfaceHost>();
                if (host != null)
                    _apply = host.ApplyJsonl;
            }

            if (_apply == null)
            {
                Debug.LogWarning("[A2uiTimeline] no apply callback");
                return;
            }

            if (_co != null) StopCoroutine(_co);
            _co = StartCoroutine(Run());
        }

        public void StopTimeline()
        {
            if (_co != null) StopCoroutine(_co);
            _co = null;
        }

        IEnumerator Run()
        {
            do
            {
                var t0 = Time.realtimeSinceStartup;
                var idx = 0;
                while (idx < beats.Count)
                {
                    var beat = beats[idx];
                    var wait = beat.atSeconds - (Time.realtimeSinceStartup - t0);
                    if (wait > 0f)
                        yield return new WaitForSecondsRealtime(wait);

                    PushBeat(beat);
                    idx++;
                }

                if (!loop) break;
                yield return new WaitForSecondsRealtime(2f);
            } while (loop);

            _co = null;
        }

        void PushBeat(Beat beat)
        {
            try
            {
                var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", beat.jsonlRelativePath));
                var text = File.ReadAllText(path);
                var prompt = A2uiSchemeACommandServer.ExtractPrompt(text);
                if (string.IsNullOrEmpty(prompt))
                    prompt = "timeline:" + (beat.label ?? Path.GetFileName(beat.jsonlRelativePath));
                var jsonl = A2uiSchemeACommandServer.StripMetaLines(text);
                Debug.Log($"[A2uiTimeline] t≈{beat.atSeconds:0}s · {beat.label}");
                _apply?.Invoke(prompt, jsonl);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
