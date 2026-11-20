using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Grupo de objetos que SOLO existen cuando el jugador está sin compañero (1v1, prueba de un jugador o compañero desconectado):
    /// las notas que sustituyen a la información que normalmente daría el otro cuarto. El hijo "Content" se activa o no según el caso.
    /// </summary>
    public class SoloGroup : MonoBehaviour
    {
        Transform _content;
        int _state = -1;

        void Awake()
        {
            _content = transform.Find("Content");
            if (_content != null) _content.gameObject.SetActive(false);
        }

        void Update()
        {
            if (_content == null || !PuzzleContext.Ready) return;
            var arena = GetComponentInParent<ArenaSlot>();
            if (arena == null || arena.slotIndex != PuzzleContext.LocalSlot) return;

            int want = PuzzleContext.EffectiveSolo ? 1 : 0;
            if (want == _state) return;
            _state = want;
            _content.gameObject.SetActive(want == 1);
        }
    }
}
