using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// El gas / la presión. La intensidad (PuzzleContext.Gas, 0-1) sube con el tiempo y salta con cada penalización:
    ///  - Quirófano: niebla verdosa que se espesa.   - Cuarto de máquinas: vapor rojizo que se espesa.
    ///  - Siseo de fondo, toses cuando está alto y tic-tac en el último minuto.
    /// Todo es local y cosmético; el reloj real vive en TeamState. Al salir de la partida se restaura la niebla original.
    /// </summary>
    public class GasController : MonoBehaviour
    {
        AudioSource _hiss, _tick;
        bool _saved;
        bool _origFog;
        Color _origColor;
        FogMode _origMode;
        float _origDensity;
        float _nextCough;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<GasController>() != null) return;
            var go = new GameObject("GasController");
            DontDestroyOnLoad(go);
            go.AddComponent<GasController>();
        }

        void Start()
        {
            _hiss = Sfx.MakeLoop(gameObject, "gas_leak", 0f);
            _tick = Sfx.MakeLoop(gameObject, "clock_tick", 0f);
        }

        void Update()
        {
            bool live = PuzzleContext.Ready && !PuzzleContext.GameOver;
            float gas = live ? PuzzleContext.Gas : 0f;

            // Niebla
            if (PuzzleContext.Ready)
            {
                if (!_saved)
                {
                    _saved = true;
                    _origFog = RenderSettings.fog;
                    _origColor = RenderSettings.fogColor;
                    _origMode = RenderSettings.fogMode;
                    _origDensity = RenderSettings.fogDensity;
                }
                float k = Mathf.Pow(gas, 1.3f);
                Color tint = PuzzleContext.LocalRole == Role.Cirujano ? new Color(0.28f, 0.38f, 0.16f) : new Color(0.4f, 0.09f, 0.06f);
                RenderSettings.fog = k > 0.01f || _origFog;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = Color.Lerp(_origFog ? _origColor : tint * 0.3f, tint, Mathf.Clamp01(k * 2f));
                RenderSettings.fogDensity = Mathf.Lerp(_origFog ? _origDensity : 0f, 0.055f, k);
            }
            else if (_saved)
            {
                _saved = false;
                RenderSettings.fog = _origFog;
                RenderSettings.fogColor = _origColor;
                RenderSettings.fogMode = _origMode;
                RenderSettings.fogDensity = _origDensity;
            }

            // Sonido
            if (_hiss != null) _hiss.volume = Mathf.MoveTowards(_hiss.volume, Mathf.Clamp01(gas * 1.2f - 0.15f) * 0.45f, Time.unscaledDeltaTime * 0.3f);
            bool lastMinute = live && PuzzleContext.TimeLeft < 60f;
            if (_tick != null) _tick.volume = Mathf.MoveTowards(_tick.volume, lastMinute ? 0.45f : 0f, Time.unscaledDeltaTime * 0.8f);

            if (live && gas > 0.45f && Time.unscaledTime >= _nextCough)
            {
                Sfx.Play2D("cough", 0.8f, 0.08f);
                _nextCough = Time.unscaledTime + Mathf.Lerp(16f, 6f, gas) + Random.Range(0f, 4f);
            }
            else if (Time.unscaledTime >= _nextCough && gas <= 0.45f) _nextCough = Time.unscaledTime + 5f;
        }

        void OnDestroy()
        {
            if (_saved)
            {
                RenderSettings.fog = _origFog;
                RenderSettings.fogColor = _origColor;
                RenderSettings.fogMode = _origMode;
                RenderSettings.fogDensity = _origDensity;
            }
        }
    }
}
