using System;
using System.Collections.Generic;
using EscapeRoom.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using NT = EscapeRoom.NightTerrorUi;

namespace EscapeRoom.Puzzles
{
    public struct NoteLine
    {
        public string text;
        public int symbol;   // -1 = sin símbolo
        public NoteLine(string text, int symbol = -1) { this.text = text; this.symbol = symbol; }
    }

    /// <summary>
    /// Interfaz de los puzzles (IMGUI, estilo Night Terror): notas, teclado numérico, cuaderno (Tab), mensajes rápidos (T),
    /// inventario (1-5), progreso del equipo y del rival, avisos y pantalla de resultado. Se crea sola.
    /// Mientras hay una ventana abierta el ratón se libera y el jugador no se mueve ni interactúa.
    /// </summary>
    public class PuzzleUI : MonoBehaviour
    {
        enum Mode { None, Note, Keypad, Notebook, Chat, Result }

        static PuzzleUI _inst;
        Mode _mode;
        int _openFrame;
        bool _wasCaptured;

        string _title = "";
        List<NoteLine> _lines = new List<NoteLine>();

        int _digits;
        string _buf = "";
        Func<string, bool> _submit;
        string _feedback = "";
        float _feedbackUntil;

        string _toast = "";
        float _toastUntil;
        bool _cheats;

        // resultado de la partida
        bool _resultShown;
        float _resultAt = -1f;
        bool _resultWon;
        int _resultWinner;
        int _resultReason;
        bool _helpShown;
        bool _diag;
        float _readySince = -1f;

        public static bool Modal => _inst != null && _inst._mode != Mode.None;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (_inst != null) return;
            var go = new GameObject("PuzzleUI");
            DontDestroyOnLoad(go);
            _inst = go.AddComponent<PuzzleUI>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _inst = null; FollowCamera.ModalOpen = false; }

        void Awake()
        {
            _inst = this;
            _cheats = Application.isEditor || Debug.isDebugBuild;
            foreach (var a in Environment.GetCommandLineArgs()) if (a == "-cheats") _cheats = true;
        }

        // ------------------------------------------------------------------ API

        public static void Toast(string message, float seconds = 3.5f)
        {
            if (_inst == null) return;
            _inst._toast = message;
            _inst._toastUntil = Time.unscaledTime + seconds;
        }

        /// <summary>Muestra una nota (texto con símbolos opcionales). Si se da clave, además se guarda en el cuaderno.</summary>
        public static void ShowNote(string title, List<NoteLine> lines, string notebookKey = null, bool sound = true)
        {
            if (_inst == null) return;
            _inst.Open(Mode.Note);
            _inst._title = title;
            _inst._lines = lines;
            if (sound) Sfx.Play2D("page");
            if (!string.IsNullOrEmpty(notebookKey))
            {
                var sb = new System.Text.StringBuilder();
                foreach (var l in lines)
                    sb.AppendLine(l.symbol >= 0 ? $"[{PuzzleData.SymbolNames[l.symbol]}] {l.text}" : l.text);
                Notebook.Add(notebookKey, title, sb.ToString().TrimEnd());
            }
        }

        public static void ShowNote(string title, string body, string notebookKey = null)
        {
            var lines = new List<NoteLine>();
            foreach (var l in body.Split('\n')) lines.Add(new NoteLine(l));
            ShowNote(title, lines, notebookKey);
        }

        /// <summary>Teclado numérico. submit devuelve true si el código es correcto (se cierra solo).</summary>
        public static void OpenKeypad(string title, int digits, Func<string, bool> submit)
        {
            if (_inst == null) return;
            _inst.Open(Mode.Keypad);
            _inst._title = title;
            _inst._digits = digits;
            _inst._buf = "";
            _inst._submit = submit;
            _inst._feedback = "";
        }

        public static void CloseAll() { if (_inst != null) _inst.Close(); }

        // ------------------------------------------------------------------ abrir / cerrar

