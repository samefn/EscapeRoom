using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom
{
    /// <summary>
    /// Estilo visual de toda la interfaz (IMGUI), basado en el tema gratuito "Night Terror Lite" para Ren'Py
    /// (paneles agrietados, etiqueta roja, tajo rojo al pasar el ratón, viñeta, grano y arañazos).
    /// Imágenes y fuentes: Assets/Resources/NightTerror/. Licencia: LICENSE_NightTerrorLite.txt (se puede usar en el juego;
    /// no se puede redistribuir el pack suelto). Fuentes: Source Serif 4 e IBM Plex Mono (SIL OFL 1.1).
    /// Si faltan las imágenes o las fuentes, todo sigue funcionando con cajas planas y la fuente por defecto.
    /// </summary>
    public static class NightTerrorUi
    {
        // Paleta del tema original
        public static readonly Color Void = Hex(0x0B0A0C);
        public static readonly Color Ash = Hex(0x1B1719);
        public static readonly Color Bone = Hex(0xDCD3C4);
        public static readonly Color Fog = Hex(0x8C827A);
        public static readonly Color Crack = Hex(0x5C4F4B);
        public static readonly Color Blood = Hex(0xA8101A);
        public static readonly Color Ember = Hex(0xE8353D);

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        public enum FontKind { Serif, Bold, Italic, Mono }

        static readonly Dictionary<string, Texture2D> Tex = new Dictionary<string, Texture2D>();
        static readonly Dictionary<FontKind, Font> Fonts = new Dictionary<FontKind, Font>();
        static readonly Dictionary<string, GUIStyle> Styles = new Dictionary<string, GUIStyle>();
        static GUIStyle _invisibleButton;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Tex.Clear(); Fonts.Clear(); Styles.Clear(); _invisibleButton = null; }

        public static Texture2D Texture(string name)
        {
            if (!Tex.TryGetValue(name, out var t))
            {
                t = Resources.Load<Texture2D>("NightTerror/" + name);
                Tex[name] = t;
            }
            return t;
        }

        public static Font GetFont(FontKind kind)
        {
            if (!Fonts.TryGetValue(kind, out var f))
            {
                string file = kind == FontKind.Bold ? "SourceSerif4-Semibold" : kind == FontKind.Italic ? "SourceSerif4-It" :
                              kind == FontKind.Mono ? "IBMPlexMono-Regular" : "SourceSerif4-Regular";
                f = Resources.Load<Font>("NightTerror/Fonts/" + file);
                Fonts[kind] = f;
            }
            return f;
        }

        // ------------------------------------------------------------------ efectos de parpadeo (como ntl_flicker del tema)

        /// <summary>1 casi siempre; cada ~1,9 s baja un instante (bombilla que se muere).</summary>
        public static float Flicker
        {
            get
            {
                float t = Time.unscaledTime % 1.9f;
                if (t < 0.05f) return 0.35f;
                if (t < 0.12f) return 1f;
                if (t < 0.16f) return 0.6f;
                return 1f;
            }
        }

        // ------------------------------------------------------------------ texto

        /// <summary>Estilo de texto en caché. El color se pasa en cada llamada.</summary>
        public static GUIStyle TextStyle(FontKind kind, int size, TextAnchor align = TextAnchor.MiddleLeft, bool wrap = false)
        {
            string key = $"{kind}|{size}|{align}|{wrap}";
            if (!Styles.TryGetValue(key, out var s))
            {
                s = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = align, wordWrap = wrap, richText = false };
                var f = GetFont(kind);
                if (f != null) s.font = f;
                s.padding = new RectOffset(0, 0, 0, 0);
                Styles[key] = s;
            }
            return s;
        }

        public static void Label(Rect r, string text, int size, Color color, FontKind kind = FontKind.Serif, TextAnchor align = TextAnchor.MiddleLeft, bool wrap = false)
        {
            var s = TextStyle(kind, size, align, wrap);
            s.normal.textColor = color;
            GUI.Label(r, text, s);
        }

        // ------------------------------------------------------------------ cajas de 9 trozos

        // El arte está hecho para 1080 px; la interfaz se escala desde 720, así que los bordes se dividen entre 1,5.
        static GUIStyle Sliced(string tex, int l, int t, int r, int b)
        {
            string key = "slice|" + tex;
            if (!Styles.TryGetValue(key, out var s))
            {
                s = new GUIStyle();
                var texture = Texture(tex);
                s.normal.background = texture;
                int Bd(int v) => Mathf.Max(0, Mathf.RoundToInt(v / 1.5f));
                s.border = new RectOffset(Bd(l), Bd(r), Bd(t), Bd(b));
                Styles[key] = s;
            }
            return s;
        }

        static void DrawSliced(Rect r, string tex, int l, int t, int rr, int b, Color tint, float alpha = 1f)
        {
            if (Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            tint.a *= alpha;
            GUI.color = tint;
            if (Texture(tex) != null) Sliced(tex, l, t, rr, b).Draw(r, GUIContent.none, false, false, false, false);
            else GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        /// <summary>Panel agrietado (relleno oscuro + líneas de grieta). big = marco grande (ventanas).</summary>
        public static void Panel(Rect r, bool big = false, float fillAlpha = 0.94f, Color? fill = null, Color? line = null)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (big)
            {
                DrawSliced(r, "frame_fill", 60, 60, 60, 60, fill ?? Ash, fillAlpha);
                DrawSliced(r, "frame_crack", 60, 60, 60, 60, line ?? Crack);
            }
            else
            {
                DrawSliced(r, "panel_fill", 40, 40, 40, 40, fill ?? Ash, fillAlpha);
                DrawSliced(r, "panel_crack", 40, 40, 40, 40, line ?? Crack);
            }
        }

        /// <summary>Etiqueta roja (como el nombre del personaje en la caja de texto).</summary>
        public static void Tag(Rect r) => DrawSliced(r, "tag", 24, 12, 48, 12, Blood);

        /// <summary>Tajo rojo detrás de un botón en hover.</summary>
        public static void Slash(Rect r, float alpha = 0.9f) => DrawSliced(r, "slash", 40, 0, 40, 0, Blood, alpha * Flicker);

        public static void Underline(Rect r, Color color) => DrawSliced(r, "underline", 12, 0, 12, 0, color, Flicker);

        /// <summary>Cicatriz roja bajo un título.</summary>
        public static void TitleScar(Rect r)
        {
            var t = Texture("title_scar");
            if (t == null || Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = Blood;
            GUI.DrawTexture(r, t, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        // ------------------------------------------------------------------ botones

        /// <summary>Botón de texto con tajo rojo al pasar el ratón. Devuelve true al hacer clic.</summary>
        public static bool Button(Rect r, string text, int size = 26, bool selected = false, bool enabled = true, TextAnchor align = TextAnchor.MiddleCenter)
        {
            if (_invisibleButton == null)
            {
                _invisibleButton = new GUIStyle();
                _invisibleButton.normal.background = null;
            }

            bool hover = enabled && r.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                if (hover) Slash(new Rect(r.x - 4, r.y, r.width + 8, r.height));
                else if (selected) Slash(new Rect(r.x - 4, r.y, r.width + 8, r.height), 0.45f);
            }

            Color c = !enabled ? new Color(Fog.r, Fog.g, Fog.b, 0.45f) : (hover || selected) ? Ember : Fog;
            if (hover && !selected) c = Bone;
            Label(r, text, size, c, FontKind.Serif, align);

            bool prevEnabled = GUI.enabled;
            GUI.enabled = enabled;
            bool clicked = GUI.Button(r, GUIContent.none, _invisibleButton);
            GUI.enabled = prevEnabled;
            if (clicked) EscapeRoom.Puzzles.Sfx.Play2D("menu_click");
            return clicked;
        }

        // ------------------------------------------------------------------ ambiente de pantalla completa

        public static void Vignette(float W, float H, float alpha = 0.6f, Color? tint = null)
        {
            var t = Texture("vignette");
            if (t == null || Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            var c = tint ?? Color.black;
            GUI.color = new Color(c.r, c.g, c.b, alpha);
            GUI.DrawTexture(new Rect(0, 0, W, H), t, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        public static void Cracks(float W, float H, float alpha = 0.55f)
        {
            var t = Texture("cracks");
            if (t == null || Event.current.type != EventType.Repaint) return;
            var prev = GUI.color;
            GUI.color = new Color(Crack.r, Crack.g, Crack.b, alpha);
            GUI.DrawTexture(new Rect(0, 0, W, H), t, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        /// <summary>Grano de película que se mueve (cuatro posiciones, como ntl_grain).</summary>
        public static void Grain(float W, float H, float alpha = 0.08f)
        {
            var t = Texture("noise");
            if (t == null || Event.current.type != EventType.Repaint) return;
            int step = (int)(Time.unscaledTime / 0.07f) % 4;
            float[] ox = { 0f, 0.25f, 0.06f, 0.39f }, oy = { 0f, 0.11f, 0.35f, 0.2f };
            var prev = GUI.color;
            GUI.color = new Color(Bone.r, Bone.g, Bone.b, alpha);
            float tile = 384f / 1.5f;
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, W, H), t, new Rect(ox[step], oy[step], W / tile, H / tile));
            GUI.color = prev;
        }

        /// <summary>Arañazos que aparecen un instante de vez en cuando.</summary>
        public static void Scratches(float W, float H)
        {
            var t = Texture("scratches");
            if (t == null || Event.current.type != EventType.Repaint) return;
            float ph = Time.unscaledTime % 5.3f;
            if (ph < 1.9f || ph > 1.98f) return;
            var prev = GUI.color;
            GUI.color = new Color(Bone.r, Bone.g, Bone.b, 0.35f);
            GUI.DrawTexture(new Rect(0, 0, W, H), t, ScaleMode.StretchToFill);
            GUI.color = prev;
        }

        public static void Backdrop(float W, float H)
        {
            if (Event.current.type == EventType.Repaint)
            {
                var prev = GUI.color;
                GUI.color = Void;
                GUI.DrawTexture(new Rect(0, 0, W, H), Texture2D.whiteTexture);
                GUI.color = prev;
            }
            Cracks(W, H);
            Vignette(W, H, 0.7f);
            Grain(W, H);
            Scratches(W, H);
        }
    }
}
