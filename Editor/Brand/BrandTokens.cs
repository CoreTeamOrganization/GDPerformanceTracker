// Editor/Brand/BrandTokens.cs
// Game District "Builder Notes" design tokens — cream / navy / gold.
// Vendored copy of the tokens used by CodeShield and MemoryShield. Deliberately
// duplicated (no shared assembly) so this package stays standalone.
// Fonts are loaded lazily from the bundled TTFs in Editor/Brand/Fonts/.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GDPerformanceTracker.Brand
{
    internal static class BrandTokens
    {
        // ── Colors — straight from the design system. Don't invent new ones. ──
        public static readonly Color Cream    = new Color32(238, 237, 230, 255); // #EEEDE6 page bg
        public static readonly Color Navy     = new Color32(14,  26,  51,  255); // #0E1A33 primary text
        public static readonly Color Gold     = new Color32(244, 196, 48,  255); // #F4C430 accent
        public static readonly Color WarmGray = new Color32(107, 107, 102, 255); // #6B6B66 muted text
        public static readonly Color Taupe    = new Color32(211, 209, 199, 255); // #D3D1C7 hairlines
        public static readonly Color Ink      = new Color32(61,  61,  58,  255); // #3D3D3A body
        public static readonly Color Sky      = new Color32(133, 183, 235, 255); // #85B7EB ambient info
        public static readonly Color Overdue  = new Color32(192, 57,  43,  255); // #C0392B failing / warning
        public static readonly Color Shipped  = new Color32(111, 167, 111, 255); // #6FA76F passing / done
        public static readonly Color Amber    = new Color32(200, 140, 20,  255); // attention (OFF values)

        /// <summary>Opaque tint of <paramref name="accent"/> over the cream page ground.</summary>
        public static Color Tint(Color accent, float amount = 0.10f) => Color.Lerp(Cream, accent, amount);

        // ── Type scale ───────────────────────────────────────────────────────
        public const int SizeH1       = 38;  // Fraunces
        public const int SizeH2       = 26;  // Fraunces
        public const int SizeH3       = 18;  // Fraunces
        public const int SizeLede     = 16;  // Fraunces italic
        public const int SizeBody     = 13;  // Inter
        public const int SizeUI       = 12;  // Inter
        public const int SizeEyebrow  = 11;  // Inter bold, upper-case
        public const int SizeFootnote = 10;  // Inter
        public const int SizeStatNum  = 22;  // Fraunces — stat numerals
        public const int SizeMono     = 12;

        // ── Layout signature ─────────────────────────────────────────────────
        public const float GoldBarWidth  = 6f;   // full-height gold left-bar on every window
        public const float Hairline      = 1f;
        public const float PadEdge       = 36f;  // primary section padding
        public const float PadTop        = 24f;
        public const float EyebrowSquare = 7f;   // gold square prefix size
        public static readonly Vector2 HubSize = new Vector2(900f, 600f); // hub windows are fixed

        // ── Fonts — lazy-loaded from the bundled TTFs ────────────────────────
        private const string PackageFonts = "Packages/com.gamedistrict.performance-tracker/Editor/Brand/Fonts/";

        private static Font _fraunces, _frauncesItalic, _inter;
        private static bool _fontsAttempted;

        public static Font Fraunces       { get { EnsureFonts(); return _fraunces; } }
        public static Font FrauncesItalic { get { EnsureFonts(); return _frauncesItalic; } }
        public static Font Inter          { get { EnsureFonts(); return _inter; } }
        public static bool FontsAvailable { get { EnsureFonts(); return _fraunces != null && _inter != null; } }

        private static void EnsureFonts()
        {
            if (_fontsAttempted) return;
            _fontsAttempted = true;
            _fraunces       = LoadFont("Fraunces");
            _frauncesItalic = LoadFont("Fraunces-Italic");
            _inter          = LoadFont("Inter");
            // Silent fallback: if the fonts are missing the UI degrades to the editor font.
        }

        private static Font LoadFont(string name)
        {
            // UPM install: virtual Packages/ path (works for git + registry packages).
            var f = AssetDatabase.LoadAssetAtPath<Font>(PackageFonts + name + ".ttf");
            if (f != null) return f;

            // Imported into Assets/ (e.g. via .unitypackage) or embedded package: search by name.
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Font"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (Path.GetFileNameWithoutExtension(p) != name || !p.Contains("/Brand/Fonts/")) continue;
                f = AssetDatabase.LoadAssetAtPath<Font>(p);
                if (f != null) return f;
            }
            return null;
        }

        // ── Primitive drawing ────────────────────────────────────────────────
        private static readonly Dictionary<uint, Texture2D> _solid = new Dictionary<uint, Texture2D>();

        /// <summary>1×1 texture of a solid color (cached). Use for GUIStyle backgrounds.</summary>
        public static Texture2D SolidTex(Color color)
        {
            Color32 c = color;
            uint key = (uint)(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24));
            if (_solid.TryGetValue(key, out var t) && t != null) return t;

            t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, color);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            _solid[key] = t;
            return t;
        }

        public static void Fill(Rect r, Color c)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, SolidTex(Color.white));
            GUI.color = prev;
        }

        public static void HairlineH(float x, float y, float w, Color c) => Fill(new Rect(x, y, w, Hairline), c);
        public static void HairlineV(float x, float y, float h, Color c) => Fill(new Rect(x, y, Hairline, h), c);

        public static void Outline(Rect r, Color c)
        {
            HairlineH(r.x, r.y, r.width, c);
            HairlineH(r.x, r.y + r.height - 1, r.width, c);
            HairlineV(r.x, r.y, r.height, c);
            HairlineV(r.x + r.width - 1, r.y, r.height, c);
        }

        // ── Style factory (cached; treat results as immutable) ───────────────
        private static readonly Dictionary<(int, int, uint, FontStyle, TextAnchor, bool), GUIStyle> _styles =
            new Dictionary<(int, int, uint, FontStyle, TextAnchor, bool), GUIStyle>();

        public static GUIStyle MakeStyle(Font font, int size, Color color,
            FontStyle fontStyle = FontStyle.Normal, TextAnchor anchor = TextAnchor.UpperLeft)
            => CachedStyle(font, size, color, fontStyle, anchor, false);

        public static GUIStyle MakeWrappedStyle(Font font, int size, Color color,
            FontStyle fontStyle = FontStyle.Normal)
            => CachedStyle(font, size, color, fontStyle, TextAnchor.UpperLeft, true);

        private static GUIStyle CachedStyle(Font font, int size, Color color, FontStyle fontStyle, TextAnchor anchor, bool wrap)
        {
            Color32 c = color;
            uint packed = (uint)(c.r | (c.g << 8) | (c.b << 16) | (c.a << 24));
            var key = (font != null ? font.GetInstanceID() : 0, size, packed, fontStyle, anchor, wrap);
            if (_styles.TryGetValue(key, out var cached)) return cached;

            var s = new GUIStyle();
            if (font != null) s.font = font;
            s.fontSize  = size;
            s.fontStyle = fontStyle;
            s.alignment = anchor;
            s.wordWrap  = wrap;
            s.richText  = false;
            s.normal.textColor = color;
            s.hover.textColor  = color;
            s.active.textColor = color;
            s.focused.textColor = color;
            _styles[key] = s;
            return s;
        }
    }
}