        void Open(Mode m)
        {
            if (_mode == Mode.None)
            {
                var cam = FindFirstObjectByType<FollowCamera>();
                _wasCaptured = cam != null && cam.MouseCaptured;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            _mode = m;
            _openFrame = Time.frameCount;
            FollowCamera.ModalOpen = true;
        }

        void Close()
        {
            if (_mode == Mode.None) return;
            _mode = Mode.None;
            FollowCamera.ModalOpen = false;
            FollowCamera.ModalClosedFrame = Time.frameCount;
            if (_wasCaptured)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // ------------------------------------------------------------------ entrada

        static readonly Key[] DigitKeys =
        {
            Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9
        };
        static readonly Key[] PadKeys =
        {
            Key.Numpad0, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5, Key.Numpad6, Key.Numpad7, Key.Numpad8, Key.Numpad9
        };
        // Atajos de los 12 mensajes rápidos: 1-9, 0, -, =
        static readonly Key[] ChatKeys =
        {
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0, Key.Minus, Key.Equals
        };
        static readonly string[] ChatKeyLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            if (!PuzzleContext.Ready)
            {
                if (_mode != Mode.None) Close();
                _helpShown = false;
                _readySince = -1f;
                _resultShown = false;
                _resultAt = -1f;
                return;
            }

            if (kb.f3Key.wasPressedThisFrame) _diag = !_diag;

            UpdateResult();

            // Al empezar se explica quién eres, qué tienes que hacer y qué es el cuaderno
            if (_readySince < 0f) _readySince = Time.unscaledTime;
            if (!_helpShown && _mode == Mode.None && Time.unscaledTime - _readySince > 2.5f && FindFirstObjectByType<FollowCamera>() != null)
            {
                _helpShown = true;
                ShowHelp();
                return;
            }

            if (_mode == Mode.None)
            {
                bool justClosed = FollowCamera.ModalClosedFrame == Time.frameCount;
                if (!justClosed && !PuzzleContext.GameOver)
                {
                    if (kb.hKey.wasPressedThisFrame) { ShowHelp(); return; }
                    if (kb.tabKey.wasPressedThisFrame) { Open(Mode.Notebook); _title = "Cuaderno"; Sfx.Play2D("page"); return; }
                    if (kb.tKey.wasPressedThisFrame)
                    {
                        if (PuzzleContext.EffectiveSolo) Toast("No hay compañero con quien hablar en este momento.");
                        else { Open(Mode.Chat); _title = "Mensajes rápidos"; Sfx.Play2D("tick"); }
                        return;
                    }
                }
                for (int i = 0; i < Inventory.Slots; i++)
                    if (kb[DigitKeys[i + 1]].wasPressedThisFrame) Inventory.Select(i);
                var mouse = Mouse.current;
                if (mouse != null && !justClosed)
                {
                    float wheel = mouse.scroll.ReadValue().y;
                    if (wheel > 0.1f) Inventory.Select((Inventory.Selected + Inventory.Slots - 1) % Inventory.Slots);
                    else if (wheel < -0.1f) Inventory.Select((Inventory.Selected + 1) % Inventory.Slots);
                }
                if (_cheats) Cheats(kb);
                return;
            }

            if (Time.frameCount == _openFrame) return;   // la misma pulsación que abrió la ventana no la cierra

            if (kb.escapeKey.wasPressedThisFrame || (kb.tabKey.wasPressedThisFrame && _mode == Mode.Notebook) ||
                (kb.tKey.wasPressedThisFrame && _mode == Mode.Chat))
            {
                Close();
                return;
            }

            switch (_mode)
            {
                case Mode.Note:
                case Mode.Notebook:
                    if (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) Close();
                    break;
                case Mode.Keypad:
                    for (int d = 0; d <= 9; d++)
                        if (kb[DigitKeys[d]].wasPressedThisFrame || kb[PadKeys[d]].wasPressedThisFrame) PressDigit(d);
                    if (kb.backspaceKey.wasPressedThisFrame && _buf.Length > 0) _buf = _buf.Substring(0, _buf.Length - 1);
                    if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Submit();
                    break;
                case Mode.Chat:
                    for (int i = 0; i < ChatKeys.Length; i++)
                        if (kb[ChatKeys[i]].wasPressedThisFrame) { SendQuick(i); break; }
                    break;
            }
        }

        /// <summary>Ventana "Tu misión": rol, objetivo, si hay compañero o notas, y los controles. Se guarda en el cuaderno.</summary>
        void ShowHelp()
        {
            bool cir = PuzzleContext.LocalRole == Role.Cirujano;
            bool solo = PuzzleContext.EffectiveSolo;
            var L = new List<NoteLine>
            {
                new NoteLine(cir ? "Eres el CIRUJANO (Quirófano)." : "Eres el OPERADOR (Cuarto de máquinas)."),
                new NoteLine(cir ? "Meta: abrir la puerta doble de la sala." : "Meta: apagar la máquina de la sala."),
                new NoteLine(cir ? "Hace falta el código del teclado y la palanca." : "Hace falta el panel de fusibles y el interruptor."),
            };
            if (solo)
            {
                L.Add(new NoteLine("Juegas SOLO tu sala (no hay compañero)."));
                L.Add(new NoteLine("Las NOTAS de papel pegadas a las paredes dan"));
                L.Add(new NoteLine("la información que daría tu compañero."));
            }
            else
            {
                L.Add(new NoteLine($"Tu compañero es el {(cir ? "Operador" : "Cirujano")}, en la otra sala."));
                L.Add(new NoteLine("Habladle (o usad la tecla T) para pasaros códigos;"));
                L.Add(new NoteLine("los objetos viajan por el tubo del lavabo."));
            }
            L.Add(new NoteLine("E: interactuar con lo que apuntas."));
            L.Add(new NoteLine("1-5 o rueda: elegir un objeto del inventario."));
            L.Add(new NoteLine("Tab: CUADERNO, donde se guardan las pistas leídas."));
            L.Add(new NoteLine(cir ? "Los cuadros Q1-Q6 de arriba son tus puzzles." : "Los cuadros M1-M6 de arriba son tus puzzles."));
            L.Add(new NoteLine("H: ver esta ayuda otra vez."));
            ShowNote("Tu misión", L, "ayuda", false);
        }

        void SendQuick(int index)
        {
            if (QuickMessages.Send(index)) Close();
        }

        void UpdateResult()
        {
            var b = PuzzleContext.Backend;
            if (b == null || _resultShown) return;
            int w = b.Winner;
            if (w < 0) return;

            bool won = w == PuzzleContext.LocalTeam;
            if (_resultAt < 0f) _resultAt = Time.unscaledTime + (won ? 3.5f : 0.3f);   // el ganador ve abrirse la puerta antes
            if (Time.unscaledTime < _resultAt) return;

            _resultShown = true;
            _resultWon = won;
            _resultWinner = w;
            _resultReason = b.EndReason;
            Open(Mode.Result);
            if (won) Sfx.Play2D("win");
            else { Sfx.Play2D("alarm"); Sfx.Play2D("lose"); }
        }

        void PressDigit(int d)
        {
            if (_buf.Length >= _digits) return;
            _buf += d;
            Sfx.Play2D("click", 0.7f, 0.06f);
        }

        void Submit()
        {
            if (_buf.Length < _digits) { SetFeedback("Faltan cifras."); return; }
            bool ok = _submit != null && _submit(_buf);
            if (ok) { Sfx.Play2D("keypad_ok"); Close(); return; }
            Sfx.Play2D("keypad_denied");
            SetFeedback("Código incorrecto.");
            _buf = "";
        }

        void SetFeedback(string text)
        {
            _feedback = text;
            _feedbackUntil = Time.unscaledTime + 2.5f;
        }

        // ------------------------------------------------------------------ pruebas (editor, build de desarrollo o -cheats)

        void Cheats(Keyboard kb)
        {
            if (kb.f9Key.wasPressedThisFrame)
            {
                // Lo que normalmente llegaría del compañero por el tubo.
                if (PuzzleContext.LocalRole == Role.Cirujano) Inventory.Add(ItemId.Crowbar);
                else Inventory.Add(ItemId.Fuse, 3);
                Toast("[Prueba] Objeto del compañero añadido al inventario.");
            }
            if (kb.f10Key.wasPressedThisFrame)
            {
                Debug.Log($"[Puzzles] Código armario {PuzzleData.Q1Code} | presión {PuzzleData.PressureText} | cajones {PuzzleData.DrawerText} | " +
                          $"puerta {PuzzleData.DoorCode} | fusibles {string.Join(",", Array.ConvertAll(PuzzleData.FuseOrder, i => PuzzleData.FuseColorNames[i]))} | taquilla {PuzzleData.CrowbarLocker + 1}");
                Toast($"[Prueba] Armario {PuzzleData.Q1Code}, puerta {PuzzleData.DoorCode} (más en la consola)", 6f);
            }
        }

        // ------------------------------------------------------------------ dibujo

        static string TeamName(int team) => team == 0 ? "A" : team == 1 ? "B" : (team + 1).ToString();

        void OnGUI()
        {
            if (!PuzzleContext.Ready) return;
            UiScale.Begin(out float W, out float H);

            if (_diag) DrawDiagnostics(H);
            if (_mode == Mode.None || _mode == Mode.Result) DrawHud(W, H);
            if (_mode != Mode.None) DrawWindow(W, H);

            if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime < _toastUntil)
            {
                var r = new Rect((W - 680f) / 2, H - 218f, 680f, 58f);
                NT.Panel(r, false, 0.88f);
                NT.Label(new Rect(r.x + 24, r.y, r.width - 48, r.height), _toast, 20, NT.Bone, NT.FontKind.Serif, TextAnchor.MiddleCenter, true);
            }

            UiScale.End();
        }

