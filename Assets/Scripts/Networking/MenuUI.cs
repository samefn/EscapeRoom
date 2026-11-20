using System;
using System.Threading.Tasks;
using UnityEngine;
using NT = EscapeRoom.NightTerrorUi;

namespace EscapeRoom.Net
{
    /// <summary>
    /// Menú principal (IMGUI) de la escena Menu: crear sala, unirse con código y prueba de un jugador.
    /// Usa FusionLauncher.Instance, que sobrevive entre escenas.
    /// </summary>
    public class MenuUI : MonoBehaviour
    {
        enum Page { Main, Create, Join, Test }

        Page _page = Page.Main;
        MatchMode _mode = MatchMode.OneVsOne;
        int _region;
        string _code = "";

        GUIStyle _title, _sub, _label, _status, _field, _btn, _ver;

        void Start()
        {
            _region = PlayerPrefs.GetInt("EscapeRoom.Region", 0);
            _code = PlayerPrefs.GetString("EscapeRoom.LastCode", "");

            // Solo pruebas: abrir directamente una página del menú (-menupage main|create|join|test).
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-menupage");
            if (i >= 0 && i + 1 < args.Length && Enum.TryParse(args[i + 1], true, out Page page)) _page = page;
        }

        async void Run(Func<Task> action)
        {
            try { await action(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        void Styles()
        {
            if (_title != null) return;
            _title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 44, fontStyle = FontStyle.Bold };
            _title.normal.textColor = new Color(0.85f, 0.15f, 0.15f);
            _sub = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 17 };
            _sub.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            _label = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
            _label.normal.textColor = Color.white;
            _status = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 16, wordWrap = true };
            _status.normal.textColor = Color.white;
            _field = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleCenter, fontSize = 30 };
            _field.normal.textColor = Color.white;
            _field.focused.textColor = Color.white;
            _btn = new GUIStyle(GUI.skin.button) { fontSize = 19 };
            _btn.normal.textColor = Color.white;
            _btn.hover.textColor = Color.white;
            _btn.active.textColor = Color.white;
            _ver = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            _ver.normal.textColor = new Color(0.6f, 0.6f, 0.6f);

            // Tipografías y colores del tema Night Terror
            var serif = NT.GetFont(NT.FontKind.Serif);
            var bold = NT.GetFont(NT.FontKind.Bold);
            var mono = NT.GetFont(NT.FontKind.Mono);
            if (serif != null) { _sub.font = serif; _label.font = bold != null ? bold : serif; _status.font = serif; }
            if (mono != null) { _field.font = mono; _ver.font = mono; }
            _label.normal.textColor = NT.Bone;
            _sub.normal.textColor = NT.Fog;
            _status.normal.textColor = NT.Bone;
            _field.normal.textColor = NT.Ember;
            _field.focused.textColor = NT.Ember;
            _field.normal.background = null;
            _field.focused.background = null;
            _field.hover.background = null;
            _ver.normal.textColor = NT.Fog;
        }

        bool Btn(float x, float y, float w, float h, string text, bool selected = false)
        {
            // NT.Button dibuja el tajo rojo al pasar el ratón y reproduce el clic del menú.
            return NT.Button(new Rect(x, y, w, h), text, 26, selected, GUI.enabled);
        }

