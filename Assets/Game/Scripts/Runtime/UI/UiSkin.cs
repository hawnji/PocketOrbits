using UnityEngine;

namespace PocketOrbits.UI
{
    /// <summary>
    /// Procedurally built IMGUI styles. Sizes are in density-independent units multiplied by the player's text
    /// scale, so every control stays at least 48dp tall (comfortable touch target) on any screen.
    /// </summary>
    public sealed class UiSkin
    {
        public readonly float U;
        public readonly GUIStyle Title, Heading, Body, Small, Caption, Button, PrimaryButton, Panel, Bar, Tile, Pill, SliderTrack, SliderThumb, Toast, Tag;
        public readonly Texture2D Rounded, Solid;
        public readonly Font Font;

        public static readonly Color Accent = new Color(0.40f, 0.78f, 1f);
        public static readonly Color AccentDark = new Color(0.16f, 0.36f, 0.62f);
        public static readonly Color PanelColor = new Color(0.07f, 0.08f, 0.20f, 0.92f);
        public static readonly Color TextColor = new Color(0.93f, 0.95f, 1f);
        public static readonly Color MutedText = new Color(0.66f, 0.72f, 0.86f);
        public static readonly Color Gold = new Color(1f, 0.84f, 0.35f);

        public UiSkin(float dp, float textScale)
        {
            U = dp * textScale;
            Rounded = RoundedTexture(32, 10);
            Solid = Texture2D.whiteTexture;
            Font = GUI.skin != null ? GUI.skin.font : null;

            Title = Text(34, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor);
            Heading = Text(22, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor);
            Body = Text(17, FontStyle.Normal, TextAnchor.MiddleCenter, TextColor);
            Body.wordWrap = true;
            Small = Text(14, FontStyle.Normal, TextAnchor.MiddleCenter, MutedText);
            Small.wordWrap = true;
            Caption = Text(13, FontStyle.Bold, TextAnchor.MiddleCenter, TextColor);
            Tag = Text(15, FontStyle.Bold, TextAnchor.MiddleCenter, Color.black);
            Tag.normal.background = Rounded;
            Tag.border = new RectOffset(10, 10, 10, 10);

            Button = Boxed(17, FontStyle.Bold, new Color(0.17f, 0.20f, 0.38f), TextColor);
            PrimaryButton = Boxed(19, FontStyle.Bold, Accent, new Color(0.03f, 0.06f, 0.16f));
            Tile = Boxed(22, FontStyle.Bold, new Color(0.15f, 0.18f, 0.36f), TextColor);
            Tile.alignment = TextAnchor.UpperCenter;
            Tile.padding = new RectOffset((int)(6 * U), (int)(6 * U), (int)(10 * U), (int)(6 * U));
            Pill = Boxed(15, FontStyle.Bold, new Color(0.17f, 0.20f, 0.38f), TextColor);
            Panel = new GUIStyle { normal = { background = Rounded }, border = new RectOffset(12, 12, 12, 12) };
            Bar = new GUIStyle { normal = { background = Solid } };
            Toast = Boxed(16, FontStyle.Normal, new Color(0.05f, 0.05f, 0.12f, 0.9f), TextColor);
            Toast.wordWrap = true;
            Toast.padding = new RectOffset((int)(14 * U), (int)(14 * U), (int)(10 * U), (int)(10 * U));

            SliderTrack = new GUIStyle { normal = { background = Rounded }, border = new RectOffset(12, 12, 12, 12), fixedHeight = 12 * U, margin = new RectOffset(0, 0, (int)(18 * U), 0) };
            SliderThumb = new GUIStyle { normal = { background = Rounded }, active = { background = Rounded }, border = new RectOffset(12, 12, 12, 12), fixedWidth = 34 * U, fixedHeight = 34 * U };
        }

        GUIStyle Text(int size, FontStyle style, TextAnchor anchor, Color color)
        {
            var s = new GUIStyle { fontSize = Mathf.RoundToInt(size * U), fontStyle = style, alignment = anchor, richText = true, clipping = TextClipping.Overflow };
            s.normal.textColor = color;
            if (Font != null) s.font = Font;
            return s;
        }

        GUIStyle Boxed(int size, FontStyle fs, Color bg, Color fg)
        {
            var s = Text(size, fs, TextAnchor.MiddleCenter, fg);
            var hover = Color.Lerp(bg, Color.white, 0.12f);
            var press = Color.Lerp(bg, Color.black, 0.2f);
            s.normal.background = Tinted(bg);
            s.hover.background = Tinted(hover);
            s.active.background = Tinted(press);
            s.hover.textColor = fg;
            s.active.textColor = fg;
            s.border = new RectOffset(12, 12, 12, 12);
            s.padding = new RectOffset((int)(10 * U), (int)(10 * U), (int)(6 * U), (int)(6 * U));
            s.clipping = TextClipping.Clip;
            s.wordWrap = true;
            return s;
        }

        Texture2D Tinted(Color c)
        {
            var src = Rounded.GetPixels32();
            var tex = new Texture2D(Rounded.width, Rounded.height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var c32 = (Color32)c;
            for (int i = 0; i < src.Length; i++) src[i] = new Color32(c32.r, c32.g, c32.b, (byte)(src[i].a * c.a));
            tex.SetPixels32(src);
            tex.Apply(false, true);
            return tex;
        }

        static Texture2D RoundedTexture(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UiRounded" };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
