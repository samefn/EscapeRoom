using UnityEngine;

namespace EscapeRoom
{
    /// <summary>Elección local del jugador (persiste entre ejecuciones).</summary>
    public static class PlayerProfile
    {
        const string Key = "EscapeRoom.CharacterIndex";

        public static int CharacterIndex
        {
            get => PlayerPrefs.GetInt(Key, 0);
            set => PlayerPrefs.SetInt(Key, value);
        }
    }
}