        void OnGUI()
        {
            var launcher = FusionLauncher.Instance;
            if (launcher == null) return;
            Styles();
            UiScale.Begin(out float W, out float H);

            // Fondo oscuro para que el texto se lea sobre el cielo de la escena.
            NT.Backdrop(W, H);

            const float pw = 460f, bh = 48f, gap = 10f;
            float x = (W - pw) * 0.5f;
            float y = 60f;

            // Título con parpadeo: de vez en cuando se corta y se desplaza un instante (como ntl_title_fx)
            float tt = Time.unscaledTime % 6.1f;
            float glitch = (tt > 2.6f && tt < 2.66f) ? 0.25f : (tt > 2.76f && tt < 2.81f) ? 0.5f : 1f;
            float shift = glitch < 0.3f ? 3f : 0f;
            var tc = NT.Ember; tc.a = glitch;
            NT.Label(new Rect(shift, y - 6, W, 90), "ESCAPE ROOM", 76, tc, NT.FontKind.Bold, TextAnchor.MiddleCenter);
            y += 84;
            NT.TitleScar(new Rect((W - 520f) / 2f, y, 520f, 14f));
            y += 22;
            NT.Label(new Rect(0, y, W, 28), "Termina los dos puzzles antes que tu rival", 19, NT.Fog, NT.FontKind.Italic, TextAnchor.MiddleCenter);
            y += 52;

            GUI.enabled = !launcher.Busy;

            switch (_page)
            {
                case Page.Main:
                    if (Btn(x, y, pw, bh, "Crear sala")) { _page = Page.Create; }
                    y += bh + gap;
                    if (Btn(x, y, pw, bh, "Unirse a una sala")) { _page = Page.Join; }
                    y += bh + gap;
                    if (Btn(x, y, pw, bh, "Prueba de un jugador")) { _page = Page.Test; }
                    y += bh + gap * 2;
                    if (Btn(x, y, pw, bh, "Salir")) Application.Quit();
                    y += bh + gap;
                    break;

                case Page.Create:
                    GUI.Label(new Rect(x, y, pw, 24), "Modo de juego", _label);
                    y += 28;
                    if (Btn(x, y, pw / 2 - 4, bh, "1 vs 1  (2 jugadores)", _mode == MatchMode.OneVsOne)) _mode = MatchMode.OneVsOne;
                    if (Btn(x + pw / 2 + 4, y, pw / 2 - 4, bh, "2 vs 2  (4 jugadores)", _mode == MatchMode.TwoVsTwo)) _mode = MatchMode.TwoVsTwo;
                    y += bh + gap * 2;

                    GUI.Label(new Rect(x, y, pw, 24), "Región del servidor (elige la más cercana a todos)", _label);
                    y += 28;
                    for (int i = 0; i < FusionLauncher.Regions.Length; i++)
                    {
                        float bx = x + (i % 2) * (pw / 2 + 4);
                        float by = y + (i / 2) * (40 + 6);
                        if (Btn(bx, by, pw / 2 - 4, 40, FusionLauncher.Regions[i].label, _region == i)) _region = i;
                    }
                    y += 2 * 46 + gap * 2;

                    if (Btn(x, y, pw, bh + 6, "Crear sala"))
                    {
                        PlayerPrefs.SetInt("EscapeRoom.Region", _region);
                        int region = _region;
                        var mode = _mode;
                        Run(() => launcher.Host(mode, region));
                    }
                    y += bh + 6 + gap;
                    if (Btn(x, y, pw, 40, "Atrás")) _page = Page.Main;
                    y += 40 + gap;
                    break;

                case Page.Join:
                    GUI.Label(new Rect(x, y, pw, 24), "Código de sala (te lo da el host)", _label);
                    y += 28;
                    NT.Panel(new Rect(x, y, pw, 56), false, 0.9f);
                    _code = GUI.TextField(new Rect(x, y, pw, 56), _code, 7, _field).ToUpperInvariant();
                    y += 56 + gap * 2;
                    GUI.enabled = !launcher.Busy && !string.IsNullOrWhiteSpace(_code);
                    if (Btn(x, y, pw, bh + 6, "Unirse"))
                    {
                        PlayerPrefs.SetString("EscapeRoom.LastCode", _code);
                        string code = _code;
                        Run(() => launcher.Join(code));
                    }
                    GUI.enabled = !launcher.Busy;
                    y += bh + 6 + gap;
                    if (Btn(x, y, pw, 40, "Atrás")) _page = Page.Main;
                    y += 40 + gap;
                    break;

                case Page.Test:
                    GUI.Label(new Rect(x, y, pw, 24), "Sin conexión: un solo jugador, para probar el mapa", _label);
                    y += 32;
                    if (Btn(x, y, pw, bh, "Quirófano (escape)")) Run(() => launcher.StartTest(MapType.Quirofano));
                    y += bh + gap;
                    if (Btn(x, y, pw, bh, "Cuarto de máquinas (apagar)")) Run(() => launcher.StartTest(MapType.CuartoMaquinas));
                    y += bh + gap * 2;
                    if (Btn(x, y, pw, 40, "Atrás")) _page = Page.Main;
                    y += 40 + gap;
                    break;
            }
            GUI.enabled = true;

            // Estado / errores
            string text = launcher.Busy && string.IsNullOrEmpty(launcher.Status) ? "Conectando..." : launcher.Status;
            if (!string.IsNullOrEmpty(text))
            {
                var prev = GUI.color;
                GUI.color = launcher.IsError ? new Color(NT.Ember.r, NT.Ember.g, NT.Ember.b) : Color.white;
                GUI.Label(new Rect(x, y + 10, pw, 80), text, _status);
                GUI.color = prev;
            }

            GUI.Label(new Rect(10, H - 26, 400, 22), "v" + Application.version, _ver);
            UiScale.End();
        }
    }
}
