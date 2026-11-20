using UnityEngine;
using UnityEngine.InputSystem;

namespace EscapeRoom
{
    /// <summary>
    /// Cámara en PRIMERA persona local (el nombre se mantiene para no romper la escena Game).
    /// Cada cliente la asigna a SU personaje. El ratón mueve la cámara; WASD mueve al personaje en la dirección
    /// en que miras. ESC libera/captura el ratón (para pulsar "Salir" del panel de sala).
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class FollowCamera : MonoBehaviour
    {
        public Transform target;
        [Tooltip("Altura de los ojos sobre los pies del personaje.")]
        public float eyeHeight = 1.6f;
        [Tooltip("Grados por píxel de movimiento del ratón.")]
        public float sensitivity = 0.1f;
        public float maxPitch = 85f;

        float _yaw;
        float _pitch;
        bool _captured;

        public bool MouseCaptured => _captured;

        /// <summary>Hay una ventana de puzzle abierta (nota, teclado, cuaderno): no se mira, no se mueve, no se interactúa.</summary>
        public static bool ModalOpen;
        /// <summary>Fotograma en que se cerró la última ventana (evita que la misma tecla Esc/E haga otra cosa).</summary>
        public static int ModalClosedFrame = -1;

        void Awake()
        {
            Debug.Log($"[FollowCamera] Awake en la escena '{gameObject.scene.name}' (cámara '{name}').");
            GetComponent<Camera>().nearClipPlane = 0.05f;   // evita ver "dentro" de las paredes al acercarse
            if (GetComponent<InteractionSystem>() == null) gameObject.AddComponent<InteractionSystem>();
        }

        /// <summary>Gira la cámara para mirar a un punto del mundo (útil para pruebas y escenas guionizadas).</summary>
        public void LookAt(Vector3 worldPoint)
        {
            Vector3 d = worldPoint - (target != null ? target.position + Vector3.up * eyeHeight : transform.position);
            if (d.sqrMagnitude < 0.0001f) return;
            _yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            _pitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        public void SetTarget(Transform t)
        {
            target = t;
            if (t == null) return;
            _yaw = t.eulerAngles.y;
            _pitch = 0f;
            SetCaptured(true);
        }

        void SetCaptured(bool captured)
        {
            _captured = captured;
            Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !captured;
        }

        void Update()
        {
            if (ModalOpen || ModalClosedFrame == Time.frameCount) return;
            var kb = Keyboard.current;
            if (target != null && kb != null && kb.escapeKey.wasPressedThisFrame)
                SetCaptured(!_captured);
        }

        void LateUpdate()
        {
            if (target == null) return;

            if (_captured && !ModalOpen)
            {
                var mouse = Mouse.current;
                if (mouse != null)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    _yaw += d.x * sensitivity;
                    _pitch = Mathf.Clamp(_pitch - d.y * sensitivity, -maxPitch, maxPitch);
                }
            }

            transform.position = target.position + Vector3.up * eyeHeight;
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        void OnDestroy()
        {
            ModalOpen = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
