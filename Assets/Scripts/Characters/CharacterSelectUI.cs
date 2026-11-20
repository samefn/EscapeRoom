using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace EscapeRoom
{
    /// <summary>
    /// Selección de personaje sobre la escena CharacterSelect (los 4 personajes de "New Scene").
    /// Es 100% local: no depende de Fusion. La capa de red escucha el evento Confirmed
    /// y envía el índice elegido al host (ver Assets/Scripts/Networking~/PlayerSession.cs).
    ///
    /// Los botones siguen el orden de IZQUIERDA a DERECHA en pantalla (según la cámara), sin importar
    /// el orden del array "previews". Confirmed entrega el índice del personaje en CharacterDatabase.
    /// Controles: A/D o flechas para cambiar, Enter para confirmar, o botones en pantalla.
    /// </summary>
    public class CharacterSelectUI : MonoBehaviour
    {
        public CharacterDatabase database;
        [Tooltip("previews[i] debe ser el modelo del personaje i de CharacterDatabase.")]
        public Transform[] previews;
        public float selectedScale = 1.25f;
        public float spinSpeed = 40f;

        public static event Action<int> Confirmed;

        struct Slot
        {
            public Transform t;
            public int dbIndex;
            public Vector3 baseScale;
            public Quaternion baseRot;
        }

        Slot[] _slots = new Slot[0];

        /// <summary>Posición del botón/personaje seleccionado, de izquierda a derecha.</summary>
        public int Index { get; private set; }
        /// <summary>Índice en CharacterDatabase del personaje seleccionado (es lo que se envía por red).</summary>
        public int SelectedCharacter => _slots.Length == 0 ? 0 : _slots[Index].dbIndex;
        public bool Locked { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Confirmed = null;

        void Start()
        {
            var list = new List<Slot>();
            if (previews != null)
            {
                for (int i = 0; i < previews.Length; i++)
                {
                    if (previews[i] == null) continue;
                    list.Add(new Slot
                    {
                        t = previews[i],
                        dbIndex = i,
                        baseScale = previews[i].localScale,
                        baseRot = previews[i].localRotation
                    });
                }
            }

            // Orden visual: de izquierda a derecha desde el punto de vista de la cámara.
            var cam = Camera.main;

            // Fondo negro: la selección no debe enseñar el skybox de Unity (la iluminación de los personajes se mantiene).
            var bgCam = cam != null ? cam : FindFirstObjectByType<Camera>();
            if (bgCam != null)
            {
                bgCam.clearFlags = CameraClearFlags.SolidColor;
                bgCam.backgroundColor = Color.black;
            }
            float KeyX(Transform t) => cam != null ? cam.transform.InverseTransformPoint(t.position).x : t.position.x;
            list.Sort((a, b) => KeyX(a.t).CompareTo(KeyX(b.t)));
            _slots = list.ToArray();

            Index = 0;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].dbIndex == PlayerProfile.CharacterIndex) Index = i;
        }

        void Update()
        {
            if (_slots.Length == 0) return;

            var kb = Keyboard.current;
            if (!Locked && kb != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) Step(-1);
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) Step(1);
                if (kb.enterKey.wasPressedThisFrame) Confirm();
            }

            float k = 10f * Time.deltaTime;
            for (int i = 0; i < _slots.Length; i++)
            {
                var s = _slots[i];
                bool sel = i == Index;
                s.t.localScale = Vector3.Lerp(s.t.localScale, sel ? s.baseScale * selectedScale : s.baseScale, k);
                s.t.localRotation = sel
                    ? s.t.localRotation * Quaternion.Euler(0f, spinSpeed * Time.deltaTime, 0f)
                    : Quaternion.Slerp(s.t.localRotation, s.baseRot, k);
            }
        }

        public void Select(int slot)
        {
            if (!Locked && slot >= 0 && slot < _slots.Length) Index = slot;
        }

        /// <summary>Selecciona por índice de CharacterDatabase (no por posición en pantalla).</summary>
        public void SelectByCharacter(int dbIndex)
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].dbIndex == dbIndex) { Select(i); return; }
        }

        void Step(int d) => Select((Index + d + _slots.Length) % _slots.Length);

        public void Confirm()
        {
            if (Locked || _slots.Length == 0) return;
            Locked = true;
            int db = SelectedCharacter;
            PlayerProfile.CharacterIndex = db;
            Debug.Log($"[CharacterSelect] Confirmado: botón {Index + 1} -> personaje {db} ({NameOf(db)}), modelo '{_slots[Index].t.name}'");
            Confirmed?.Invoke(db);
        }

        string NameOf(int dbIndex)
        {
            var def = database != null ? database.Get(dbIndex) : null;
            return def != null && !string.IsNullOrEmpty(def.displayName) ? def.displayName : $"Personaje {dbIndex + 1}";
        }

        void OnGUI()
        {
            if (_slots.Length == 0) return;
            EscapeRoom.Net.UiScale.Begin(out float W, out float H);

            // Vignette suave y título (estilo Night Terror)
            NightTerrorUi.Vignette(W, H, 0.55f);
            NightTerrorUi.Grain(W, H, 0.05f);
            NightTerrorUi.Label(new Rect(0, 18, W, 56), "Elige tu personaje", 40, NightTerrorUi.Ember, NightTerrorUi.FontKind.Bold, TextAnchor.MiddleCenter);
            NightTerrorUi.TitleScar(new Rect((W - 420f) / 2f, 72f, 420f, 12f));

            const float w = 170f, h = 46f, gap = 10f;
            float total = _slots.Length * w + (_slots.Length - 1) * gap;
            float x0 = (W - total) * 0.5f;
            float y = H - 150f;

            GUI.enabled = !Locked;
            for (int i = 0; i < _slots.Length; i++)
            {
                var r = new Rect(x0 + i * (w + gap), y, w, h);
                NightTerrorUi.Panel(r, false, 0.85f, null, i == Index ? NightTerrorUi.Ember : NightTerrorUi.Crack);
                if (NightTerrorUi.Button(r, NameOf(_slots[i].dbIndex), 21, i == Index, !Locked)) Select(i);
            }

            var ok = new Rect((W - 230f) * 0.5f, y + h + 14f, 230f, 50f);
            NightTerrorUi.Panel(ok, false, 0.9f, null, NightTerrorUi.Blood);
            if (NightTerrorUi.Button(ok, "Listo", 28, false, !Locked)) Confirm();
            GUI.enabled = true;

            if (Locked)
                NightTerrorUi.Label(new Rect(0, y + h + 74f, W, 30), "Esperando a los demás jugadores...", 20, NightTerrorUi.Fog, NightTerrorUi.FontKind.Italic, TextAnchor.MiddleCenter);
            EscapeRoom.Net.UiScale.End();
        }
    }
}
