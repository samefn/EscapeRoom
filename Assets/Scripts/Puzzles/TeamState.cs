using EscapeRoom.Puzzles;
using Fusion;
using UnityEngine;

namespace EscapeRoom.Net
{
    /// <summary>
    /// Estado compartido de UN equipo (un NetworkObject por equipo, creado por el host al cargar Game).
    /// Guarda solo lo que cruza de una sala a la otra: puzzles resueltos, la cápsula del tubo neumático
    /// y la palanca final sincronizada. El contenido de las pistas NO viaja: sale de la semilla (PuzzleData).
    ///
    /// Los jugadores piden cambios con RPC (cualquier jugador -> StateAuthority, es decir, el host) y el host
    /// valida y escribe en Flags. Los demás lo leen del estado replicado.
    /// Docs: Remote Procedure Calls (RpcSources / RpcTargets / RpcHostMode) y Network Collections (NetworkArray + Capacity).
    /// </summary>
    public class TeamState : NetworkBehaviour, ITeamBackend
    {
        public const int SyncWindowSeconds = 5;

        [Networked] public int Team { get; set; }
        [Networked] public int Seed { get; set; }
        /// <summary>Equipo ganador + 1 (0 = nadie todavía). El host lo escribe en TODOS los TeamState a la vez.</summary>
        [Networked] public int WinnerPlus1 { get; set; }
        /// <summary>Tick (del host) en que empezó la partida del equipo: de aquí sale el reloj compartido.</summary>
        [Networked] public int StartTick { get; set; }
        /// <summary>Segundos de gas acumulados por fallos, pistas y desconexiones.</summary>
        [Networked] public int PenaltySeconds { get; set; }
        /// <summary>1 = equipo de un solo jugador (1v1).</summary>
        [Networked] public int SoloFlag { get; set; }
        /// <summary>Compañeros desconectados ahora mismo.</summary>
        [Networked] public int AwayCountNet { get; set; }
        /// <summary>0 = en juego, 1 = el ganador escapó, 2 = se acabó el tiempo.</summary>
        [Networked] public int EndReasonNet { get; set; }

        [Networked, Capacity(16)] NetworkArray<int> Flags => default;

        public int MatchSeed => Seed;
        public bool IsValid => Object != null && Object.IsValid;
        public int Winner => IsValid ? WinnerPlus1 - 1 : -1;
        public bool Solo => IsValid && SoloFlag == 1;
        public int AwayCount => IsValid ? AwayCountNet : 0;
        public int EndReason => IsValid ? EndReasonNet : 0;

        /// <summary>Tiempo que le queda al equipo: partida de 18 min menos gas. El reloj usa los ticks de la simulación (Runner.Tick).</summary>
        public float TimeLeft
        {
            get
            {
                if (!IsValid || Runner == null || StartTick == 0) return PuzzleData.MatchSeconds;
                float elapsed = (Runner.Tick.Raw - StartTick) / (float)Runner.TickRate;
                return PuzzleData.MatchSeconds - PenaltySeconds - elapsed;
            }
        }

        /// <summary>Puzzles resueltos por el otro equipo (lee el TeamState rival, que también está replicado).</summary>
        public int RivalSolved
        {
            get
            {
                foreach (var ts in FindObjectsByType<TeamState>(FindObjectsSortMode.None))
                {
                    if (ts == this || !ts.IsValid) continue;
                    int mask = ts.Get(TeamKeys.Solved), n = 0;
                    for (int b = 0; b < Bit.Count; b++) n += (mask >> b) & 1;
                    return n;
                }
                return -1;
            }
        }

        public int Get(int key)
        {
            if (!IsValid || key < 0 || key >= TeamKeys.Count) return 0;
            return Flags[key];
        }

        // ------------------------------------------------------------------ peticiones (las llama cualquier jugador)

        public void RequestSolved(int bit) => RPC_Solved(bit);
        public void RequestTubeSend(Role to, int payload) => RPC_TubeSend((int)to, payload);
        public void RequestTubeTake(Role to) => RPC_TubeTake((int)to);
        public void RequestPress(Role role) => RPC_Press((int)role);
        public void RequestMessage(Role from, int id) => RPC_Message((int)from, id);
        public void RequestPenalty(int seconds) => RPC_Penalty(seconds);

        /// <summary>Solo el host: suma gas directamente (desconexiones).</summary>
        public void HostAddPenalty(int seconds) { if (Object.HasStateAuthority) PenaltySeconds += Mathf.Clamp(seconds, 0, 300); }