        void DrawHud(float W, float H)
        {
            var white = Texture2D.whiteTexture;

            // Ambiente: viñeta y grano suaves sobre la vista de juego
            float gas = PuzzleContext.GameOver ? 0f : PuzzleContext.Gas;
            Color gasTint = PuzzleContext.LocalRole == Role.Cirujano ? new Color(0.18f, 0.3f, 0.06f) : new Color(0.35f, 0.04f, 0.03f);
            NT.Vignette(W, H, Mathf.Clamp01(0.5f + gas * 0.4f), Color.Lerp(Color.black, gasTint, Mathf.Clamp01(gas * 1.6f)));
            NT.Grain(W, H, 0.045f + gas * 0.05f);
            DrawTimer(W, H, gas);

            // Inventario
            float slot = 64f, gap = 8f;
            float total = Inventory.Slots * slot + (Inventory.Slots - 1) * gap;
            float x0 = (W - total) / 2f, y0 = H - 84f;
            for (int i = 0; i < Inventory.Slots; i++)
            {
                var r = new Rect(x0 + i * (slot + gap), y0, slot, slot);
                bool sel = i == Inventory.Selected;
                NT.Panel(r, false, 0.85f, null, sel ? NT.Ember : NT.Crack);
                var st = Inventory.At(i);
                if (st.item != ItemId.None)
                {
                    var prev = GUI.color;
                    GUI.color = Inventory.ColorOf(st.item);
                    GUI.DrawTexture(new Rect(r.x + 17, r.y + 9, slot - 34, slot - 36), white);
                    GUI.color = prev;
                    NT.Label(new Rect(r.x, r.yMax - 20, r.width, 18), st.count > 1 ? $"{Inventory.NameOf(st.item)} x{st.count}" : Inventory.NameOf(st.item),
                        11, NT.Bone, NT.FontKind.Mono, TextAnchor.MiddleCenter);
                }
                NT.Label(new Rect(r.x + 7, r.y + 3, 18, 16), (i + 1).ToString(), 12, sel ? NT.Ember : NT.Fog, NT.FontKind.Mono);
            }

            // Progreso del equipo (arriba, centro) y del rival. En modo individual solo cuentan los puzzles de tu sala.
            bool solo = PuzzleContext.Solo;
            int pips = solo ? 6 : Bit.Count;
            int firstBit = solo && PuzzleContext.LocalRole == Role.Operador ? 6 : 0;
            float pip = 34f, pg = 4f;
            float tw = pips * pip + (pips - 1) * pg;
            float px = (W - tw) / 2f;
            for (int k = 0; k < pips; k++)
            {
                int i = firstBit + k;
                var r = new Rect(px + k * (pip + pg), 10f, pip, 24f);
                bool done = TeamLink.IsSolved(i);
                bool mine = (i < 6) == (PuzzleContext.LocalRole == Role.Cirujano);
                var prev = GUI.color;
                GUI.color = done ? new Color(NT.Blood.r, NT.Blood.g, NT.Blood.b, 0.95f) : new Color(NT.Ash.r, NT.Ash.g, NT.Ash.b, mine ? 0.8f : 0.5f);
                GUI.DrawTexture(r, white);
                GUI.color = prev;
                NT.Label(r, Bit.Names[i], 12, done ? NT.Bone : (mine ? NT.Fog : new Color(NT.Fog.r, NT.Fog.g, NT.Fog.b, 0.55f)), NT.FontKind.Mono, TextAnchor.MiddleCenter);
            }
            string role = (PuzzleContext.LocalRole == Role.Cirujano ? "Cirujano · Quirófano" : "Operador · Cuarto de máquinas") + (PuzzleContext.Solo ? " · individual" : "");
            string rival = PuzzleContext.RivalSolved >= 0 ? $"   ·   Rival: {PuzzleContext.RivalSolved}/{PuzzleContext.TotalPuzzles}" : "";
            NT.Label(new Rect(0, 38f, W, 20f), $"{role}{rival}", 13, NT.Fog, NT.FontKind.Mono, TextAnchor.MiddleCenter);
            NT.Label(new Rect(0, 56f, W, 20f), "E interactuar · Tab cuaderno de pistas · T mensajes · 1-5 objetos · H ayuda", 12,
                new Color(NT.Fog.r, NT.Fog.g, NT.Fog.b, 0.7f), NT.FontKind.Mono, TextAnchor.MiddleCenter);

            DrawMessages(H);

            // Compañero desconectado: puede volver con el código; mientras tanto, modo solitario.
            if (PuzzleContext.PartnerAway && !PuzzleContext.Solo && !PuzzleContext.GameOver)
            {
                var launcher = FusionLauncher.Instance;
                string code = launcher != null && !string.IsNullOrEmpty(launcher.RoomCode) ? launcher.RoomCode : "";
                var box = new Rect((W - 640f) / 2f, 84f, 640f, 58f);
                NT.Panel(box, false, 0.9f, null, NT.Blood);
                NT.Label(new Rect(box.x + 20, box.y, box.width - 40, box.height),
                    "Tu compañero se desconectó. Puede volver con el código" + (code != "" ? " " + code : "") + ". Modo solitario activo.",
                    17, NT.Bone, NT.FontKind.Serif, TextAnchor.MiddleCenter, true);
            }

            // Equipo que terminó: banner
            if (TeamLink.Finished || (PuzzleContext.GameOver && _resultShown))
            {
                bool lost = PuzzleContext.GameOver && !_resultWon && _resultShown;
                var banner = new Rect(0, H * 0.16f, W, 78f);
                var prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(banner, white);
                GUI.color = prev;
                NT.Label(banner, lost ? "DERROTA" : "¡TU EQUIPO ESCAPÓ!", 46, lost ? NT.Fog : NT.Ember, NT.FontKind.Bold, TextAnchor.MiddleCenter);
            }
        }

