using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace EscapeRoom.Net
{
    public struct RegionOption
    {
        public string label;   // texto del menú
        public string code;    // código de región de Photon ("" = automática)
        public string prefix;  // prefijo que va dentro del código de sala
    }

    /// <summary>
    /// Arranca Fusion 2 y lleva el flujo de partida (arquitectura PLAYER-HOST):
    ///   GameMode.Single  -> prueba de un jugador (sin red)
    ///   GameMode.Host    -> un jugador crea la sala y hace de host
    ///   GameMode.Client  -> los demás se unen con el código de sala
    /// Flujo: Menu -> CharacterSelect -> (todos "Listo") -> Game, y el host spawnea cada avatar en su arena.
    ///
    /// Salas entre ordenadores distintos: Photon Cloud empareja por SessionName dentro de una REGIÓN. La región
    /// "automática" no es determinista (docs: manual/connection-and-matchmaking/regions), así que la región va
    /// FIJA y codificada en el código de sala ("SA-K7QX"): quien se une la toma del código.
    ///
    /// Vive en la escena Menu y pasa a DontDestroyOnLoad (singleton).
    /// </summary>
    public class FusionLauncher : MonoBehaviour, INetworkRunnerCallbacks
    {
        public const string MenuSceneName = "Menu";
        public const string SelectScenePath = "Assets/Scenes/CharacterSelect.unity";
        public const string GameScenePath = "Assets/Scenes/Game.unity";

        public static readonly RegionOption[] Regions =
        {
            new RegionOption { label = "Sudamérica", code = "sa", prefix = "SA" },
            new RegionOption { label = "EE. UU. (este)", code = "us", prefix = "US" },
            new RegionOption { label = "Europa", code = "eu", prefix = "EU" },
            new RegionOption { label = "Automática", code = "", prefix = "XX" },
        };

        public static FusionLauncher Instance { get; private set; }

        [SerializeField] NetworkObject sessionPrefab;   // PlayerSession
        [SerializeField] NetworkObject avatarPrefab;    // PlayerAvatar
        [SerializeField] NetworkObject teamPrefab;      // TeamState (estado compartido de cada equipo: puzzles, tubo, palanca final)

        /// <summary>Mapa que se usa en la prueba de un jugador.</summary>
        public MapType testMap = MapType.Quirofano;

        public MatchMode Mode { get; private set; }
        public NetworkRunner Runner => _runner;
        public string RoomCode { get; private set; } = "";
        public string RegionLabel { get; private set; } = "";
        public string Status { get; private set; } = "";
        public bool IsError { get; private set; }
        public bool IsHost { get; private set; }
        public bool IsSingle { get; private set; }
        /// <summary>Hay un runner en marcha o conectándose.</summary>
        public bool Busy => _runner != null;
        /// <summary>Conectado a una sala (o en prueba de un jugador).</summary>
        public bool InSession => _runner != null && _runner.IsRunning;

        NetworkRunner _runner;
        readonly Dictionary<PlayerRef, PlayerSession> _sessions = new Dictionary<PlayerRef, PlayerSession>();
        readonly Dictionary<PlayerRef, NetworkObject> _avatars = new Dictionary<PlayerRef, NetworkObject>();

        // Reconexión: cada jugador se identifica con un id; si se va a mitad de partida, su avatar y su sesión
        // quedan "vacantes" a su nombre y se le devuelven cuando vuelve a entrar con el código de sala.
        readonly Dictionary<PlayerRef, string> _clientIds = new Dictionary<PlayerRef, string>();
        readonly Dictionary<string, Vacancy> _vacated = new Dictionary<string, Vacancy>();
        class Vacancy { public NetworkObject avatar; public PlayerSession session; public int team; }

        static string _clientId;
        /// <summary>Identifica a este jugador entre sesiones de la misma ejecución. Para probar con dos builds en un PC: -clientid NOMBRE.</summary>
        public static string ClientId
        {
            get
            {
                if (!string.IsNullOrEmpty(_clientId)) return _clientId;
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "-clientid");
                _clientId = i >= 0 && i + 1 < args.Length ? args[i + 1] : Guid.NewGuid().ToString("N");
                return _clientId;
            }
        }
        bool _matchStarted;
        bool _avatarsSpawned;
        bool _leaving;

        // Opciones de línea de comandos para probar con dos builds sin tocar el menú (ver README).
        int _autoPick = -1;
        bool _autoWalk;
        bool _autoLog;
        float _autoQuitAt = -1f;
        float _nextAutoLog;
        string _shotDir;
        string _shotTag;
        float _nextShot;
        int _shotCount;
        bool _autoAim;
        Interactable _aimTarget;
        int _aimAngle;
        float _aimEstablishedAt = -1f;
        bool _aimPressed;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Al volver a la escena Menu aparece una copia: se descarta, vale la que ya existe.
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;   // imprescindible con dos builds en el mismo PC
        }

        void Start()
        {
            if (Instance == this) ParseCommandLine();
        }

        // ------------------------------------------------------------------ códigos de sala

        const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // sin 0/O/1/I

        public static string NewRoomCode(RegionOption region)
        {
            var sb = new StringBuilder(region.prefix).Append('-');
            for (int i = 0; i < 4; i++) sb.Append(Alphabet[UnityEngine.Random.Range(0, Alphabet.Length)]);
            return sb.ToString();
        }

        /// <summary>Acepta "sa-k7qx", "SAK7QX", con espacios, etc. Devuelve el código normalizado y su región.</summary>
        public static bool TryParseRoomCode(string input, out string code, out RegionOption region)
        {
            code = "";
            region = default;
            if (string.IsNullOrWhiteSpace(input)) return false;

            string s = input.Trim().ToUpperInvariant().Replace(" ", "");
            if (s.Length == 6 && s.IndexOf('-') < 0) s = s.Substring(0, 2) + "-" + s.Substring(2);
            if (s.Length != 7 || s[2] != '-') return false;

            string prefix = s.Substring(0, 2);
            foreach (var r in Regions)
            {
                if (r.prefix != prefix) continue;
                region = r;
                code = s;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ arranque

        public Task StartTest(MapType map)
        {
            testMap = map;
            return Connect(MatchMode.Test, "", GameMode.Single, default);
        }

        /// <summary>Crea una sala. Devuelve el código en <see cref="RoomCode"/>. customCode solo para pruebas.</summary>
        public Task Host(MatchMode mode, int regionIndex, string customCode = null)
        {
            var region = Regions[Mathf.Clamp(regionIndex, 0, Regions.Length - 1)];
            string code;
            if (!string.IsNullOrEmpty(customCode) && TryParseRoomCode(customCode, out var parsed, out var parsedRegion))
            {
                code = parsed;
                region = parsedRegion;
            }
            else
            {
                code = NewRoomCode(region);
            }
            return Connect(mode, code, GameMode.Host, region);
        }

        public Task Join(string roomCode)
        {
            if (!TryParseRoomCode(roomCode, out var code, out var region))
            {
                SetStatus("Código no válido. Tiene la forma SA-K7QX.", true);
                return Task.CompletedTask;
            }
            return Connect(MatchMode.OneVsOne, code, GameMode.Client, region);
        }

        async Task Connect(MatchMode mode, string roomCode, GameMode gameMode, RegionOption region)
        {
            if (_runner != null) return;

            Mode = mode;
            RoomCode = roomCode;
            RegionLabel = region.label;
            IsHost = gameMode == GameMode.Host || gameMode == GameMode.Single;
            IsSingle = gameMode == GameMode.Single;
            _matchStarted = false;
            _avatarsSpawned = false;
            _leaving = false;
            _sessions.Clear();
            _avatars.Clear();
            SetStatus(gameMode == GameMode.Client ? "Buscando la sala..." : "Creando la sala...", false);

            int selectIndex = SceneUtility.GetBuildIndexByScenePath(SelectScenePath);
            if (gameMode != GameMode.Client && selectIndex < 0)
            {
                Fail($"{SelectScenePath} no está en Build Settings. Ejecuta el menú Escape Room > 0.");
                return;
            }

            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;   // este peer envía input (callback OnInput)

            var sceneManager = GetComponent<NetworkSceneManagerDefault>();
            if (sceneManager == null) sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
            var objectProvider = GetComponent<NetworkObjectProviderDefault>();
            if (objectProvider == null) objectProvider = gameObject.AddComponent<NetworkObjectProviderDefault>();

            // El host (o el modo Single) elige la escena inicial; los clientes la reciben del host.
            var sceneInfo = new NetworkSceneInfo();
            if (gameMode != GameMode.Client)
                sceneInfo.AddSceneRef(SceneRef.FromIndex(selectIndex), LoadSceneMode.Single);

            // Misma región para todos: copia de la configuración global con la región fija.
            Fusion.Photon.Realtime.FusionAppSettings appSettings = null;
            if (gameMode != GameMode.Single)
            {
                appSettings = Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.GetCopy();
                appSettings.FixedRegion = region.code;
            }

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = gameMode,
                SessionName = string.IsNullOrEmpty(roomCode) ? "EscapeRoomTest" : roomCode,
                PlayerCount = MatchLayout.PlayerCount(mode),
                Scene = sceneInfo,
                SceneManager = sceneManager,
                ObjectProvider = objectProvider,
                CustomPhotonAppSettings = appSettings
            });

            if (!result.Ok)
            {
                Fail(ReasonText(result.ShutdownReason));
                return;
            }

            SetStatus(IsSingle ? "Prueba de un jugador" : $"Sala {RoomCode}", false);
        }

        void Fail(string message)
        {
            UnityEngine.Debug.LogError("[Fusion] " + message);
            SetStatus(message, true);
            if (_runner != null)
            {
                Destroy(_runner);
                _runner = null;
            }
        }

        void SetStatus(string text, bool error)
        {
            Status = text;
            IsError = error;
        }

        public static string ReasonText(ShutdownReason reason)
        {
            switch (reason)
            {
                case ShutdownReason.GameNotFound: return "No existe ninguna sala con ese código. Revisa el código y que el host siga esperando.";
                case ShutdownReason.GameIsFull: return "La sala está llena.";
                case ShutdownReason.GameClosed: return "La sala ya está cerrada (la partida empezó).";
                case ShutdownReason.GameIdAlreadyExists: return "Ya existe una sala con ese código. Crea otra.";
                case ShutdownReason.MaxCcuReached: return "Se alcanzó el límite de jugadores del plan de Photon.";
                case ShutdownReason.InvalidRegion: return "Región no válida.";
                case ShutdownReason.InvalidAuthentication: return "App Id de Photon no válido. Revisa PhotonAppSettings.";
                case ShutdownReason.PhotonCloudTimeout:
                case ShutdownReason.ConnectionTimeout:
                case ShutdownReason.OperationTimeout: return "No se pudo conectar con Photon. Revisa tu conexión a internet.";
                case ShutdownReason.ConnectionRefused: return "El host rechazó la conexión.";
                case ShutdownReason.IncompatibleConfiguration: return "Configuración incompatible: ambas builds deben ser de la misma versión.";
                case ShutdownReason.Ok: return "Desconectado.";
                default: return "Error de conexión: " + reason;
            }
        }

        /// <summary>Sale de la sala y vuelve al menú.</summary>
        public async void Leave()
        {
            var runner = _runner;
            _leaving = true;
            _runner = null;
            if (runner != null)
            {
                try { await runner.Shutdown(false); }   // false: no destruir este GameObject
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
                Destroy(runner);
            }
            ResetState();
            SetStatus("", false);
            GoToMenu();
        }

        void ResetState()
        {
            _sessions.Clear();
            _avatars.Clear();
            _matchStarted = false;
            _avatarsSpawned = false;
            RoomCode = "";
            IsHost = false;
            IsSingle = false;
        }

        static void GoToMenu()
        {
            if (SceneManager.GetActiveScene().name != MenuSceneName)
                SceneManager.LoadScene(MenuSceneName);
        }

        // ------------------------------------------------------------------ lógica del host

        void Update()
        {
            UpdateAutomation();

            if (_runner == null || !_runner.IsRunning || !_runner.IsServer || _matchStarted) return;
            if (_sessions.Count < MatchLayout.PlayerCount(Mode)) return;
            foreach (var s in _sessions.Values)
                if (s == null || s.Ready != 1) return;

            // Todos listos: el host cierra la sala (nadie más entra) y carga la escena Game para todos.
            int gameIndex = SceneUtility.GetBuildIndexByScenePath(GameScenePath);
            if (gameIndex < 0)
            {
                Fail($"{GameScenePath} no está en Build Settings.");
                return;
            }

            _matchStarted = true;
            if (!IsSingle)
            {
                _runner.SessionInfo.IsOpen = false;
                _runner.SessionInfo.IsVisible = false;
            }
            UnityEngine.Debug.Log("[Fusion] Todos listos: cargando Game");
            _runner.LoadScene(SceneRef.FromIndex(gameIndex), LoadSceneMode.Single, LocalPhysicsMode.None, true);
        }

        void SpawnAvatars(NetworkRunner runner, ArenaRegistry registry)
        {
            var map = Mode == MatchMode.Test ? testMap : (MapType)UnityEngine.Random.Range(0, 2);

            // Orden estable por orden de entrada: define equipos y arenas (ver MatchLayout).
            var ordered = _sessions.OrderBy(kv => kv.Key.RawEncoded).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                PlayerRef player = ordered[i].Key;
                PlayerSession session = ordered[i].Value;
                PlayerAssignment a = MatchLayout.Assign(Mode, i, map);

                ArenaSlot slot = registry.Get(a.slotIndex);
                if (slot == null)
                {
                    UnityEngine.Debug.LogError($"[Fusion] No hay arena {a.slotIndex} en la escena Game.");
                    continue;
                }

                int character = session.CharacterIndex;
                var avatar = runner.Spawn(avatarPrefab, slot.SpawnPosition, slot.SpawnRotation, player,
                    (r, obj) =>
                    {
                        var pa = obj.GetComponent<PlayerAvatar>();
                        pa.CharacterIndex = character;
                        pa.SlotIndex = a.slotIndex;
                        pa.Team = a.team;
                    });
                _avatars[player] = avatar;
            }

            SpawnTeams(runner, ordered.Count);
        }

        /// <summary>Un TeamState por equipo. Todos los equipos comparten la misma semilla (mismos códigos y pistas).</summary>
        void SpawnTeams(NetworkRunner runner, int playerCount)
        {
            if (Mode == MatchMode.Test)
            {
                UnityEngine.Debug.Log("[Fusion] Prueba de un jugador: sin TeamState (estado de equipo local, el compañero se simula).");
                return;
            }
            if (teamPrefab == null)
            {
                UnityEngine.Debug.LogWarning("[Fusion] Falta el prefab TeamState en FusionLauncher. Ejecuta Escape Room > 8. " +
                                             "Sin él los puzzles funcionan, pero el tubo y la palanca final no conectan a los compañeros.");
                return;
            }

            int seed = UnityEngine.Random.Range(1, int.MaxValue);
            var teams = new HashSet<int>();
            for (int i = 0; i < playerCount; i++) teams.Add(MatchLayout.Assign(Mode, i, MapType.Quirofano).team);

            var sizes = new Dictionary<int, int>();
            for (int i = 0; i < playerCount; i++)
            {
                int tm = MatchLayout.Assign(Mode, i, MapType.Quirofano).team;
                sizes[tm] = sizes.TryGetValue(tm, out int c) ? c + 1 : 1;
            }
            int startTick = runner.Tick.Raw;

            foreach (int team in teams)
            {
                int t = team;
                int size = sizes[t];
                runner.Spawn(teamPrefab, Vector3.zero, Quaternion.identity, PlayerRef.None,
                    (r, obj) =>
                    {
                        var ts = obj.GetComponent<TeamState>();
                        ts.Team = t;
                        ts.Seed = seed;
                        ts.StartTick = startTick == 0 ? 1 : startTick;
                        ts.SoloFlag = size == 1 ? 1 : 0;
                    });
                UnityEngine.Debug.Log($"[Fusion] TeamState del equipo {t} creado (semilla {seed})");
            }
        }

        // ------------------------------------------------------------------ INetworkRunnerCallbacks

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer) return;
            var obj = runner.Spawn(sessionPrefab, Vector3.zero, Quaternion.identity, player);
            _sessions[player] = obj.GetComponent<PlayerSession>();
            // Si la partida ya empezó, esta sesión es de alguien que vuelve: PlayerSession.RPC_Identify decide (OnIdentified).
            UnityEngine.Debug.Log($"[Fusion] Jugador {player} entró ({_sessions.Count}/{MatchLayout.PlayerCount(Mode)})");
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            if (!runner.IsServer) return;

            // A mitad de partida: no se despawnea. Se guarda el sitio para cuando vuelva el jugador (reconexión).
            if (_matchStarted && !IsSingle && _clientIds.TryGetValue(player, out var leftId) &&
                _avatars.TryGetValue(player, out var leftAvatar) && leftAvatar != null)
            {
                _sessions.TryGetValue(player, out var leftSession);
                int team = leftAvatar.GetComponent<PlayerAvatar>().Team;
                leftAvatar.RemoveInputAuthority();
                if (leftSession != null) leftSession.Object.RemoveInputAuthority();
                _vacated[leftId] = new Vacancy { avatar = leftAvatar, session = leftSession, team = team };
                var ts = FindTeamState(team);
                if (ts != null) { ts.AwayCountNet++; ts.HostAddPenalty(30); }   // modo solitario + 30 s de gas
                _sessions.Remove(player);
                _avatars.Remove(player);
                _clientIds.Remove(player);
                runner.SessionInfo.IsOpen = true;   // visible=false: solo entra quien tenga el código
                UnityEngine.Debug.Log($"[Fusion] Jugador {player} se desconectó a mitad de partida: su sitio queda reservado (equipo {team})");
                return;
            }

            if (_sessions.TryGetValue(player, out var s) && s != null) runner.Despawn(s.Object);
            if (_avatars.TryGetValue(player, out var a) && a != null) runner.Despawn(a);
            _sessions.Remove(player);
            _avatars.Remove(player);
            UnityEngine.Debug.Log($"[Fusion] Jugador {player} salió");
        }

        TeamState FindTeamState(int team)
        {
            foreach (var ts in FindObjectsByType<TeamState>(FindObjectsSortMode.None))
                if (ts.Object != null && ts.Object.IsValid && ts.Team == team) return ts;
            return null;
        }

        /// <summary>Lo llama PlayerSession en el host cuando un jugador dice quién es (id estable de su cliente).</summary>
        public void OnIdentified(PlayerSession session, string id)
        {
            var runner = _runner;
            if (runner == null || !runner.IsServer || session == null || session.Object == null || !session.Object.IsValid) return;
            PlayerRef player = session.Object.InputAuthority;

            if (!_matchStarted)
            {
                _clientIds[player] = id;
                return;
            }

            if (_vacated.TryGetValue(id, out var v))
            {
                _vacated.Remove(id);
                if (_sessions.TryGetValue(player, out var temp) && temp != null && temp != v.session) runner.Despawn(temp.Object);
                if (v.avatar != null) v.avatar.AssignInputAuthority(player);
                if (v.session != null) v.session.Object.AssignInputAuthority(player);
                _sessions[player] = v.session;
                _avatars[player] = v.avatar;
                _clientIds[player] = id;
                var ts = FindTeamState(v.team);
                if (ts != null && ts.AwayCountNet > 0) ts.AwayCountNet--;
                if (_vacated.Count == 0) runner.SessionInfo.IsOpen = false;
                UnityEngine.Debug.Log($"[Fusion] Jugador {player} volvió a la partida (equipo {v.team})");
            }
            else
            {
                UnityEngine.Debug.Log($"[Fusion] {player} intentó entrar con la partida empezada y no hay ningún sitio suyo: se le desconecta.");
                runner.Disconnect(player);
            }
        }

        /// <summary>Último input enviado (para el diagnóstico F3).</summary>
        public static Vector3 LastInputDirection;
        public static int InputCalls;

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            InputCalls++;
            var data = new NetworkInputData();
            float x = 0f, z = 0f;
            var kb = Keyboard.current;
            if (kb != null)
            {
                x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            }
            if (_autoWalk) z = 1f;   // solo pruebas automáticas (-autowalk)
            if (FollowCamera.ModalOpen) { x = 0f; z = 0f; }   // con una ventana de puzzle abierta el personaje no se mueve

            // Relativo a la cámara: W = alejarse de la cámara.
            Vector3 fwd = Vector3.forward;
            var cam = Camera.main;
            if (cam != null)
            {
                fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
                fwd = fwd.sqrMagnitude > 0.0001f ? fwd.normalized : Vector3.forward;
            }
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            data.Direction = right * x + fwd * z;
            LastInputDirection = data.Direction;
            input.Set(data);
        }

        public void OnSceneLoadDone(NetworkRunner runner)
        {
            if (!runner.IsServer || !_matchStarted || _avatarsSpawned) return;

            var registry = FindFirstObjectByType<ArenaRegistry>();
            if (registry == null) return;   // todavía es CharacterSelect

            _avatarsSpawned = true;
            SpawnAvatars(runner, registry);
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            UnityEngine.Debug.Log($"[Fusion] Shutdown: {shutdownReason}");
            if (_leaving) return;   // Leave() ya se encarga

            // Cierre inesperado (por ejemplo, el host salió): volver al menú con un aviso.
            if (_runner == runner)
            {
                Destroy(_runner);
                _runner = null;
            }
            ResetState();
            SetStatus(shutdownReason == ShutdownReason.Ok ? "La sala se cerró." : ReasonText(shutdownReason), shutdownReason != ShutdownReason.Ok);
            GoToMenu();
        }

        public void OnConnectedToServer(NetworkRunner runner) { }

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            UnityEngine.Debug.Log($"[Fusion] Desconectado del servidor: {reason}");
        }

        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

        // ------------------------------------------------------------------ pruebas con dos builds (línea de comandos)
        //
        //   EscapeRoom.exe -autohost 1v1|2v2 [-room SA-TEST] [-autopick 0..3] [-autowalk] [-autolog] [-autoquit 60]
        //   EscapeRoom.exe -autojoin -room SA-TEST [-autopick 0..3] [-autowalk] [-autolog] [-autoquit 60]
        //   EscapeRoom.exe -autotest [quirofano|maquinas] [-autopick 0..3] [-autowalk] [-autolog] [-autoquit 60]

        void ParseCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            bool Has(string k) => Array.IndexOf(args, k) >= 0;
            string Get(string k)
            {
                int i = Array.IndexOf(args, k);
                return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("-") ? args[i + 1] : null;
            }

            if (Has("-autopick") && int.TryParse(Get("-autopick"), out int pick)) _autoPick = pick;
            _autoWalk = Has("-autowalk");
            _autoLog = Has("-autolog");
            _autoAim = Has("-autoaim");
            if (_autoAim) Interactable.Interacted += i => UnityEngine.Debug.Log($"[AutoAim] interactuó con '{i.displayName}'");
            _shotDir = Get("-autoshot");
            _shotTag = Get("-shottag") ?? "x";
            _nextShot = Time.realtimeSinceStartup + 2.5f;
            if (!string.IsNullOrEmpty(_shotDir)) System.IO.Directory.CreateDirectory(_shotDir);
            if (Has("-autoquit") && float.TryParse(Get("-autoquit"), out float secs)) _autoQuitAt = Time.realtimeSinceStartup + secs;

            string room = Get("-room");
            if (Has("-autohost"))
            {
                var mode = Get("-autohost") == "2v2" ? MatchMode.TwoVsTwo : MatchMode.OneVsOne;
                UnityEngine.Debug.Log($"[Auto] Creando sala {mode} {room}");
                _ = Host(mode, 0, room);
            }
            else if (Has("-autojoin"))
            {
                UnityEngine.Debug.Log($"[Auto] Uniéndose a {room}");
                _ = Join(room);
            }
            else if (Has("-autotest"))
            {
                var map = Get("-autotest") == "maquinas" ? MapType.CuartoMaquinas : MapType.Quirofano;
                _ = StartTest(map);
            }
        }

        /// <summary>
        /// Solo pruebas (-autoaim): se coloca frente a un cajón/válvula, lo apunta, comprueba que el sistema lo detecta
        /// y pulsa E. Así se puede ver el aviso de interacción en una captura sin mover el ratón a mano.
        /// </summary>
        void AutoAimStep()
        {
            var fc = FindFirstObjectByType<FollowCamera>();
            if (fc == null || fc.target == null) return;
            var sys = fc.GetComponent<InteractionSystem>();
            var ncc = fc.target.GetComponent<NetworkCharacterController>();
            if (sys == null || ncc == null) return;

            if (_aimTarget == null)
            {
                float arenaX = Mathf.Round(fc.target.position.x / 200f) * 200f;
                _aimTarget = FindObjectsByType<Interactable>(FindObjectsSortMode.None)
                    .Where(i => Mathf.Abs(i.transform.position.x - arenaX) < 100f &&
                                (i.displayName.StartsWith("Cajón") || i.displayName.StartsWith("Válvula")))
                    .OrderBy(i => Vector3.Distance(i.transform.position, fc.target.position))
                    .FirstOrDefault();
                if (_aimTarget == null) return;
                UnityEngine.Debug.Log($"[AutoAim] objetivo: '{_aimTarget.displayName}'");
            }

            if (sys.Target == _aimTarget)
            {
                if (_aimEstablishedAt < 0f)
                {
                    _aimEstablishedAt = Time.realtimeSinceStartup;
                    UnityEngine.Debug.Log($"[AutoAim] el sistema detecta '{sys.Target.displayName}' al apuntarlo");
                }
                if (!_aimPressed && Time.realtimeSinceStartup - _aimEstablishedAt > 3f)
                {
                    _aimPressed = true;
                    var kb = Keyboard.current;
                    if (kb != null) InputSystem.QueueStateEvent(kb, new KeyboardState(Key.E));
                }
                else if (_aimPressed && Time.realtimeSinceStartup - _aimEstablishedAt > 3.3f)
                {
                    var kb = Keyboard.current;
                    if (kb != null) InputSystem.QueueStateEvent(kb, new KeyboardState());
                }
                return;
            }

            // Probar posiciones alrededor del objeto hasta que el rayo lo alcance
            if (_aimEstablishedAt < 0f && _aimTarget.TryGetWorldBounds(out Bounds b))
            {
                float ang = (_aimAngle++ % 12) * 30f * Mathf.Deg2Rad;
                Vector3 stand = new Vector3(b.center.x + Mathf.Sin(ang) * 1.5f, fc.target.position.y, b.center.z + Mathf.Cos(ang) * 1.5f);
                ncc.Teleport(stand);
                fc.LookAt(b.center);
            }
        }

        void UpdateAutomation()
        {
            if (_autoPick >= 0 && PlayerSession.Local != null)
            {
                var ui = FindFirstObjectByType<CharacterSelectUI>();
                if (ui != null && !ui.Locked)
                {
                    ui.SelectByCharacter(_autoPick);
                    ui.Confirm();
                    _autoPick = -1;
                }
            }

            if (_autoAim) AutoAimStep();

            if (_autoLog && Time.realtimeSinceStartup >= _nextAutoLog)
            {
                _nextAutoLog = Time.realtimeSinceStartup + 1f;
                foreach (var pa in FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None))
                {
                    if (pa.Object == null || !pa.Object.IsValid) continue;
                    UnityEngine.Debug.Log($"[AutoLog] avatar arena={pa.SlotIndex} equipo={pa.Team} personaje={pa.CharacterIndex} " +
                                          $"propio={pa.Object.HasInputAuthority} pos={pa.transform.position} cursor={Cursor.lockState}");
                }
                if (_runner != null && _runner.IsRunning)
                {
                    int sessions = FindObjectsByType<PlayerSession>(FindObjectsSortMode.None).Length;
                    UnityEngine.Debug.Log($"[AutoLog] escena={SceneManager.GetActiveScene().name} sesiones={sessions} " +
                                          $"max={_runner.SessionInfo.MaxPlayers} sala={RoomCode}");
                }
            }

            // Capturas periódicas para revisar la interfaz sin mirar la pantalla (-autoshot carpeta -shottag nombre).
            if (!string.IsNullOrEmpty(_shotDir) && _shotCount < 12 && Time.realtimeSinceStartup >= _nextShot)
            {
                _nextShot = Time.realtimeSinceStartup + 3f;
                string file = System.IO.Path.Combine(_shotDir, $"{_shotTag}_{_shotCount:00}_{SceneManager.GetActiveScene().name}.png");
                ScreenCapture.CaptureScreenshot(file);
                _shotCount++;
            }

            if (_autoQuitAt > 0f && Time.realtimeSinceStartup >= _autoQuitAt)
            {
                UnityEngine.Debug.Log("[Auto] autoquit");
                Application.Quit();
            }
        }
    }
}