        // ------------------------------------------------------------------ RPC: se ejecutan en el host

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        void RPC_Solved(int bit)
        {
            if (bit < 0 || bit >= Bit.Count) return;
            Flags.Set(TeamKeys.Solved, Flags[TeamKeys.Solved] | (1 << bit));
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        void RPC_TubeSend(int toRole, int payload)
        {
            var to = (Role)toRole;
            int item = TeamKeys.TubeItem(to);
            if (Flags[item] != 0 || payload == 0) return;   // el tubo transporta una cápsula a la vez
            Flags.Set(item, payload);
            Flags.Set(TeamKeys.TubeSeq(to), Flags[TeamKeys.TubeSeq(to)] + 1);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        void RPC_TubeTake(int toRole) => Flags.Set(TeamKeys.TubeItem((Role)toRole), 0);

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        void RPC_Message(int fromRole, int id)
        {
            int key = TeamKeys.Msg((Role)fromRole);
            int seq = ((Flags[key] >> 8) + 1) & 0xFFFFFF;
            Flags.Set(key, (seq << 8) | (id & 0xFF));
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        void RPC_Penalty(int seconds)
        {
            if (WinnerPlus1 != 0) return;
            PenaltySeconds += Mathf.Clamp(seconds, 0, 120);
        }

        /// <summary>El host vigila el reloj: si algún equipo se queda sin tiempo, el gas termina la partida.</summary>
        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || WinnerPlus1 != 0 || StartTick == 0) return;
            if (TimeLeft > 0f) return;

            // Gana quien llegó más lejos (más puzzles resueltos); si hay empate, quien tenga más tiempo (menos penalizaciones).
            TeamState best = null;
            int bestSolved = -1;
            float bestTime = float.MinValue;
            foreach (var ts in FindObjectsByType<TeamState>(FindObjectsSortMode.None))
            {
                if (!ts.IsValid) continue;
                int mask = ts.Get(TeamKeys.Solved), n = 0;
                for (int b = 0; b < Bit.Count; b++) n += (mask >> b) & 1;
                float left = ts.TimeLeft;
                if (n > bestSolved || (n == bestSolved && left > bestTime)) { best = ts; bestSolved = n; bestTime = left; }
            }
            if (best == null) return;
            foreach (var ts in FindObjectsByType<TeamState>(FindObjectsSortMode.None))
                if (ts.IsValid) { ts.WinnerPlus1 = best.Team + 1; ts.EndReasonNet = 2; }
        }

        bool AnyWinner()
        {
            foreach (var ts in FindObjectsByType<TeamState>(FindObjectsSortMode.None))
                if (ts.IsValid && ts.WinnerPlus1 != 0) return true;
            return false;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        void RPC_Press(int roleInt)
        {
            if (AnyWinner()) return;   // la partida ya terminó
            var role = (Role)roleInt;
            int now = Runner.Tick.Raw;   // ticks del host: misma referencia para los dos
            int window = SyncWindowSeconds * Runner.TickRate;
            int other = Flags[TeamKeys.Stamp(TeamKeys.Other(role))];

            // Equipo de uno, o compañero desconectado: basta con una palanca (los puzzles de la sala ya se exigieron en el cliente).
            bool alone = SoloFlag == 1 || AwayCountNet > 0;
            if (alone || (other != 0 && now - other >= 0 && now - other <= window))
            {
                Flags.Set(TeamKeys.Stamp(Role.Cirujano), 0);
                Flags.Set(TeamKeys.Stamp(Role.Operador), 0);
                int bits = SoloFlag == 1 ? (role == Role.Cirujano ? 1 << Bit.Q6 : 1 << Bit.M6) : (1 << Bit.Q6) | (1 << Bit.M6);
                Flags.Set(TeamKeys.Solved, Flags[TeamKeys.Solved] | bits);
                Flags.Set(TeamKeys.Finished, 1);

                // Gana el primer equipo que termina: el host decide, así no hay empates por latencia.
                foreach (var ts in FindObjectsByType<TeamState>(FindObjectsSortMode.None))
                    if (ts.IsValid) { ts.WinnerPlus1 = Team + 1; ts.EndReasonNet = 1; }
            }
            else
            {
                Flags.Set(TeamKeys.Stamp(role), now == 0 ? 1 : now);
            }
        }
    }
}
