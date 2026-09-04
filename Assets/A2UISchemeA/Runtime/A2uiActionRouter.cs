using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace A2UISchemeA
{
    /// <summary>
    /// 内存伪车机服务：媒体播放 + 空调温度（G1 闭环用）。
    /// </summary>
    public class FakeVehicleService
    {
        public System.Collections.Generic.Dictionary<string, bool> PlayingById = new();
        public float Volume = 72f;
        public string TrackId = "track-media";
        public string Title = "夜航星图";
        public string Artist = "银河快线";
        public bool IsPlaying(string id) => PlayingById.TryGetValue(id, out var p) && p;
        public string PlayLabel => IsPlaying(TrackId) ? "暂停" : "播放";

        public float Celsius = 24f;

        public event Action Changed;

        public bool TryApply(string actionName, JObject context, out string detail)
        {
            detail = "";
            if (string.IsNullOrEmpty(actionName))
            {
                detail = "empty action";
                return false;
            }

            switch (actionName)
            {
                case "toggle_play":
                {
                    var id = context?["id"]?.Value<string>();
                    if (string.IsNullOrEmpty(id)) id = TrackId; // 旧全局控件回退
                    PlayingById[id] = !IsPlaying(id);
                    detail = $"id={id} playing={IsPlaying(id)}";
                    Changed?.Invoke();
                    return true;
                }
                case "prev_track":
                    Title = "上一曲·星尘";
                    detail = "prev_track";
                    Changed?.Invoke();
                    return true;
                case "next_track":
                    Title = "下一曲·晨雾";
                    detail = "next_track";
                    Changed?.Invoke();
                    return true;
                case "climate_cooler":
                    Celsius = Mathf.Max(16f, Celsius - 1f);
                    detail = $"celsius={Celsius}";
                    Changed?.Invoke();
                    return true;
                case "climate_warmer":
                    Celsius = Mathf.Min(30f, Celsius + 1f);
                    detail = $"celsius={Celsius}";
                    Changed?.Invoke();
                    return true;
                case "enable_dnd":
                    detail = $"dnd minutes={context?["minutes"]}";
                    Changed?.Invoke();
                    return true;
                case "confirm_yes":
                case "confirm_no":
                case "nav_charge":
                case "dismiss":
                case "choose_route_a":
                case "choose_route_b":
                case "tour_ping":
                case "tour_secondary":
                case "open_help":
                case "ping":
                case "open_modal":
                    detail = "ack:" + actionName;
                    return true;
                default:
                    detail = "unknown action: " + actionName;
                    return false;
            }
        }

        public JObject BuildMediaDataModelUpdate(string surfaceId, string id = null)
        {
            // 只回写单条播放状态（playLabel + playing）到 /media/<id>，标题沿用界面里已有的字面量（Mapper 用 fallback 兜底）。
            // 这样点击某一条「播放」后，只有该条切到「正在播放」/「暂停」，其它条不受影响。
            id ??= TrackId;
            var playing = IsPlaying(id);
            return new JObject
            {
                ["dataModelUpdate"] = new JObject
                {
                    ["surfaceId"] = surfaceId,
                    ["path"] = "/media/" + id,
                    ["contents"] = new JArray
                    {
                        new JObject { ["key"] = "playLabel", ["valueString"] = playing ? "暂停" : "播放" },
                        new JObject { ["key"] = "playing", ["valueBoolean"] = playing }
                    }
                }
            };
        }

        public JObject BuildClimateDataModelUpdate(string surfaceId)
        {
            return new JObject
            {
                ["dataModelUpdate"] = new JObject
                {
                    ["surfaceId"] = surfaceId,
                    ["path"] = "/climate",
                    ["contents"] = new JArray
                    {
                        new JObject { ["key"] = "celsius", ["valueNumber"] = Celsius },
                        new JObject { ["key"] = "tempLabel", ["valueString"] = $"{Celsius:0}°C" }
                    }
                }
            };
        }

    }

    /// <summary>
    /// G1：userAction 白名单路由 → FakeVehicleService → dataModel 补丁。
    /// </summary>
    public class A2uiActionRouter
    {
        readonly FakeVehicleService _svc;
        readonly A2uiV08Processor _processor;
        readonly Action<string> _onLog;

        public FakeVehicleService Service => _svc;

        public A2uiActionRouter(A2uiV08Processor processor, FakeVehicleService svc, Action<string> onLog)
        {
            _processor = processor;
            _svc = svc;
            _onLog = onLog;
        }

        public void Handle(string actionName, JObject context, string surfaceId)
        {
            if (!_svc.TryApply(actionName, context, out var detail))
            {
                _onLog?.Invoke($"Action REJECTED: {detail}");
                return;
            }

            _onLog?.Invoke($"Action OK: {actionName} → {detail}");

            if (actionName is "toggle_play" or "prev_track" or "next_track")
            {
                var id = context?["id"]?.Value<string>();
                var target = ResolveSurface(surfaceId, "media", "cabin");
                if (target != null)
                    _processor.IngestMessage(_svc.BuildMediaDataModelUpdate(target, id));
            }

            if (actionName is "climate_cooler" or "climate_warmer")
            {
                var target = ResolveSurface(surfaceId, "climate");
                if (target != null)
                    _processor.IngestMessage(_svc.BuildClimateDataModelUpdate(target));
            }
        }

        string ResolveSurface(string preferred, params string[] fallbacks)
        {
            if (!string.IsNullOrEmpty(preferred) && _processor.Surfaces.ContainsKey(preferred))
                return preferred;
            foreach (var id in fallbacks)
            {
                if (_processor.Surfaces.ContainsKey(id))
                    return id;
            }

            return null;
        }
    }
}
