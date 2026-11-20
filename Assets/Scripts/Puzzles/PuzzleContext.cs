using System.Collections.Generic;
using EscapeRoom.Net;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>Posiciones de la tabla de banderas de red del equipo (TeamState.Flags).</summary>
    public static class TeamKeys
    {
        public const int Count = 16;
        public const int Solved = 0;            // un bit por puzzle (ver Bit)
        public const int Finished = 7;          // 1 = el equipo escapó
        public static int TubeItem(Role to) => to == Role.Cirujano ? 1 : 3;   // contenido de la cápsula que VA hacia ese rol
        public static int TubeSeq(Role to) => to == Role.Cirujano ? 2 : 4;    // contador de envíos hacia ese rol
        public static int Stamp(Role r) => r == Role.Cirujano ? 5 : 6;        // instante en que ese rol tiró de su palanca
        public static Role Other(Role r) => r == Role.Cirujano ? Role.Operador : Role.Cirujano;
        public static int Msg(Role from) => from == Role.Cirujano ? 8 : 9;      // último mensaje rápido enviado por ese rol: (contador << 8) | id
    }

    /// <summary>Bit de cada puzzle dentro de TeamKeys.Solved.</summary>
    public static class Bit
    {
        public const int Q1 = 0, Q2 = 1, Q3 = 2, Q4 = 3, Q5 = 4, Q6 = 5;
        public const int M1 = 6, M2 = 7, M3 = 8, M4 = 9, M5 = 10, M6 = 11;
        public const int Count = 12;
        public static readonly string[] Names = { "Q1", "Q2", "Q3", "Q4", "Q5", "Q6", "M1", "M2", "M3", "M4", "M5", "M6" };
    }

    /// <summary>
    /// Lo que los puzzles necesitan del estado compartido del equipo. Lo implementa TeamState (red) y,
    /// si no hay red de equipo, LocalTeamBackend (para probar una sala sola).
    /// </summary>
    public interface ITeamBackend
    {
        bool IsValid { get; }
        int MatchSeed { get; }
        int Get(int key);
        void RequestSolved(int bit);
        void RequestTubeSend(Role to, int payload);
        void RequestTubeTake(Role to);
        void RequestPress(Role role);
        void RequestMessage(Role from, int id);
        /// <summary>Equipo ganador (-1 = todavía nadie).</summary>
        int Winner { get; }
        /// <summary>Puzzles resueltos por el equipo rival (-1 = no se sabe).</summary>
        int RivalSolved { get; }
        /// <summary>Segundos que le quedan al equipo (descontadas las penalizaciones de gas). Puede ser negativo.</summary>
        float TimeLeft { get; }
        /// <summary>El equipo es de un solo jugador (modo 1v1 o prueba): los puzzles cruzados se resuelven con notas de la propia sala.</summary>
        bool Solo { get; }
        /// <summary>Cuántos compañeros del equipo están desconectados ahora mismo.</summary>
        int AwayCount { get; }
        /// <summary>0 = en juego, 1 = el ganador escapó, 2 = se acabó el tiempo.</summary>
        int EndReason { get; }
        void RequestPenalty(int seconds);
    }

    /// <summary>Estado de equipo local para pruebas sin TeamState. El tubo devuelve el objeto al emisor y la palanca "compañera" responde sola.</summary>
    public class LocalTeamBackend : ITeamBackend
    {
        readonly int[] _f = new int[TeamKeys.Count];
        readonly float _start = Time.unscaledTime;
        int _penalty;
        public bool IsValid => true;
        public int MatchSeed => 12345;
        public bool Solo => true;
        public int AwayCount => 0;
        public float TimeLeft => PuzzleData.MatchSeconds - _penalty - (Time.unscaledTime - _start);
        public int EndReason => _f[TeamKeys.Finished] == 1 ? 1 : (TimeLeft <= 0f ? 2 : 0);
        public int Winner => EndReason == 1 ? Mathf.Max(0, PuzzleContext.LocalTeam) : (EndReason == 2 ? 99 : -1);
        public int RivalSolved => -1;
        public void RequestPenalty(int seconds) => _penalty += Mathf.Clamp(seconds, 0, 120);
        public void RequestMessage(Role from, int id) => _f[TeamKeys.Msg(from)] = (((_f[TeamKeys.Msg(from)] >> 8) + 1) << 8) | (id & 0xFF);
        public int Get(int key) => key >= 0 && key < _f.Length ? _f[key] : 0;
        public void RequestSolved(int bit) => _f[TeamKeys.Solved] |= 1 << bit;

        public void RequestTubeSend(Role to, int payload)
        {
            Role self = TeamKeys.Other(to);   // sin compañero: la cápsula vuelve a quien la envió
            _f[TeamKeys.TubeItem(self)] = payload;
            _f[TeamKeys.TubeSeq(self)]++;
        }

        public void RequestTubeTake(Role to) => _f[TeamKeys.TubeItem(to)] = 0;

        public void RequestPress(Role role)
        {
            // Sin compañero: una sola persona basta (los puzzles de la sala ya se exigieron antes de llegar aquí)
            _f[TeamKeys.Stamp(role)] = 1;
            if (_f[TeamKeys.Finished] == 1) return;
            _f[TeamKeys.Finished] = 1;
            _f[TeamKeys.Solved] |= role == Role.Cirujano ? 1 << Bit.Q6 : 1 << Bit.M6;
        }

        public void Tick() { }
    }

    /// <summary>Banderas locales del jugador (progreso dentro de su sala). Se borran al salir de la partida.</summary>
    public static class LocalProgress
    {
        static readonly HashSet<string> Set_ = new HashSet<string>();
        public static bool Has(string key) => string.IsNullOrEmpty(key) || Set_.Contains(key);
        public static void Set(string key) { if (!string.IsNullOrEmpty(key)) Set_.Add(key); }
        public static void Clear() => Set_.Clear();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Set_.Clear();
    }

    /// <summary>Acceso cómodo al estado del equipo.</summary>
    public static class TeamLink
    {
        public static ITeamBackend B => PuzzleContext.Backend;
        public static int Get(int key) => B != null ? B.Get(key) : 0;
        public static bool IsSolved(int bit) => (Get(TeamKeys.Solved) >> bit & 1) == 1;
        public static bool Finished => Get(TeamKeys.Finished) == 1;

        public static void Solve(int bit)
        {
            if (B == null || IsSolved(bit)) return;
            B.RequestSolved(bit);
        }

        /// <summary>Suma segundos de gas al equipo (un fallo, una pista...). Avisa en pantalla.</summary>
        public static void Penalize(int seconds, string reason)
        {
            if (B == null || PuzzleContext.GameOver) return;
            B.RequestPenalty(seconds);
            PuzzleUI.Toast($"{reason}  (+{seconds} s de gas)");
        }
    }

    /// <summary>
    /// Se crea sola. Averigua quién es el jugador local (arena, equipo, rol), engancha el TeamState de su equipo
    /// y prepara la semilla de los puzzles. Los puzzles solo actúan cuando Ready es verdadero y están en la arena local.
    /// </summary>
    public class PuzzleContext : MonoBehaviour
    {
        public static ITeamBackend Backend { get; private set; }
        public static bool Ready { get; private set; }
        public static int LocalSlot { get; private set; } = -1;
        public static int LocalTeam { get; private set; } = -1;
        public static Role LocalRole { get; private set; }
        public static PlayerAvatar LocalAvatar { get; private set; }
        /// <summary>Ya hay un equipo ganador: se acabó la partida para todos.</summary>
        public static bool GameOver => Backend != null && Backend.Winner >= 0;
        public static int RivalSolved { get; private set; } = -1;

        public static float TimeLeft => Backend != null ? Backend.TimeLeft : PuzzleData.MatchSeconds;
        /// <summary>0 = aire limpio, 1 = gas letal. Sube con el tiempo y salta con cada penalización.</summary>
        public static float Gas => Mathf.Clamp01(1f - TimeLeft / PuzzleData.MatchSeconds);
        public static bool Solo => Backend != null && Backend.Solo;
        public static bool PartnerAway => Backend != null && Backend.AwayCount > 0;
        /// <summary>Equipo de uno (1v1, prueba) o compañero desconectado: aparecen las notas que sustituyen al compañero.</summary>
        public static bool EffectiveSolo => Solo || PartnerAway;
        public static int TotalPuzzles => Solo ? 6 : Bit.Count;

        int _prevSolved = -1;
        bool _restoreChecked;
        bool _countdownPlayed;

        float _next;
        float _waitSince = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<PuzzleContext>() != null) return;
            var go = new GameObject("PuzzleContext");
            DontDestroyOnLoad(go);
            go.AddComponent<PuzzleContext>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Backend = null; Ready = false; LocalSlot = -1; LocalTeam = -1; }

        void Update()
        {
            (Backend as LocalTeamBackend)?.Tick();
            if (Ready) { QuickMessages.Poll(); HintSystem.Tick(); }
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.25f;
            Resolve();
        }

        /// <summary>Cuando el compañero resuelve algo se oye un golpe amortiguado por las tuberías.</summary>
        void TrackPartnerProgress()
        {
            int mask = Backend.Get(TeamKeys.Solved);
            if (_prevSolved >= 0 && mask != _prevSolved)
            {
                int added = mask & ~_prevSolved;
                int partnerBits = LocalRole == Role.Cirujano ? 0xFC0 : 0x03F;   // M1-M6 / Q1-Q6
                if ((added & partnerBits) != 0) Sfx.Play2D("knock", 0.55f, 0.1f);
            }
            _prevSolved = mask;
        }

        void Clear()
        {
            Ready = false;
            Backend = null;
            LocalSlot = LocalTeam = -1;
            LocalAvatar = null;
            _waitSince = -1f;
            LocalProgress.Clear();
            Inventory.Clear();
            Notebook.Clear();
            QuickMessages.Clear();
            RivalSolved = -1;
            _prevSolved = -1;
            _countdownPlayed = false;
            _restoreChecked = false;
            HintSystem.Clear();
        }

        void Resolve()
        {
            PlayerAvatar local = null;
            foreach (var pa in FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None))
                if (pa.Object != null && pa.Object.IsValid && pa.Object.HasInputAuthority) { local = pa; break; }

            if (local == null)
            {
                if (Ready || Backend != null || LocalSlot >= 0) Clear();
                return;
            }

            var registry = FindFirstObjectByType<ArenaRegistry>();
            var arena = registry != null ? registry.Get(local.SlotIndex) : null;
            if (arena == null) return;

            if (Backend != null && !Backend.IsValid) Backend = null;   // el TeamState desapareció (cierre de sala)
            LocalAvatar = local;
            LocalSlot = local.SlotIndex;
            LocalTeam = local.Team;
            LocalRole = arena.map == MapType.Quirofano ? Role.Cirujano : Role.Operador;

            // ¿Hay TeamState de mi equipo? (lo crea el host; en el cliente puede tardar unos fotogramas)
            TeamState found = null;
            foreach (var ts in FindObjectsByType<TeamState>(FindObjectsSortMode.None))
                if (ts.Object != null && ts.Object.IsValid && ts.Team == LocalTeam && ts.MatchSeed != 0) { found = ts; break; }

            if (found != null)
            {
                Backend = found;
            }
            else if (Backend == null)
            {
                if (_waitSince < 0f) _waitSince = Time.unscaledTime;
                if (Time.unscaledTime - _waitSince > 3f)
                {
                    Debug.LogWarning("[Puzzles] No hay TeamState: se usa el estado local (el tubo y la palanca final no conectan con el compañero).");
                    Backend = new LocalTeamBackend();
                }
            }

            if (Backend != null)
            {
                PuzzleData.Ensure(Backend.MatchSeed);
                Ready = true;
                if (!_countdownPlayed) { _countdownPlayed = true; Sfx.Play2D("countdown", 0.8f); }
                RivalSolved = Backend.RivalSolved;
                TrackPartnerProgress();

                // Reconexión: si al volver ya hay puzzles resueltos, cada puzzle reconstruye su estado local.
                if (!_restoreChecked)
                {
                    _restoreChecked = true;
                    int mask = Backend.Get(TeamKeys.Solved);
                    if (mask != 0)
                    {
                        Debug.Log($"[Puzzles] Reconexión: se restauran los puzzles ya resueltos (máscara {mask}).");
                        foreach (var p in FindObjectsByType<PuzzleBase>(FindObjectsSortMode.None))
                            if (p.InMyArena) p.Restore(mask);
                    }
                }
            }
        }
    }
}
