using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace A2UISchemeA
{
    /// <summary>
    /// 主题注册表：把“主题键 → 展示名 → 作用域类”集中一处，并支持从 Styles/ 自动发现
    /// 新导入的 USS 皮肤集（约定任一目录下放 FigmaTokens.uss 即视为一套可热切主题）。
    /// Runtime + Editor 共用，保证“场景里 A2UISchemeAHost 的下拉”和“测试发送面板的主题热切”
    /// 按钮永远一致；新增一套 USS 后，两者都会自动多出对应按钮，无需改代码。
    /// </summary>
    [System.Serializable]
    public struct A2uiThemeEntry
    {
        public string Key;        // Host NormalizeTheme 接受的键（ds/a/b/dark/figma-*…）
        public string Label;      // 面板/下拉展示名
        public string ScopeClass; // 组件皮肤作用域类（MakeCraftedSkin 用）
    }

    public static class A2uiThemeRegistry
    {
        const string StylesRoot = "Assets/A2UISchemeA/Styles";

        // 内置主题（手写，稳定顺序在前）
        static readonly A2uiThemeEntry[] Builtin =
        {
            new A2uiThemeEntry { Key = "ds",    Label = "DS 设计系统",      ScopeClass = "ds-root" },
            new A2uiThemeEntry { Key = "a",     Label = "M3 Light",        ScopeClass = "a2ui-token--a" },
            new A2uiThemeEntry { Key = "dark",  Label = "M3 Dark",         ScopeClass = "a2ui-token--dark" },
        };

        static List<A2uiThemeEntry> _cache;
        static int _cacheStamp;

        /// <summary>全部主题（内置 + 自动发现的 FigmaTokens 皮肤集），按展示名排序后返回。</summary>
        public static IList<A2uiThemeEntry> All()
        {
            RefreshIfNeeded();
            return _cache;
        }

        public static A2uiThemeEntry FindByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return Builtin[0];
            RefreshIfNeeded();

            // figma 别名容错先于精确匹配：简写 figma-export、无分隔 figmaexport、
            // 空格 "figma export" 都过不了 NormalizeTheme 的 StartsWith("figma-")
            // 分支，会在 default 里被折叠成 ds。这里在 raw 键上剥分隔符做后缀
            // 匹配解析出规范键；不中才走 NormalizeTheme 精确匹配，最后回退默认。
            var canonical = ResolveFigmaAlias(key);
            if (canonical != null)
            {
                foreach (var e in _cache)
                    if (e.Key == canonical) return e;
            }

            var norm = A2uiSchemeAHost.NormalizeTheme(key);
            foreach (var e in _cache)
                if (e.Key == norm) return e;
            return Builtin[0];
        }

        /// <summary>
        /// figma 别名容错解析：raw 键剥掉全部非字母数字分隔符后与候选键做
        /// 「相等或后缀」匹配（取剥后最短候选 = 贴合度最高），返回规范注册表键。
        /// 用后缀而非包含：只装 FigmaExportDark 一个皮肤时，规范键
        /// figma-figmaexport 不得被 figma-figmaexportdark 劫持（figmaexportdark
        /// 不是 figmafigmaexportdark 的后缀）。不回调 NormalizeTheme，无递归。
        /// raw 键不含 figma 字样、或无候选命中时返回 null。
        /// </summary>
        public static string ResolveFigmaAlias(string rawKey)
        {
            var want = StripSeparators(rawKey);
            if (!want.Contains("figma")) return null;
            RefreshIfNeeded();
            string bestKey = null;
            var bestLen = int.MaxValue;
            foreach (var e in _cache)
            {
                if (!e.Key.StartsWith("figma-")) continue;
                var have = StripSeparators(e.Key);
                if (have.Length < bestLen && have.EndsWith(want))
                {
                    bestKey = e.Key;
                    bestLen = have.Length;
                }
            }
            return bestKey;
        }

        /// <summary>小写化并剥掉全部非字母数字分隔符（-/_/空格），用于主题键容错比对。</summary>
        static string StripSeparators(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>把 NormalizeTheme 之后的键映射成组件皮肤作用域类。</summary>
        public static string ScopeFor(string key) => FindByKey(key).ScopeClass;

        static void RefreshIfNeeded()
        {
            var stamp = GetStamp();
            if (_cache != null && stamp == _cacheStamp) return;
            _cacheStamp = stamp;

            var list = new List<A2uiThemeEntry>(Builtin);

            // 自动发现：Styles/ 下任意子目录含 FigmaTokens.uss 即视为一套可热切皮肤。
            // 键统一为 figma-<目录名>（小写），与 A2uiSchemeAHost.NormalizeTheme 的 figma 分支对应。
            var root = Path.Combine(Application.dataPath, "A2UISchemeA", "Styles");
            if (Directory.Exists(root))
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    var tokenFile = Path.Combine(dir, "FigmaTokens.uss");
                    if (!File.Exists(tokenFile)) continue;

                    var dirName = Path.GetFileName(dir);
                    var key = "figma-" + dirName.ToLowerInvariant();
                    // 避免与内置键撞名（极端情况）
                    if (System.Array.Exists(Builtin, b => b.Key == key)) continue;

                    // 展示名：取目录名，下划线/连字符转空格并首字母大写
                    var label = PrettyName(dirName);

                    list.Add(new A2uiThemeEntry
                    {
                        Key = key,
                        Label = label,
                        ScopeClass = "a2ui-skin--" + key,   // key = figma-<dir> → a2ui-skin--figma-<dir>
                    });
                }
            }

            list.Sort((x, y) => x.Label.CompareTo(y.Label));
            _cache = list;
        }

        static int GetStamp()
        {
            // 用 Styles 目录最近修改时间作为“是否有新 USS 导入”的廉价判据。
            // Editor 下每次 import 会刷新；Runtime（如台架）时间戳在打包时固定，缓存一次即可。
            var root = Path.Combine(Application.dataPath, "A2UISchemeA", "Styles");
            if (!Directory.Exists(root)) return 0;
            var info = new DirectoryInfo(root);
            return (int)(info.LastWriteTimeUtc.Ticks & 0x7FFFFFFF);
        }

        static string PrettyName(string dirName)
        {
            var s = dirName.Replace('_', ' ').Replace('-', ' ').Trim();
            if (s.Length == 0) return "Figma";
            // 拆分 CamelCase / PascalCase：FigmaExport → Figma Export
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (i > 0 && char.IsUpper(c) &&
                    (char.IsLower(s[i - 1]) || (i + 1 < s.Length && char.IsLower(s[i + 1]))))
                    sb.Append(' ');
                sb.Append(c);
            }
            var result = sb.ToString().Trim();
            if (result.Length == 0) return "Figma";
            return char.ToUpperInvariant(result[0]) + result.Substring(1);
        }

        /// <summary>
        /// 返回所有自动发现的 Figma 皮肤 USS 文件路径（FigmaTokens.uss + FigmaComponents.uss），
        /// 路径为 Assets/ 相对路径，供 Host 在编辑器下用 AssetDatabase 加载。
        /// 这样新导入一套 USS，面板/场景长出按钮的同时，文档也能真正加载到这份皮肤。
        /// </summary>
        public static List<string> DiscoveredStylePaths()
        {
            var root = Path.Combine(Application.dataPath, "A2UISchemeA", "Styles");
            var paths = new List<string>();
            if (!Directory.Exists(root)) return paths;
            foreach (var dir in Directory.GetDirectories(root))
            {
                string Rel(string abs) => "Assets" + abs.Substring(Application.dataPath.Length).Replace('\\', '/');
                var tokenFile = Path.Combine(dir, "FigmaTokens.uss");
                var compFile = Path.Combine(dir, "FigmaComponents.uss");
                if (File.Exists(tokenFile)) paths.Add(Rel(tokenFile));
                if (File.Exists(compFile)) paths.Add(Rel(compFile));
            }
            return paths;
        }
    }
}
