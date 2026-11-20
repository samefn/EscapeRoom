using Fusion;

namespace EscapeRoom.Net
{
    /// <summary>
    /// Un PlayerSession por jugador, creado por el host con InputAuthority = ese jugador.
    /// Guarda el personaje elegido y si el jugador pulsó "Listo".
    /// Docs: RPC cliente -> host (host-mode-basics/6-remote-procedure-calls) y [Networked] (networked-properties).
    /// </summary>
    public class PlayerSession : NetworkBehaviour
    {
        public static PlayerSession Local { get; private set; }

        [Networked] public int CharacterIndex { get; set; }
        [Networked] public int Ready { get; set; }   // 0 / 1

        public override void Spawned()
        {
            // Sobrevive a los cambios de escena (CharacterSelect -> Game).
            Runner.MakeDontDestroyOnLoad(gameObject);

            if (Object.HasInputAuthority)
            {
                Local = this;
                CharacterSelectUI.Confirmed += OnCharacterConfirmed;

                // Dice al host quién es (para poder devolverle su sitio si se desconecta y vuelve).
                if (Object.HasStateAuthority) FusionLauncher.Instance?.OnIdentified(this, FusionLauncher.ClientId);
                else RPC_Identify(FusionLauncher.ClientId);
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Local == this) Local = null;
            CharacterSelectUI.Confirmed -= OnCharacterConfirmed;
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        public void RPC_Identify(string clientId) => FusionLauncher.Instance?.OnIdentified(this, clientId);

        void OnCharacterConfirmed(int characterIndex)
        {
            // El host (o el modo Single) ya tiene autoridad: aplica directo. Un cliente lo pide por RPC.
            if (Object.HasStateAuthority) Apply(characterIndex);
            else RPC_SetCharacter(characterIndex);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
        public void RPC_SetCharacter(int characterIndex) => Apply(characterIndex);

        void Apply(int characterIndex)
        {
            CharacterIndex = characterIndex;
            Ready = 1;
        }
    }
}
