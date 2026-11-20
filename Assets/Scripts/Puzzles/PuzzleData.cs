using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    public enum Role { Cirujano = 0, Operador = 1 }

    /// <summary>
    /// Contenido de los puzzles generado a partir de UNA semilla que comparten los cuatro jugadores
    /// (la fija el host en TeamState). Con el mismo número todos obtienen los mismos códigos sin enviarlos por red.
    /// Usa su propio generador (no UnityEngine.Random) para no depender del estado global.
    /// </summary>
    public static class PuzzleData
    {
        public static readonly string[] OrganNames = { "Corazón", "Pulmón", "Hígado", "Riñón", "Estómago" };
        public static readonly string[] SymbolNames = { "Círculo", "Triángulo", "Cuadrado", "Cruz", "Rombo", "Anillo", "Barras", "Aspa" };
        public static readonly string[] FuseColorNames = { "ROJO", "VERDE", "AZUL" };
        public static readonly Color[] FuseColors = { new Color(0.9f, 0.15f, 0.12f), new Color(0.15f, 0.8f, 0.25f), new Color(0.2f, 0.4f, 0.95f) };

        /// <summary>
        /// Números de cajón que existen en el archivador "SheetRack" del Quirófano. El modelo no tiene el cajón 3 (hay un hueco),
        /// así que la secuencia solo puede elegir entre estos. PuzzleValidator comprueba que todos existen en la arena.
        /// </summary>
        public static readonly int[] DrawerNumbers = { 1, 2, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        /// <summary>Falso en las comprobaciones masivas del editor (evita miles de líneas de log).</summary>
        public static bool Verbose = true;

        public const int TankA = 5, TankB = 3, TankGoal = 4;
        public const int MatchSeconds = 18 * 60;   // tiempo de la partida por equipo (menos las penalizaciones de gas)

        public static int Seed { get; private set; }
        public static bool Ready { get; private set; }

        public static int[] OrganNumber = new int[5];   // número (1-9) de cada órgano en el póster
        public static int[] Q1Order = new int[3];       // órganos que pide el expediente, en orden
        public static int[] Pressure = new int[3];      // presión de las 3 válvulas (1-9)
        public static int[] DrawerSeq = new int[3];     // cajones 1-12, en orden
        public static int[] LegendQ = new int[8];       // símbolo -> dígito, versión del póster (Quirófano)
        public static int[] LegendM = new int[8];       // símbolo -> dígito, versión de las cajas (Cuarto de máquinas)
        public static int[] SymbolsA = new int[3];      // los ve el Cirujano en el proyector (se traducen con LegendM)
        public static int[] SymbolsB = new int[3];      // los ve el Operador en la pantalla (se traducen con LegendQ)
        public static int[] FuseOrder = new int[3];     // orden de los interruptores de colores
        public static int CrowbarLocker;                // taquilla (0-2) que guarda la palanca

        public static string Q1Code
        {
            get { var s = ""; foreach (int o in Q1Order) s += OrganNumber[o]; return s; }
        }

        public static string DoorCode
        {
            get
            {
                var s = "";
                foreach (int sym in SymbolsA) s += LegendM[sym];
                foreach (int sym in SymbolsB) s += LegendQ[sym];
                return s;
            }
        }

        public static string PressureText => $"{Pressure[0]} · {Pressure[1]} · {Pressure[2]}";
        public static string DrawerText => $"{DrawerSeq[0]} · {DrawerSeq[1]} · {DrawerSeq[2]}";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Ready = false; Seed = 0; }

        // ------------------------------------------------------------------ generación

        struct Rng
        {
            uint _s;
            public Rng(int seed) { _s = (uint)seed * 2654435761u + 0x9E3779B9u; if (_s == 0) _s = 1; Next(); Next(); }
            public uint Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
            public int Range(int minInclusive, int maxExclusive) => minInclusive + (int)(Next() % (uint)(maxExclusive - minInclusive));
        }

        static int[] Pick(ref Rng r, int count, int minInclusive, int maxExclusive)
        {
            var pool = new List<int>();
            for (int i = minInclusive; i < maxExclusive; i++) pool.Add(i);
            var res = new int[count];
            for (int i = 0; i < count; i++)
            {
                int k = r.Range(0, pool.Count);
                res[i] = pool[k];
                pool.RemoveAt(k);
            }
            return res;
        }

        public static void Ensure(int seed)
        {
            if (Ready && seed == Seed) return;
            Seed = seed;
            var r = new Rng(seed);

            OrganNumber = Pick(ref r, 5, 1, 10);
            Q1Order = Pick(ref r, 3, 0, 5);

            Pressure = new int[3];
            for (int i = 0; i < 3; i++) Pressure[i] = r.Range(1, 10);

            var di = Pick(ref r, 3, 0, DrawerNumbers.Length);
            DrawerSeq = new[] { DrawerNumbers[di[0]], DrawerNumbers[di[1]], DrawerNumbers[di[2]] };
            LegendQ = Pick(ref r, 8, 0, 10);
            LegendM = Pick(ref r, 8, 0, 10);
            SymbolsA = Pick(ref r, 3, 0, 8);
            SymbolsB = Pick(ref r, 3, 0, 8);
            FuseOrder = Pick(ref r, 3, 0, 3);
            CrowbarLocker = r.Range(0, 3);

            Ready = true;
            if (Verbose) Debug.Log($"[Puzzles] Semilla {seed}: código del armario {Q1Code}, puerta {DoorCode}");
        }
    }

    /// <summary>Símbolos geométricos dibujados por código (sustituyen a los iconos finales: ojo, mano, hueso...).</summary>
    public static class SymbolLibrary
    {
        static readonly Texture2D[] _tex = new Texture2D[8];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { for (int i = 0; i < _tex.Length; i++) _tex[i] = null; }

        public static Texture2D Get(int id)
        {
            id = Mathf.Clamp(id, 0, 7);
            if (_tex[id] == null) _tex[id] = Build(id);
            return _tex[id];
        }

        static Texture2D Build(int id)
        {
            const int N = 64;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;   // -1..1
                    px[y * N + x] = Inside(id, u, v) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        static bool Inside(int id, float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            switch (id)
            {
                case 0: return r < 0.78f;                                              // círculo
                case 1: return v > -0.7f && v < 0.8f && Mathf.Abs(u) < (0.8f - v) * 0.55f + 0.02f;   // triángulo
                case 2: return Mathf.Abs(u) < 0.7f && Mathf.Abs(v) < 0.7f;             // cuadrado
                case 3: return (Mathf.Abs(u) < 0.2f && Mathf.Abs(v) < 0.8f) || (Mathf.Abs(v) < 0.2f && Mathf.Abs(u) < 0.8f);   // cruz
                case 4: return Mathf.Abs(u) + Mathf.Abs(v) < 0.85f;                    // rombo
                case 5: return r < 0.8f && r > 0.48f;                                  // anillo
                case 6: return Mathf.Abs(v) < 0.75f && (Mathf.Abs(u + 0.55f) < 0.14f || Mathf.Abs(u) < 0.14f || Mathf.Abs(u - 0.55f) < 0.14f);   // barras
                default: return Mathf.Abs(Mathf.Abs(u) - Mathf.Abs(v)) < 0.17f && r < 0.9f;   // aspa (X)
            }
        }

        public static void Draw(Rect rect, int id, Color tint)
        {
            var prev = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, Get(id));
            GUI.color = prev;
        }
    }
}
