using UnityEngine;

namespace EscapeRoom
{
    [CreateAssetMenu(menuName = "Escape Room/Character Database", fileName = "CharacterDatabase")]
    public class CharacterDatabase : ScriptableObject
    {
        public CharacterDefinition[] characters;

        public int Count => characters == null ? 0 : characters.Length;

        public CharacterDefinition Get(int index)
        {
            if (Count == 0) return null;
            return characters[Mathf.Clamp(index, 0, Count - 1)];
        }
    }
}
