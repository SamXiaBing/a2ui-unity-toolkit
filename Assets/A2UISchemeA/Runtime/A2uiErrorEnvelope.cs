using System.IO;
using Newtonsoft.Json.Linq;

namespace A2UISchemeA
{
    /// <summary>
    /// D3：官方形态 client→server error 消息（协议 v0.9 栈）。
    /// 顶层封套 {version, error:{code, surfaceId, message, path}}，path 用 JSON Pointer；
    /// 走独立 SendError 通道，不与 action 通道混用。
    /// </summary>
    public static class A2uiErrorEnvelope
    {
        /// <summary>封套协议版本（与运行时 v0.9 栈样例一致）。</summary>
        public const string Version = "v0.9";

        /// <summary>v0.9.1 规范唯一文档化的校验错误码（"Standard validation error format"）。</summary>
        public const string CodeValidationFailed = "VALIDATION_FAILED";

        /// <summary>构建官方形态 error 封套。四字段按规范全必填：未知 surfaceId 落空串、未知 path 落 "/"。</summary>
        public static JObject Build(string code, string surfaceId, string message, string path)
        {
            return new JObject
            {
                ["version"] = Version,
                ["error"] = new JObject
                {
                    ["code"] = string.IsNullOrEmpty(code) ? CodeValidationFailed : code,
                    ["surfaceId"] = surfaceId ?? "",
                    ["message"] = message ?? "",
                    ["path"] = string.IsNullOrEmpty(path) ? "/" : path
                }
            };
        }

        /// <summary>
        /// 从 JSONL 原文提取首个可定位的 surfaceId（v0.8 四类 + v0.9 三类消息键），
        /// 供 error 封套必填字段使用。坏行跳过；找不到返回 null。
        /// </summary>
        public static string TryExtractSurfaceId(string jsonl)
        {
            if (string.IsNullOrEmpty(jsonl)) return null;
            using var reader = new StringReader(jsonl);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                JObject jo;
                try { jo = JObject.Parse(t); }
                catch { continue; }
                foreach (var key in MessageKeysWithSurfaceId)
                {
                    var sid = (jo[key] as JObject)?["surfaceId"]?.Value<string>();
                    if (!string.IsNullOrEmpty(sid)) return sid;
                }
            }

            return null;
        }

        static readonly string[] MessageKeysWithSurfaceId =
        {
            "surfaceUpdate", "beginRendering", "dataModelUpdate", "deleteSurface",
            "createSurface", "updateComponents", "updateDataModel"
        };
    }
}
