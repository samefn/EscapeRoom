using EscapeRoom.Net;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EscapeRoom
{
    /// <summary>
    /// Detecta qué Interactable apunta el jugador (rayo desde la cámara, con un margen "cerca de" el objeto)
    /// y dibuja la interfaz: mira, marco del objeto, aviso "[E] verbo nombre" y mensajes.
    /// Se añade sola a la cámara de FollowCamera. Interactuar: tecla E o clic izquierdo.
    /// </summary>
    [RequireComponent(typeof(FollowCamera))]
    public class InteractionSystem : MonoBehaviour
    {
        [Tooltip("Distancia máxima de interacción, en metros.")]
        public float range = 2.6f;
        [Tooltip("Margen de puntería: si el rayo fino falla, se prueba con una esfera de este radio (metros).")]
        public float aimRadius = 0.2f;

        public Interactable Target { get; private set; }

        FollowCamera _fc;
        Camera _cam;
        string _toast;
        float _toastUntil;
        GUIStyle _key, _prompt, _hint, _toastStyle;

        void Awake()
        {
            _fc = GetComponent<FollowCamera>();
            _cam = GetComponent<Camera>();
        }

        void OnEnable() => Interactable.Interacted += OnInteracted;
        void OnDisable() => Interactable.Interacted -= OnInteracted;

        void OnInteracted(Interactable i)
        {
            if (!string.IsNullOrWhiteSpace(i.message))
            {
                _toast = i.message;
                _toastUntil = Time.unscaledTime + 3f;
            }
        }

        void Update()
        {
            var before = Target;
            Target = null;
            if (_fc.target == null || !_fc.MouseCaptured) return;
            if (FollowCamera.ModalOpen || FollowCamera.ModalClosedFrame == Time.frameCount) return;

            Target = FindTarget();
            if (Target != null && Target != before) EscapeRoom.Puzzles.Sfx.Play2D("tick");   // aviso al apuntar un objeto
            if (Target == null) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool pressed = (kb != null && kb.eKey.wasPressedThisFrame) ||
                           (mouse != null && mouse.leftButton.wasPressedThisFrame);
            if (pressed) Target.Interact();
        }

        Interactable FindTarget()
        {
            Vector3 origin = transform.position;
            Vector3 dir = transform.forward;
            const QueryTriggerInteraction ignoreTriggers = QueryTriggerInteraction.Ignore;

            // 1) Rayo fino: lo que tienes justo en el centro.
            Interactable found = null;
            float dist = 0f;
            if (Physics.Raycast(origin, dir, out var hit, range, ~0, ignoreTriggers))
            {
                found = hit.collider.GetComponentInParent<Interactable>();
                dist = hit.distance;
            }

            // 2) Si no hay nada, esfera: apuntar "cerca" del objeto basta. Una pared delante sigue tapando.
            if (found == null && Physics.SphereCast(origin, aimRadius, dir, out hit, range, ~0, ignoreTriggers))
            {
                found = hit.collider.GetComponentInParent<Interactable>();
                dist = hit.distance;
            }

            if (found == null || !found.IsAvailable) return null;
            if (found.maxDistance > 0f && dist > found.maxDistance) return null;
            return found;
        }

        // ------------------------------------------------------------------ interfaz

        void Styles()
        {
            if (_key != null) return;
            _key = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold };
            _key.normal.textColor = Color.white;
            _prompt = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _prompt.normal.textColor = Color.white;
            _hint = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
            _hint.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
            _toastStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 18, wordWrap = true };
            _toastStyle.normal.textColor = Color.white;
        }

        void OnGUI()
        {
            if (_fc == null || _fc.target == null || !_fc.MouseCaptured || FollowCamera.ModalOpen) return;
            Styles();
            float s = UiScale.Begin(out float W, out float H);
            var white = Texture2D.whiteTexture;
            var prev = GUI.color;

            // Mira
            bool aiming = Target != null;
            float size = aiming ? 10f : 5f;
            GUI.color = aiming ? new Color(NightTerrorUi.Ember.r, NightTerrorUi.Ember.g, NightTerrorUi.Ember.b, 0.95f) : new Color(NightTerrorUi.Bone.r, NightTerrorUi.Bone.g, NightTerrorUi.Bone.b, 0.55f);
            GUI.DrawTexture(new Rect(W / 2 - size / 2, H / 2 - size / 2, size, size), white);

            if (aiming)
            {
                DrawFrame(Target, s);

                // Aviso: [E]  Abrir Cajón 3
                string text = $"{Target.verb} {Target.displayName}";
                bool hasHint = !string.IsNullOrWhiteSpace(Target.hint);
                float w = Mathf.Max(260f, 70f + text.Length * 13f);
                float h = hasHint ? 78f : 54f;
                var box = new Rect((W - w) / 2, H / 2 + 56f, w, h);

                GUI.color = Color.white;
                NightTerrorUi.Panel(box, false, 0.88f);
                // Tecla [E] en una etiqueta roja
                NightTerrorUi.Tag(new Rect(box.x + 8, box.y + 11, 46, 32));
                NightTerrorUi.Label(new Rect(box.x + 8, box.y + 11, 40, 32), "E", 22, NightTerrorUi.Bone, NightTerrorUi.FontKind.Bold, TextAnchor.MiddleCenter);
                NightTerrorUi.Label(new Rect(box.x + 66, box.y + 6, w - 76, 38), text, 22, NightTerrorUi.Bone, NightTerrorUi.FontKind.Serif);
                if (hasHint) NightTerrorUi.Label(new Rect(box.x + 66, box.y + 42, w - 76, 28), Target.hint, 14, NightTerrorUi.Fog, NightTerrorUi.FontKind.Italic);
            }

            // Mensaje tras interactuar
            if (!string.IsNullOrEmpty(_toast) && Time.unscaledTime < _toastUntil)
            {
                GUI.color = Color.white;
                var tr = new Rect((W - 560f) / 2, H - 150f, 560f, 62f);
                NightTerrorUi.Panel(tr, false, 0.9f);
                NightTerrorUi.Label(new Rect(tr.x + 24, tr.y, tr.width - 48, tr.height), _toast, 19, NightTerrorUi.Bone, NightTerrorUi.FontKind.Serif, TextAnchor.MiddleCenter, true);
            }

            GUI.color = prev;
            UiScale.End();
        }

        /// <summary>Marco amarillo alrededor del objeto apuntado.</summary>
        void DrawFrame(Interactable target, float s)
        {
            if (_cam == null || !target.TryGetWorldBounds(out Bounds b)) return;

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 sp = _cam.WorldToScreenPoint(corner);
                if (sp.z <= 0.01f) continue;   // detrás de la cámara
                any = true;
                float gx = sp.x / s, gy = (Screen.height - sp.y) / s;
                minX = Mathf.Min(minX, gx); maxX = Mathf.Max(maxX, gx);
                minY = Mathf.Min(minY, gy); maxY = Mathf.Max(maxY, gy);
            }
            if (!any) return;

            // Recorta a pantalla y limita el tamaño para que objetos enormes no tapen todo.
            float sw = Screen.width / s, sh = Screen.height / s;
            minX = Mathf.Clamp(minX, 4f, sw - 4f); maxX = Mathf.Clamp(maxX, 4f, sw - 4f);
            minY = Mathf.Clamp(minY, 4f, sh - 4f); maxY = Mathf.Clamp(maxY, 4f, sh - 4f);
            if (maxX - minX < 24f || maxY - minY < 24f) return;

            var white = Texture2D.whiteTexture;
            GUI.color = new Color(NightTerrorUi.Ember.r, NightTerrorUi.Ember.g, NightTerrorUi.Ember.b, 0.85f);
            const float t = 2f, len = 18f;
            // Cuatro esquinas (no el rectángulo entero: se ve más limpio)
            Corner(white, minX, minY, len, t, 1, 1);
            Corner(white, maxX, minY, len, t, -1, 1);
            Corner(white, minX, maxY, len, t, 1, -1);
            Corner(white, maxX, maxY, len, t, -1, -1);
        }

        static void Corner(Texture2D tex, float x, float y, float len, float t, int dx, int dy)
        {
            GUI.DrawTexture(new Rect(dx > 0 ? x : x - len, dy > 0 ? y : y - t, len, t), tex);
            GUI.DrawTexture(new Rect(dx > 0 ? x : x - t, dy > 0 ? y : y - len, t, len), tex);
        }
    }
}
