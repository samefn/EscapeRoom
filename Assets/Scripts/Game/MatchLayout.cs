namespace EscapeRoom
{
    public enum MatchMode
    {
        OneVsOne,
        TwoVsTwo,
        Test   // prueba de un solo jugador (GameMode.Single)
    }

    public struct PlayerAssignment
    {
        public int team;       // 0 = A, 1 = B  (en 1v1 cada jugador es su propio "equipo")
        public MapType map;
        public int slotIndex;  // arena de la escena Game (0..3)
    }

    /// <summary>
    /// Reglas de reparto de jugadores en arenas. Lógica pura, sin red, para poder probarla sola.
    ///
    /// Arenas de la escena Game:  0 = Quirófano, 1 = Cuarto de máquinas, 2 = Quirófano, 3 = Cuarto de máquinas.
    ///   1v1: los dos jugadores juegan el MISMO mapa (elegido al azar por el host), cada uno en su arena.
    ///   2v2: jugadores 0,1 = equipo A; 2,3 = equipo B. El 1º de cada equipo va al quirófano y el 2º al cuarto de máquinas.
    ///   Test: un solo jugador, en el mapa indicado.
    /// </summary>
    public static class MatchLayout
    {
        public static int PlayerCount(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.OneVsOne: return 2;
                case MatchMode.TwoVsTwo: return 4;
                default: return 1;
            }
        }

        /// <param name="sharedMap">Mapa de la partida en 1v1 y en Test. Se ignora en 2v2.</param>
        public static PlayerAssignment Assign(MatchMode mode, int playerIndex, MapType sharedMap)
        {
            int team, occurrence;
            MapType map;

            if (mode == MatchMode.TwoVsTwo)
            {
                team = playerIndex / 2;
                occurrence = team;          // cada equipo usa su propia copia de cada mapa
                map = (MapType)(playerIndex % 2);
            }
            else
            {
                team = playerIndex;
                occurrence = playerIndex;   // 0 o 1
                map = sharedMap;
            }

            return new PlayerAssignment
            {
                team = team,
                map = map,
                slotIndex = occurrence * 2 + (int)map
            };
        }
    }
}