        /// <summary>F3: estado de la cámara y del avatar local (para entender por qué no se mueve algo).</summary>
        void DrawDiagnostics(float H)
        {
            var cam = FindFirstObjectByType<FollowCamera>();
            var av = PuzzleContext.LocalAvatar;
            string avTxt = av != null ? $"{av.transform.position:0.0}" : "ninguno";
            string camTxt = cam != null ? $"{cam.transform.position:0.0} objetivo={(cam.target != null ? cam.target.name : "NULO")} ratónCapturado={cam.MouseCaptured}" : "NO HAY FollowCamera";
            string s = $"[F3] avatar {avTxt} | cámara {camTxt} | ventana abierta={FollowCamera.ModalOpen} | cursor={Cursor.lockState} | foco={Application.isFocused} | arena {PuzzleContext.LocalSlot} {PuzzleContext.LocalRole}";
            NT.Label(new Rect(14f, H - 36f, 1400f, 22f), s, 13, NT.Bone, NT.FontKind.Mono);

            // Segunda y tercera línea: cámaras que existen, escenas cargadas y entrada de teclado
            var sb = new System.Text.StringBuilder("[F3] cámaras: ");
            foreach (var c in Camera.allCameras)
                sb.Append($"'{c.name}'(escena {c.gameObject.scene.name}, prof {c.depth:0}, pos {c.transform.position:0.0}, rot {c.transform.eulerAngles.y:0}º) ");
            sb.Append($"| escenas cargadas: {UnityEngine.SceneManagement.SceneManager.sceneCount} (activa '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}')");
            NT.Label(new Rect(14f, H - 56f, 1800f, 22f), sb.ToString(), 13, NT.Bone, NT.FontKind.Mono);

            var kb = Keyboard.current;
            string keys = kb != null ? $"W={kb.wKey.isPressed} A={kb.aKey.isPressed} S={kb.sKey.isPressed} D={kb.dKey.isPressed}" : "teclado NULO";
            string vel = av != null && av.Object != null && av.Object.IsValid ? $"entrada enviada {EscapeRoom.Net.FusionLauncher.LastInputDirection:0.00} (llamadas {EscapeRoom.Net.FusionLauncher.InputCalls}) | InputAuthority={av.Object.HasInputAuthority} StateAuthority={av.Object.HasStateAuthority}" : "avatar no válido";
            NT.Label(new Rect(14f, H - 76f, 1800f, 22f), $"[F3] {keys} | {vel}", 13, NT.Bone, NT.FontKind.Mono);
        }

