using UnityEngine;

namespace EscapeRoom
{
    /// <summary>Lista de arenas de la escena Game. La rellena la herramienta Escape Room > 1.</summary>
    public class ArenaRegistry : MonoBehaviour
    {
        public ArenaSlot[] slots;

        public ArenaSlot Get(int slotIndex)
        {
            if (slots == null || slots.Length == 0)
                slots = FindObjectsByType<ArenaSlot>(FindObjectsSortMode.None);

            foreach (var s in slots)
                if (s != null && s.slotIndex == slotIndex) return s;
            return null;
        }
    }
}
