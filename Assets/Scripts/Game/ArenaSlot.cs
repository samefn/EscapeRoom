using UnityEngine;

namespace EscapeRoom
{
    public enum MapType
    {
        Quirofano = 0,       // Scene 1 - mapa de ESCAPE
        CuartoMaquinas = 1   // Scene 2 - mapa de APAGAR la trampa
    }

    /// <summary>
    /// Una arena = una copia de un mapa dentro de la escena "Game".
    /// Cada jugador usa una arena propia; nunca comparte arena con otro.
    /// Es un componente local (sin red): los puzzles no necesitan NetworkObject.
    /// </summary>
    public class ArenaSlot : MonoBehaviour
    {
        public MapType map;
        public int slotIndex;
        public Transform spawnPoint;

        public Vector3 SpawnPosition => spawnPoint != null ? spawnPoint.position : transform.position;
        public Quaternion SpawnRotation => spawnPoint != null ? spawnPoint.rotation : transform.rotation;
    }
}
