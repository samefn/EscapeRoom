using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Ambiente de una arena. Solo suena el de la arena del jugador local (las otras tres están en la misma escena,
    /// pero a 200 m: se silencian). Quirófano = zumbido fluorescente; Cuarto de máquinas = drone grave.
    /// Más una capa de tensión muy baja en las dos.
    /// </summary>
    public class ArenaAmbience : MonoBehaviour
    {
        public float roomVolume = 0.35f;
        public float tensionVolume = 0.12f;

        ArenaSlot _arena;
        AudioSource _room, _tension;
        float _gain;

        void Start()
        {
            _arena = GetComponent<ArenaSlot>();
            if (_arena == null) _arena = GetComponentInParent<ArenaSlot>();
            if (_arena == null) return;

            string roomFile = _arena.map == MapType.Quirofano ? "fluorescent_hum" : "ambient_drone";
            _room = Sfx.MakeLoop(gameObject, roomFile, 0f);
            _tension = Sfx.MakeLoop(gameObject, "ambient_drone", 0f);
            if (_arena.map == MapType.CuartoMaquinas) _tension.enabled = false;   // ya es su ambiente principal
        }

        void Update()
        {
            if (_arena == null) return;
            bool mine = PuzzleContext.Ready && PuzzleContext.LocalSlot == _arena.slotIndex;
            _gain = Mathf.MoveTowards(_gain, mine ? 1f : 0f, Time.unscaledDeltaTime * 0.8f);
            if (_room != null) _room.volume = _gain * roomVolume;
            if (_tension != null && _tension.enabled) _tension.volume = _gain * tensionVolume;
        }
    }
}
