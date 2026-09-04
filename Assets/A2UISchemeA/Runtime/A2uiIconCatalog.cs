using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace A2UISchemeA
{
    /// <summary>
    /// Icon.name → 运行时生成的简易图标纹理（可替换为 Figma 导出 Sprite）。
    /// </summary>
    public static class A2uiIconCatalog
    {
        static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        public static readonly string[] BundledNames =
        {
            "home", "settings", "search", "warning", "error", "info", "help",
            "check", "close", "favorite", "star", "notifications", "mail",
            "phone", "lock", "person", "add", "delete", "edit", "refresh",
            "arrowBack", "arrowForward", "locationOn"
        };

        public static bool TryGetTexture(string name, out Texture2D tex)
        {
            name = string.IsNullOrEmpty(name) ? "info" : name;
            if (Cache.TryGetValue(name, out tex) && tex != null)
                return true;

            tex = BuildTexture(name);
            Cache[name] = tex;
            return tex != null;
        }

        public static string ResolveName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "info";
            foreach (var n in BundledNames)
            {
                if (n == name) return name;
            }

            return "info";
        }

        static Texture2D BuildTexture(string name)
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                name = "a2ui-icon-" + name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var bg = new Color(0.12f, 0.16f, 0.22f, 0f);
            var fg = Accent(name);
            var pixels = new Color[s * s];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = bg;

            DrawIcon(pixels, s, name, fg);
            tex.SetPixels(pixels);
            tex.Apply(false, false);
            return tex;
        }

        static Color Accent(string name)
        {
            if (name == "warning" || name == "error")
                return new Color(1f, 0.55f, 0.2f, 1f);
            if (name == "check")
                return new Color(0.4f, 0.85f, 0.45f, 1f);
            if (name == "favorite" || name == "star")
                return new Color(1f, 0.75f, 0.2f, 1f);
            if (name == "delete")
                return new Color(0.95f, 0.35f, 0.35f, 1f);
            return new Color(0.95f, 0.7f, 0.3f, 1f);
        }

        static void DrawIcon(Color[] px, int s, string name, Color fg)
        {
            switch (name)
            {
                case "home":
                    FillTri(px, s, 32, 12, 12, 36, 52, 36, fg);
                    FillRect(px, s, 22, 36, 20, 18, fg);
                    break;
                case "settings":
                    FillCircle(px, s, 32, 32, 10, fg);
                    for (var a = 0; a < 6; a++)
                    {
                        var rad = a * Mathf.PI / 3f;
                        var cx = 32 + Mathf.Cos(rad) * 20;
                        var cy = 32 + Mathf.Sin(rad) * 20;
                        FillCircle(px, s, (int)cx, (int)cy, 5, fg);
                    }
                    break;
                case "search":
                    FillCircleRing(px, s, 26, 26, 14, 3, fg);
                    FillRect(px, s, 36, 36, 14, 4, fg);
                    break;
                case "warning":
                case "error":
                    FillTri(px, s, 32, 10, 10, 52, 54, 52, fg);
                    FillRect(px, s, 30, 24, 4, 14, new Color(0.1f, 0.1f, 0.1f, 1f));
                    FillCircle(px, s, 32, 46, 3, new Color(0.1f, 0.1f, 0.1f, 1f));
                    break;
                case "check":
                    FillRect(px, s, 14, 30, 12, 5, fg);
                    FillRect(px, s, 22, 36, 28, 5, fg);
                    break;
                case "close":
                    FillRect(px, s, 16, 28, 32, 5, fg);
                    FillRect(px, s, 28, 16, 5, 32, fg);
                    break;
                case "favorite":
                case "star":
                    FillCircle(px, s, 32, 28, 14, fg);
                    FillTri(px, s, 32, 48, 18, 28, 46, 28, fg);
                    break;
                case "notifications":
                    FillRect(px, s, 22, 18, 20, 24, fg);
                    FillCircle(px, s, 32, 16, 6, fg);
                    FillCircle(px, s, 32, 48, 4, fg);
                    break;
                case "mail":
                    FillRect(px, s, 12, 20, 40, 28, fg);
                    FillTri(px, s, 32, 36, 12, 20, 52, 20, new Color(0.1f, 0.12f, 0.16f, 1f));
                    break;
                case "phone":
                case "call":
                    FillRect(px, s, 24, 12, 16, 40, fg);
                    FillCircle(px, s, 32, 46, 3, new Color(0.1f, 0.1f, 0.1f, 1f));
                    break;
                case "lock":
                    FillRect(px, s, 18, 28, 28, 22, fg);
                    FillCircleRing(px, s, 32, 24, 10, 3, fg);
                    break;
                case "person":
                case "accountCircle":
                    FillCircle(px, s, 32, 22, 10, fg);
                    FillCircle(px, s, 32, 48, 16, fg);
                    break;
                case "add":
                    FillRect(px, s, 28, 14, 8, 36, fg);
                    FillRect(px, s, 14, 28, 36, 8, fg);
                    break;
                case "delete":
                    FillRect(px, s, 18, 22, 28, 30, fg);
                    FillRect(px, s, 14, 16, 36, 6, fg);
                    break;
                case "edit":
                    FillRect(px, s, 18, 36, 24, 8, fg);
                    FillTri(px, s, 46, 14, 38, 30, 52, 22, fg);
                    break;
                case "refresh":
                    FillCircleRing(px, s, 32, 32, 16, 4, fg);
                    FillTri(px, s, 48, 18, 42, 30, 54, 30, fg);
                    break;
                case "arrowBack":
                    FillTri(px, s, 16, 32, 36, 16, 36, 48, fg);
                    FillRect(px, s, 32, 28, 18, 8, fg);
                    break;
                case "arrowForward":
                    FillTri(px, s, 48, 32, 28, 16, 28, 48, fg);
                    FillRect(px, s, 14, 28, 18, 8, fg);
                    break;
                case "locationOn":
                    FillCircle(px, s, 32, 24, 12, fg);
                    FillTri(px, s, 32, 52, 20, 28, 44, 28, fg);
                    break;
                case "info":
                case "help":
                default:
                    FillCircle(px, s, 32, 32, 22, fg);
                    FillRect(px, s, 30, 18, 4, 4, new Color(0.1f, 0.1f, 0.1f, 1f));
                    FillRect(px, s, 30, 26, 4, 18, new Color(0.1f, 0.1f, 0.1f, 1f));
                    break;
            }
        }

        static void FillRect(Color[] px, int s, int x, int y, int w, int h, Color c)
        {
            for (var j = y; j < y + h; j++)
            for (var i = x; i < x + w; i++)
                Set(px, s, i, j, c);
        }

        static void FillCircle(Color[] px, int s, int cx, int cy, int r, Color c)
        {
            var r2 = r * r;
            for (var j = cy - r; j <= cy + r; j++)
            for (var i = cx - r; i <= cx + r; i++)
            {
                var dx = i - cx;
                var dy = j - cy;
                if (dx * dx + dy * dy <= r2)
                    Set(px, s, i, j, c);
            }
        }

        static void FillCircleRing(Color[] px, int s, int cx, int cy, int r, int t, Color c)
        {
            var r2 = r * r;
            var i2 = (r - t) * (r - t);
            for (var j = cy - r; j <= cy + r; j++)
            for (var i = cx - r; i <= cx + r; i++)
            {
                var dx = i - cx;
                var dy = j - cy;
                var d = dx * dx + dy * dy;
                if (d <= r2 && d >= i2)
                    Set(px, s, i, j, c);
            }
        }

        static void FillTri(Color[] px, int s, int x0, int y0, int x1, int y1, int x2, int y2, Color c)
        {
            var minX = Mathf.Min(x0, Mathf.Min(x1, x2));
            var maxX = Mathf.Max(x0, Mathf.Max(x1, x2));
            var minY = Mathf.Min(y0, Mathf.Min(y1, y2));
            var maxY = Mathf.Max(y0, Mathf.Max(y1, y2));
            for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
            {
                if (PointInTri(x, y, x0, y0, x1, y1, x2, y2))
                    Set(px, s, x, y, c);
            }
        }

        static bool PointInTri(int px, int py, int x0, int y0, int x1, int y1, int x2, int y2)
        {
            var d1 = Sign(px, py, x0, y0, x1, y1);
            var d2 = Sign(px, py, x1, y1, x2, y2);
            var d3 = Sign(px, py, x2, y2, x0, y0);
            var hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
            var hasPos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(hasNeg && hasPos);
        }

        static float Sign(int px, int py, int x0, int y0, int x1, int y1) =>
            (px - x1) * (y0 - y1) - (x0 - x1) * (py - y1);

        static void Set(Color[] px, int s, int x, int y, Color c)
        {
            if (x < 0 || y < 0 || x >= s || y >= s) return;
            // Texture2D y=0 is bottom; flip for intuitive draw
            px[(s - 1 - y) * s + x] = c;
        }
    }
}
