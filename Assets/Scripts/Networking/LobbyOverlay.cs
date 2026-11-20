using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EscapeRoom.Net
{
    /// <summary>
    /// Panel de sala (IMGUI) que acompaña al jugador mientras está conectado: código de sala, quién ha entrado,
    /// quién ya eligió personaje y botón Salir. Va en el mismo objeto que FusionLauncher (DontDestroyOnLoad).
    /// </summary>
    public class LobbyOverlay : MonoBehaviour
    {
        GUIStyle _code, _line, _small;
        float _copiedUntil;

        void Styles()
        {
            if (_code != null) return;
            _code = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 30, fontStyle = FontStyle.Bold };
            _line = new GUIStyle(GUI.skin.label) { fontSize = 15 };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            _code.normal.textColor = Color.white;
            _line.normal.textColor = Color.white;
            _small.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

            // Estilo Night Terror
            var serif = NightTerrorUi.GetFont(NightTerrorUi.FontKind.Serif);
            var bold = NightTerrorUi.GetFont(NightTerrorUi.FontKind.Bold);
            if (serif != null) { _line.font = serif; _small.font = serif; }
            if (bold != null) _code.font = bold;
            _code.normal.textColor = NightTerrorUi.Ember;
            _line.normal.textColor = NightTerrorUi.Bone;
            _small.normal.textColor = NightTerrorUi.Fog;
        }

        void OnGUI()
        {
            var launcher = FusionLauncher.Instance;
            if (launcher == null || !launcher.InSession) return;
            Styles();
            UiScale.Begin(out float W, out float H);

            string scene = SceneManager.GetActiveScene().name;
            bool selecting = scene == "CharacterSelect";

            float pw = 270f;
            // En la selección va a la izquierda para no tapar al personaje de la derecha; en partida, a la derecha.
            float x = selecting ? 12f : W - pw - 12f;
            float y = 12f;
            float ph = selecting ? 120f + 24f * 4 + 40f : 100f;
            NightTerrorUi.Panel(new Rect(x, y, pw, ph), false, 0.88f);
            x += 10f;
            y += 6f;
            float iw = pw - 20f;

            if (launcher.IsSingle)
            {
                GUI.Label(new Rect(x, y, iw, 24), "Prueba de un jugador", _line);
                y += 26;
            }
            else
            {
                GUI.Label(new Rect(x, y, iw, 22), "Código de sala  (" + launcher.RegionLabel + ")", _line);
                y += 22;
                GUI.Label(new Rect(x, y, iw, 40), launcher.RoomCode, _code);
                y += 42;
                if (NightTerrorUi.Button(new Rect(x, y, iw / 2 - 3, 28), Time.unscaledTime < _copiedUntil ? "¡Copiado!" : "Copiar", 18))
                {
                    GUIUtility.systemCopyBuffer = launcher.RoomCode;
                    _copiedUntil = Time.unscaledTime + 1.5f;
                }
                if (NightTerrorUi.Button(new Rect(x + iw / 2 + 3, y, iw / 2 - 3, 28), "Salir", 18)) launcher.Leave();
                y += 34;
            }

            if (launcher.IsSingle && NightTerrorUi.Button(new Rect(x, y, iw, 28), "Salir", 18)) launcher.Leave();

            if (selecting && !launcher.IsSingle)
            {
                var sessions = FindObjectsByType<PlayerSession>(FindObjectsSortMode.None)
                    .Where(s => s.Object != null && s.Object.IsValid)
                    .OrderBy(s => s.Object.InputAuthority.RawEncoded)
                    .ToList();

                int max = launcher.Runner != null ? launcher.Runner.SessionInfo.MaxPlayers : 0;
                if (max <= 0) max = MatchLayout.PlayerCount(launcher.Mode);

                y += 4;
                GUI.Label(new Rect(x, y, iw, 22), $"Jugadores {sessions.Count} / {max}", _line);
                y += 24;
                for (int i = 0; i < sessions.Count && i < 4; i++)
                {
                    var s = sessions[i];
                    string who = $"Jugador {i + 1}" + (s.Object.HasInputAuthority ? " (tú)" : "");
                    string state = s.Ready == 1 ? "listo" : "eligiendo...";
                    GUI.Label(new Rect(x, y, iw, 22), $"{who}  -  {state}", _line);
                    y += 22;
                }
                if (sessions.Count < max)
                {
                    GUI.Label(new Rect(x, ph - 44f + 12f, iw, 40), "Esperando jugadores. Pasa el código a tus amigos.", _small);
                }
            }
            else if (!selecting && !launcher.IsSingle)
            {
                // En partida: el panel se queda mínimo (código + salir ya dibujados arriba)
            }

            UiScale.End();
        }
    }
}
