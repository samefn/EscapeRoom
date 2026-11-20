using System;
using UnityEngine;
using UnityEngine.Events;

namespace EscapeRoom
{
    /// <summary>
    /// Objeto con el que el jugador puede interactuar (cajón, puerta, grifo, válvula...).
    /// Cuando el jugador está cerca y lo apunta, InteractionSystem muestra el aviso "[E] verbo nombre".
    /// Los puzzles se enganchan por el evento onInteract (Inspector) o por Interactable.Interacted (código).
    /// Es local: no usa red, porque cada jugador resuelve su propia arena.
    /// </summary>
    public class Interactable : MonoBehaviour
    {
        [Tooltip("Nombre que se muestra en el aviso.")]
        public string displayName = "Objeto";
        [Tooltip("Verbo del aviso: Abrir, Examinar, Usar, Girar, Leer...")]
        public string verb = "Examinar";
        [Tooltip("Texto opcional bajo el aviso.")]
        public string hint = "";
        [Tooltip("Mensaje que aparece en pantalla al interactuar (vacío = ninguno).")]
        [TextArea] public string message = "";
        [Tooltip("Si es falso, no se puede apuntar ni usar (por ejemplo, ya resuelto).")]
        public bool available = true;
        [Tooltip("Distancia máxima en metros. 0 = la que tenga InteractionSystem.")]
        public float maxDistance = 0f;

        public UnityEvent<Interactable> onInteract = new UnityEvent<Interactable>();

        /// <summary>Se dispara para cualquier interacción (para puzzles y pruebas).</summary>
        public static event Action<Interactable> Interacted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Interacted = null;

        public bool IsAvailable => available && isActiveAndEnabled;

        public void Interact()
        {
            onInteract.Invoke(this);
            Interacted?.Invoke(this);
        }

        /// <summary>Caja que envuelve todos los renderers del objeto (para dibujar el marco de selección).</summary>
        public bool TryGetWorldBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }
    }
}