        /// <summary>Reloj de la partida (arriba a la izquierda) con la barra de gas.</summary>
        void DrawTimer(float W, float H, float gas)
        {
            int secs = Mathf.Max(0, Mathf.CeilToInt(PuzzleContext.TimeLeft));
            bool low = secs <= 60 || gas > 0.75f;
            Color c = low ? Color.Lerp(NT.Ember, NT.Bone, 1f - NT.Flicker) : NT.Bone;
            NT.Label(new Rect(14f, 28f, 220f, 40f), $"{secs / 60:00}:{secs % 60:00}", 34, c, NT.FontKind.Mono);

            var bar = new Rect(16f, 70f, 190f, 8f);
            var white = Texture2D.whiteTexture;
            var prev = GUI.color;
            GUI.color = new Color(NT.Ash.r, NT.Ash.g, NT.Ash.b, 0.85f);
            GUI.DrawTexture(bar, white);
            GUI.color = Color.Lerp(new Color(0.45f, 0.6f, 0.2f), NT.Ember, Mathf.Clamp01(gas * 1.4f));
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(gas), bar.height), white);
            GUI.color = prev;
            NT.Label(new Rect(16f, 80f, 200f, 18f), PuzzleContext.LocalRole == Role.Cirujano ? "gas" : "presión", 12, NT.Fog, NT.FontKind.Mono);
        }

        /// <summary>Últimos mensajes rápidos (propios y del compañero), abajo a la izquierda.</summary>
        void DrawMessages(float H)
        {
            int shown = 0;
            float y = H - 100f;
            for (int i = QuickMessages.Log.Count - 1; i >= 0 && shown < 5; i--)
            {
                var e = QuickMessages.Log[i];
                float age = Time.unscaledTime - e.time;
                if (age > 14f) break;
                float a = age > 11f ? 1f - (age - 11f) / 3f : 1f;
                var prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);

                float w = 360f;
                var row = new Rect(24f, y - 34f, w, 32f);
                NT.Panel(row, false, 0.8f * a);
                if (!e.mine)
                {
                    NT.Tag(new Rect(row.x - 6, row.y - 8, 120f, 24f));
                    NT.Label(new Rect(row.x + 10, row.y - 8, 100f, 24f), QuickMessages.PartnerName, 13, NT.Bone, NT.FontKind.Bold);
                }
                NT.Label(new Rect(row.x + 14, row.y + (e.mine ? 0 : 8), row.width - 24, e.mine ? 32f : 24f), (e.mine ? "Tú: " : "") + e.text, 16,
                    e.mine ? NT.Fog : NT.Bone, NT.FontKind.Serif);
                GUI.color = prev;
                y -= e.mine ? 38f : 46f;
                shown++;
            }
        }

        void DrawWindow(float W, float H)
        {
            // Fondo de la ventana: oscurece el juego
            var white = Texture2D.whiteTexture;
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(0, 0, W, H), white);
            GUI.color = prev;
            NT.Vignette(W, H, 0.6f);

            switch (_mode)
            {
                case Mode.Keypad: DrawKeypad(W, H); return;
                case Mode.Chat: DrawChat(W, H); return;
                case Mode.Result: DrawResult(W, H); return;
            }

            // Nota o cuaderno
            var lines = new List<NoteLine>();
            if (_mode == Mode.Notebook)
            {
                if (Notebook.Entries.Count == 0) lines.Add(new NoteLine("Todavía no has anotado ninguna pista."));
                foreach (var e in Notebook.Entries)
                {
                    lines.Add(new NoteLine("▸ " + e.title));
                    foreach (var l in e.text.Split('\n')) lines.Add(new NoteLine("      " + l));
                }
            }
            else lines = _lines;

            float w = 760f, lineH = 38f;
            float h = Mathf.Min(H - 50f, 170f + lines.Count * lineH);
            var panel = new Rect((W - w) / 2f, (H - h) / 2f, w, h);
            NT.Panel(panel, true);
            NT.Label(new Rect(panel.x, panel.y + 26f, panel.width, 44f), _title, 32, NT.Ember, NT.FontKind.Bold, TextAnchor.MiddleCenter);
            NT.TitleScar(new Rect(panel.x + 70f, panel.y + 74f, panel.width - 140f, 10f));

            float y = panel.y + 98f;
            int maxLines = Mathf.FloorToInt((h - 160f) / lineH) + 1;
            for (int i = 0; i < lines.Count && i < maxLines; i++)
            {
                var l = lines[i];
                float tx = panel.x + 64f;
                if (l.symbol >= 0)
                {
                    SymbolLibrary.Draw(new Rect(tx, y + 2f, 32f, 32f), l.symbol, NT.Bone);
                    tx += 48f;
                }
                bool head = l.text.StartsWith("▸");
                NT.Label(new Rect(tx, y, panel.xMax - tx - 56f, lineH), l.text, 21, head ? NT.Ember : NT.Bone,
                    head ? NT.FontKind.Bold : NT.FontKind.Serif);
                y += lineH;
            }

            NT.Label(new Rect(panel.x, panel.yMax - 44f, panel.width, 24f), "[E] o Esc para cerrar", 13, NT.Fog, NT.FontKind.Mono, TextAnchor.MiddleCenter);
        }

        void DrawKeypad(float W, float H)
        {
            float w = 400f, h = 600f;
            var panel = new Rect((W - w) / 2f, (H - h) / 2f, w, h);
            NT.Panel(panel, true);
            NT.Label(new Rect(panel.x, panel.y + 26f, panel.width, 40f), _title, 28, NT.Ember, NT.FontKind.Bold, TextAnchor.MiddleCenter);
            NT.TitleScar(new Rect(panel.x + 50f, panel.y + 68f, panel.width - 100f, 9f));

            var white = Texture2D.whiteTexture;
            float cell = 46f, cg = 6f;
            float dw = _digits * cell + (_digits - 1) * cg;
            float dx = panel.x + (w - dw) / 2f;
            for (int i = 0; i < _digits; i++)
            {
                var r = new Rect(dx + i * (cell + cg), panel.y + 92f, cell, 58f);
                var prev = GUI.color;
                GUI.color = NT.Void;
                GUI.DrawTexture(r, white);
                GUI.color = prev;
                NT.Label(r, i < _buf.Length ? _buf[i].ToString() : "_", 40, i < _buf.Length ? NT.Ember : NT.Crack, NT.FontKind.Mono, TextAnchor.MiddleCenter);
            }

            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "C", "0", "OK" };
            float bw = 92f, bh = 64f, bg = 8f;
            float bx = panel.x + (w - (3 * bw + 2 * bg)) / 2f;
            float by = panel.y + 172f;
            for (int i = 0; i < 12; i++)
            {
                var r = new Rect(bx + (i % 3) * (bw + bg), by + (i / 3) * (bh + bg), bw, bh);
                NT.Panel(r, false, 0.9f);
                if (!NT.Button(r, keys[i], 30)) continue;
                if (keys[i] == "C") _buf = "";
                else if (keys[i] == "OK") Submit();
                else PressDigit(keys[i][0] - '0');
            }

            if (!string.IsNullOrEmpty(_feedback) && Time.unscaledTime < _feedbackUntil)
                NT.Label(new Rect(panel.x, panel.yMax - 70f, panel.width, 26f), _feedback, 18, NT.Ember, NT.FontKind.Italic, TextAnchor.MiddleCenter);
            NT.Label(new Rect(panel.x, panel.yMax - 42f, panel.width, 22f), "Teclado o ratón · Enter = OK · Esc = salir", 12, NT.Fog, NT.FontKind.Mono, TextAnchor.MiddleCenter);
        }

        void DrawChat(float W, float H)
        {
            float w = 760f, h = 560f;
            var panel = new Rect((W - w) / 2f, (H - h) / 2f, w, h);
            NT.Panel(panel, true);
            NT.Label(new Rect(panel.x, panel.y + 24f, panel.width, 40f), "Mensajes rápidos", 30, NT.Ember, NT.FontKind.Bold, TextAnchor.MiddleCenter);
            NT.TitleScar(new Rect(panel.x + 70f, panel.y + 66f, panel.width - 140f, 10f));
            NT.Label(new Rect(panel.x, panel.y + 80f, panel.width, 24f), $"Para tu compañero ({QuickMessages.PartnerName})", 15, NT.Fog, NT.FontKind.Italic, TextAnchor.MiddleCenter);

            float colW = (panel.width - 140f) / 2f;
            for (int i = 0; i < QuickMessages.Phrases.Length; i++)
            {
                int col = i / 6, row = i % 6;
                float x = panel.x + 56f + col * (colW + 28f);
                float y = panel.y + 124f + row * 62f;
                var r = new Rect(x, y, colW, 54f);
                NT.Panel(r, false, 0.8f);
                NT.Label(new Rect(r.x + 12, r.y, 26f, r.height), ChatKeyLabels[i], 18, NT.Ember, NT.FontKind.Mono, TextAnchor.MiddleCenter);
                if (NT.Button(new Rect(r.x + 40, r.y, r.width - 44, r.height), QuickMessages.Phrases[i], 18, false, true, TextAnchor.MiddleLeft)) SendQuick(i);
            }
            NT.Label(new Rect(panel.x, panel.yMax - 40f, panel.width, 22f), "Pulsa el número, o haz clic · T / Esc = cerrar", 13, NT.Fog, NT.FontKind.Mono, TextAnchor.MiddleCenter);
        }

        void DrawResult(float W, float H)
        {
            float w = 700f, h = 470f;
            var panel = new Rect((W - w) / 2f, (H - h) / 2f, w, h);
            NT.Panel(panel, true);
            string headline = _resultWinner == 99 ? "TIEMPO AGOTADO" : (_resultWon ? "¡VICTORIA!" : "DERROTA");
            NT.Label(new Rect(panel.x, panel.y + 34f, panel.width, 80f), headline, _resultWinner == 99 ? 52 : 64, _resultWon ? NT.Ember : NT.Fog, NT.FontKind.Bold, TextAnchor.MiddleCenter);
            NT.TitleScar(new Rect(panel.x + 70f, panel.y + 118f, panel.width - 140f, 10f));

            string msg;
            if (_resultWinner == 99) msg = "El gas acabó con vosotros antes de salir.";
            else if (_resultReason == 2)
                msg = _resultWon ? "Se acabó el tiempo, pero vuestro equipo había llegado más lejos."
                                 : $"Se acabó el tiempo: el equipo {TeamName(_resultWinner)} había llegado más lejos.";
            else
                msg = _resultWon ? $"Tu equipo (equipo {TeamName(PuzzleContext.LocalTeam)}) completó los dos puzzles finales primero."
                                 : $"El equipo {TeamName(_resultWinner)} completó los dos puzzles finales antes que vosotros.";
            NT.Label(new Rect(panel.x + 60f, panel.y + 146f, panel.width - 120f, 70f), msg, 22, NT.Bone, NT.FontKind.Serif, TextAnchor.UpperCenter, true);

            int mine = 0;
            int mask = TeamLink.Get(TeamKeys.Solved);
            for (int bb = 0; bb < Bit.Count; bb++) mine += (mask >> bb) & 1;
            string stats = PuzzleContext.RivalSolved >= 0
                ? $"Puzzles resueltos  ·  tu equipo {mine}/{PuzzleContext.TotalPuzzles}  ·  rival {PuzzleContext.RivalSolved}/{PuzzleContext.TotalPuzzles}"
                : $"Puzzles resueltos  ·  tu equipo {mine}/{PuzzleContext.TotalPuzzles}";
            NT.Label(new Rect(panel.x, panel.y + 236f, panel.width, 26f), stats, 15, NT.Fog, NT.FontKind.Mono, TextAnchor.MiddleCenter);

            if (NT.Button(new Rect(panel.x + 150f, panel.y + 300f, panel.width - 300f, 56f), "Volver al menú", 30))
            {
                Close();
                var launcher = FusionLauncher.Instance;
                if (launcher != null) launcher.Leave();
            }
            if (NT.Button(new Rect(panel.x + 150f, panel.y + 366f, panel.width - 300f, 56f), "Seguir en la sala", 26)) Close();
        }
    }
}
