using UnityEngine;

namespace EscapeRoom
{
    /// <summary>
    /// Apaga las cámaras (y oyentes de audio) que NO son la del jugador.
    ///
    /// Causa real de un fallo visto con 4 builds: los modelos del Asylum (FBX exportados desde Cinema 4D) traen una cámara
    /// "CINEMA_4D_Editor" dentro. Cada copia de arena de la escena Game la heredaba, así que había cámaras extra con la misma
    /// prioridad (profundidad 0) que la del jugador. Dibujaban una vista fija encima de la del avatar: todas las ventanas veían la
    /// misma imagen y parecía que ni la cámara ni el jugador se movían (el avatar sí se movía).
    ///
    /// En cuanto existe una FollowCamera con objetivo, cualquier otra cámara activa se desactiva. En el Menú y la selección de
    /// personaje no hay FollowCamera, así que no toca nada.
    /// </summary>
    public class CameraGuard : MonoBehaviour
    {
        float _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<CameraGuard>() != null) return;
            var go = new GameObject("CameraGuard");
            DontDestroyOnLoad(go);
            go.AddComponent<CameraGuard>();
        }

        void LateUpdate()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;

            var follow = FindFirstObjectByType<FollowCamera>();
            if (follow == null || follow.target == null) return;
            var keep = follow.GetComponent<Camera>();

            foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (cam == keep || !cam.enabled) continue;
                if (cam.targetTexture != null) continue;   // cámaras que dibujan en una textura (no se ven en pantalla): se respetan
                Debug.LogWarning($"[CameraGuard] Cámara extra apagada: '{cam.name}' (escena '{cam.gameObject.scene.name}', pos {cam.transform.position}).");
                cam.enabled = false;
            }

            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (l.gameObject != follow.gameObject && l.enabled) l.enabled = false;
        }
    }
}
