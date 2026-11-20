using Fusion;
using UnityEngine;

namespace EscapeRoom.Net
{
    /// <summary>
    /// Avatar de red. Prefab = NetworkObject + CharacterController + NetworkCharacterController + este script
    /// (lo crea el menú Escape Room > 4). El modelo con su animación se instancia como hijo en Spawned().
    ///
    /// Movimiento: NetworkCharacterController.Move (clase incluida en el SDK, Assets/Photon/Fusion/Runtime).
    /// Vista: PRIMERA persona. El jugador local no ve su modelo (FollowCamera pone la cámara a la altura de los ojos).
    /// Animación: se maneja en local a partir de la velocidad (NetworkCharacterController.Velocity ya es [Networked]).
    /// Los controllers actuales solo tienen un ciclo "Walking": al estar quieto se congela (animator.speed = 0).
    /// </summary>
    [RequireComponent(typeof(NetworkCharacterController))]
    public class PlayerAvatar : NetworkBehaviour
    {
        [SerializeField] CharacterDatabase database;

        // El host los fija en OnBeforeSpawned, antes de sincronizar.
        [Networked] public int CharacterIndex { get; set; }
        [Networked] public int SlotIndex { get; set; }
        [Networked] public int Team { get; set; }

        NetworkCharacterController _ncc;
        EscapeRoom.Puzzles.FootstepLoop _steps;
        Animator _animator;
        FollowCamera _camera;
        float _nextCameraWarn;
        float _spawnedAt;

        public override void Spawned()
        {
            _ncc = GetComponent<NetworkCharacterController>();
            _spawnedAt = Time.unscaledTime;

            var def = database != null ? database.Get(CharacterIndex) : null;
            if (def != null && def.visualPrefab != null)
            {
                var visual = Instantiate(def.visualPrefab, transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                _animator = visual.GetComponentInChildren<Animator>();

                // Primera persona: el jugador local no ve su propio cuerpo (solo proyecta sombra).
                if (Object.HasInputAuthority)
                    foreach (var r in visual.GetComponentsInChildren<Renderer>())
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
            else
            {
                UnityEngine.Debug.LogWarning("[PlayerAvatar] Sin CharacterDatabase o sin modelo para el personaje " + CharacterIndex);
            }

            // Pasos: baldosa en el Quirófano, metal en el Cuarto de máquinas
            var registry = FindFirstObjectByType<ArenaRegistry>();
            var arena = registry != null ? registry.Get(SlotIndex) : null;
            _steps = gameObject.AddComponent<EscapeRoom.Puzzles.FootstepLoop>();
            _steps.Init(arena != null ? arena.map : MapType.Quirofano);

            UnityEngine.Debug.Log($"[PlayerAvatar] Spawned: personaje {CharacterIndex}, arena {SlotIndex}, equipo {Team}, " +
                                  $"inputAuthority={Object.HasInputAuthority}");
        }

        public override void FixedUpdateNetwork()
        {
            Vector3 dir = GetInput(out NetworkInputData input) ? input.Direction : Vector3.zero;
            _ncc.Move(dir);
        }

        public override void Render()
        {
            if (_animator != null)
            {
                var v = _ncc.Velocity;
                bool moving = new Vector2(v.x, v.z).sqrMagnitude > 0.04f;
                _animator.speed = moving ? 1f : 0f;
            }
            if (_steps != null && _ncc != null)
            {
                var vel = _ncc.Velocity;
                _steps.SetMoving(new Vector2(vel.x, vel.z).sqrMagnitude > 0.04f && _ncc.Grounded);
            }

            if (Object.HasInputAuthority && _camera == null)
            {
                _camera = FindFirstObjectByType<FollowCamera>();
                if (_camera == null)
                {
                    // Red de seguridad: si la escena no tiene FollowCamera (o no se encuentra), se añade a la cámara principal.
                    var cam = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
                    if (cam != null)
                    {
                        _camera = cam.gameObject.AddComponent<FollowCamera>();
                        UnityEngine.Debug.LogWarning($"[PlayerAvatar] No había FollowCamera: se añadió a la cámara '{cam.name}'.");
                    }
                    else if (Time.unscaledTime >= _nextCameraWarn)
                    {
                        _nextCameraWarn = Time.unscaledTime + 3f;
                        UnityEngine.Debug.LogWarning("[PlayerAvatar] No hay ninguna cámara en la escena: no se puede enlazar la vista del jugador.");
                    }
                }
                if (_camera != null)
                {
                    _camera.SetTarget(transform);
                    UnityEngine.Debug.Log($"[PlayerAvatar] Cámara enlazada al avatar de la arena {SlotIndex} (cámara '{_camera.name}').");
                }
            }
        }

        void OnGUI()
        {
            if (Object == null || !Object.IsValid || !Object.HasInputAuthority) return;
            var def = database != null ? database.Get(CharacterIndex) : null;
            string name = def != null ? def.displayName : "?";
            UiScale.Begin(out _, out _);
            if (_camera == null && Time.unscaledTime - _spawnedAt > 2f)
                NightTerrorUi.Label(new Rect(14, 120, 1000, 24), "⚠ La cámara del jugador no está enlazada (revisa Player.log).", 16, NightTerrorUi.Ember, NightTerrorUi.FontKind.Mono);
            NightTerrorUi.Label(new Rect(14, 8, 1000, 22),
                $"{name}  ·  arena {SlotIndex}  ·  equipo {(Team == 0 ? "A" : Team == 1 ? "B" : (Team + 1).ToString())}  ·  WASD mover · ratón mirar · Esc suelta el ratón",
                13, NightTerrorUi.Fog, NightTerrorUi.FontKind.Mono);
            UiScale.End();
        }
    }
}
