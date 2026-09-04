using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace A2uiSchemeA.Editor
{
    /// <summary>
    /// USS 里的一条属性（name: value;）。带颜色检测，方便编辑器给颜色拾取器。
    /// </summary>
    [Serializable]
    public class UssProperty
    {
        public string Name;
        public string Value;
        public bool IsColor;

        public UssProperty(string name, string value)
        {
            Name = name;
            Value = value;
            IsColor = DetectColor(value);
        }

        public static bool DetectColor(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return false;
            var s = v.Trim();
            return s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) || s.StartsWith("#");
        }

        public static bool TryParseColor(string v, out Color c)
        {
            c = Color.white;
            if (string.IsNullOrWhiteSpace(v)) return false;
            var s = v.Trim();
            if (s.StartsWith("#"))
                return ColorUtility.TryParseHtmlString(s, out c);
            if (s.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
            {
                int a = s.IndexOf('(');
                int b = s.IndexOf(')');
                if (a < 0 || b < 0) return false;
                var parts = s.Substring(a + 1, b - a - 1).Split(',');
                if (parts.Length < 3) return false;
                if (!float.TryParse(parts[0], out var r) ||
                    !float.TryParse(parts[1], out var g) ||
                    !float.TryParse(parts[2], out var bl)) return false;
                c = new Color(r / 255f, g / 255f, bl / 255f, 1f);
                if (parts.Length >= 4 && float.TryParse(parts[3], out var al)) c.a = al;
                return true;
            }
            return false;
        }

        public static string ColorToUss(Color c)
        {
            if (Mathf.Approximately(c.a, 1f))
                return $"rgb({Mathf.RoundToInt(c.r * 255)}, {Mathf.RoundToInt(c.g * 255)}, {Mathf.RoundToInt(c.b * 255)})";
            return $"rgba({Mathf.RoundToInt(c.r * 255)}, {Mathf.RoundToInt(c.g * 255)}, {Mathf.RoundToInt(c.b * 255)}, {c.a:F2})";
        }
    }

    /// <summary>
    /// USS 里的一条规则：选择器 + 若干属性。记录花括号在原文中的索引，写回时只换花括号内部，
    /// 规则之间的空行 / 注释等原样保留。
    /// </summary>
    [Serializable]
    public class UssRule
    {
        public string Selector;
        public List<UssProperty> Properties = new List<UssProperty>();
        public int BraceOpenIndex;
        public int BraceCloseIndex;
    }

    /// <summary>
    /// 把一个 .uss 文本解析成 规则列表，并能在只改其中若干规则的前提下重建文本写回。
    /// </summary>
    public class UssDocument
    {
        public string OriginalText;
        public List<UssRule> Rules = new List<UssRule>();

        public static UssDocument Parse(string text)
        {
            var doc = new UssDocument { OriginalText = text };
            int n = text.Length;
            int i = 0;
            while (i < n)
            {
                int ob = text.IndexOf('{', i);
                if (ob < 0) break;

                // 找匹配的 '}'（按层级计数，防嵌套）
                int depth = 0;
                int cb = -1;
                for (int j = ob; j < n; j++)
                {
                    if (text[j] == '{') depth++;
                    else if (text[j] == '}')
                    {
                        depth--;
                        if (depth == 0) { cb = j; break; }
                    }
                }
                if (cb < 0) break;

                var selector = text.Substring(i, ob - i).Trim();
                var body = text.Substring(ob + 1, cb - ob - 1);
                var rule = new UssRule
                {
                    Selector = selector,
                    BraceOpenIndex = ob,
                    BraceCloseIndex = cb
                };

                foreach (var raw in body.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0) continue;
                    if (line.StartsWith("/*") || line.StartsWith("//")) continue;
                    int colon = line.IndexOf(':');
                    if (colon < 0) continue;
                    var name = line.Substring(0, colon).Trim();
                    if (name.Length == 0) continue;
                    var val = line.Substring(colon + 1).Trim().TrimEnd(';').Trim();
                    int cidx = val.IndexOf("//");
                    if (cidx >= 0) val = val.Substring(0, cidx).Trim();
                    rule.Properties.Add(new UssProperty(name, val));
                }

                doc.Rules.Add(rule);
                i = cb + 1;
            }
            return doc;
        }

        /// <summary>
        /// 按组件 RuleSelector 找到对应规则。Crafted.uss 里规则带 .a2ui-skin--crafted 前缀，
        /// token 文件带 .a2ui-token--* 前缀，这里去掉前缀后再匹配。
        /// </summary>
        public UssRule FindRule(string targetSelector)
        {
            if (string.IsNullOrWhiteSpace(targetSelector)) return null;
            var t = targetSelector.Trim();
            var tPlain = t.TrimStart('.');

            foreach (var r in Rules)
            {
                var s = r.Selector
                    .Replace(".a2ui-skin--crafted", "")
                    .Replace(".a2ui-token--aaos", "")
                    .Replace(".a2ui-token--cloud", "")
                    .Replace(".a2ui-token--ice", "")
                    .Replace(".a2ui-skin--overlay", "")
                    .Trim();
                if (s == t || s == tPlain || s.EndsWith(tPlain, StringComparison.Ordinal))
                    return r;
            }
            foreach (var r in Rules)
                if (r.Selector.Contains(t) || r.Selector.Contains(tPlain))
                    return r;
            return null;
        }

        /// <summary>
        /// 重建文本：规则之间的内容（空行、块外注释）原样保留，只重写每条规则的花括号内部。
        /// </summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            int cursor = 0;
            foreach (var rule in Rules)
            {
                // 从 cursor 到 '{'（含）原样
                sb.Append(OriginalText.Substring(cursor, rule.BraceOpenIndex + 1 - cursor));
                foreach (var p in rule.Properties)
                {
                    if (string.IsNullOrWhiteSpace(p.Name)) continue; // 跳过未命名的占位
                    sb.Append("    ").Append(p.Name).Append(": ").Append(p.Value).Append(";\n");
                }
                // 单独的 '}'
                sb.Append(OriginalText.Substring(rule.BraceCloseIndex, 1));
                cursor = rule.BraceCloseIndex + 1;
            }
            sb.Append(OriginalText.Substring(cursor));
            return sb.ToString();
        }
    }
}
