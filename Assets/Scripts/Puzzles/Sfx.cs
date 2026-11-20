using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Reproductor de efectos. Los clips están en Assets/Resources/Audio. Cada efecto tiene un id corto
    /// ("click", "solved"...) y puede ser solo un TRAMO de un archivo (por ejemplo, el cajón abre en 0-2 s y cierra en 8-9 s).
    /// Todo el audio es local y cosmético: nunca se sincroniza por red.
    /// Los ids sin archivo propio usan el más parecido.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        struct Def
        {
            public string file; public float start, end, volume;
            public Def(string file, float start, float end, float volume) { this.file = file; this.start = start; this.end = end; this.volume = volume; }
        }

        // end <= 0 significa "hasta el final del archivo".
        static readonly Dictionary<string, Def> Defs = new Dictionary<string, Def>
        {
            { "tick",         new Def("ui_tick", 0, 0, 0.45f) },            // E08 aviso al apuntar
            { "click",        new Def("mechanical_click", 0, 0, 0.8f) },    // E09 interactuar
            { "denied",       new Def("rattling_door", 0, 5, 0.7f) },       // E10 bloqueado
            { "pickup",       new Def("item_pickup", 5, 7, 0.8f) },         // E11 recoger
            { "place",        new Def("metal_place", 3, 4, 0.9f) },         // E12 usar / colocar
            { "page",         new Def("page_flip", 0, 0, 0.8f) },           // E13 cuaderno / notas
            { "msg_sent",     new Def("ui_pop", 0, 0, 0.6f) },              // E14 mensaje enviado
            { "msg_recv",     new Def("radio_beep", 0, 0, 0.8f) },          // E15 mensaje recibido
            { "solved",       new Def("success_chime", 0, 1, 0.9f) },       // E16 puzzle resuelto
            { "error",        new Def("error_buzz", 0, 0, 0.8f) },          // E17 fallo
            { "menu_click",   new Def("ui_click", 0, 0, 0.7f) },            // E19 menú
            { "drawer_open",  new Def("drawer", 0, 2, 0.9f) },              // E20 cajón abrir
            { "drawer_close", new Def("drawer", 8, 9, 0.8f) },              // E21 cajón cerrar
            { "locker",       new Def("locker_door", 0, 0, 0.9f) },         // E22 / E39 armario y taquilla
            { "door_open",    new Def("heavy_door", 0, 3, 1.0f) },          // E23 puerta pesada
            { "door_close",   new Def("heavy_door", 3, 5, 0.9f) },
            { "win",          new Def("victory", 0, 0, 0.9f) },             // E06
            { "lose",         new Def("defeat", 0, 0, 0.9f) },              // E07
            { "clock",        new Def("clock_tick", 0, 0, 0.5f) },          // E04

            // --- segunda tanda de audio ---
            { "keypad_ok",    new Def("keypad_ok", 0, 0, 0.9f) },           // E25 código correcto
            { "keypad_denied",new Def("keypad_denied", 0, 0, 0.8f) },       // E26 código incorrecto
            { "heater_on",    new Def("heater_on", 0, 0, 0.8f) },           // E27
            { "overheat",     new Def("overheat", 0, 0, 0.9f) },            // E29 sobrecalentado
            { "tap_on",       new Def("tap_on", 0, 0, 0.8f) },              // E30/E31 grifo
            { "valve",        new Def("valve_a", 0, 0, 0.8f) },             // E34 válvula
            { "valve2",       new Def("valve_b", 0, 0, 0.8f) },
            { "water_fill",   new Def("water_fill", 0, 2.5f, 0.7f) },          // E35 tanque llenándose
            { "water_drain",  new Def("water_drain", 0, 2.5f, 0.7f) },         // E36 tanque vaciándose
            { "bell",         new Def("bell", 0, 0, 0.9f) },                // E37 nivel exacto
            { "hatch",        new Def("hatch", 0, 0, 0.9f) },               // E38 escotilla
            { "locker2",      new Def("locker_b", 0, 0, 0.9f) },            // E39 taquilla
            { "locker_a",     new Def("locker_a", 0, 0, 0.9f) },
            { "fuse_in",      new Def("fuse_in", 0, 0, 0.9f) },             // E40 fusible
            { "toggle",       new Def("toggle", 0, 0, 0.9f) },              // E41 interruptor
            { "power_up",     new Def("power_up", 0, 0, 0.9f) },            // E42 máquina en espera
            { "lever_big",    new Def("lever_big", 0, 0, 1.0f) },           // E43 interruptor general
            { "power_down",   new Def("power_down", 0, 0, 1.0f) },          // E44 la máquina se apaga
            { "tube_send",    new Def("tube_send", 0, 0, 0.9f) },           // E45 tubo: envío
            { "capsule",      new Def("capsule", 0, 0, 0.9f) },             // E46 tubo: llegada
            { "lever",        new Def("lever", 0, 0, 1.0f) },               // E48 palanca
            { "sync_ok",      new Def("sync_ok", 0, 0, 1.0f) },             // E50 sincronía lograda
            { "sync_fail",    new Def("sync_fail", 0, 0, 0.9f) },           // E51 sincronía fallida
            { "key_turn",     new Def("key_turn", 0, 0, 0.9f) },            // E32 usar llave
            { "keys",         new Def("keys", 0, 0, 0.9f) },                // E32 coger llave
            { "paper",        new Def("paper", 0, 0, 0.8f) },               // E33 papel
            { "alarm",        new Def("alarm", 0, 0, 0.9f) },               // E54 alarma
            { "countdown",    new Def("countdown", 0, 0, 0.8f) },           // E55 inicio
            { "knock",        new Def("knock", 0, 0, 0.9f) },               // E58 eco por las tuberías
            { "cough",        new Def("cough", 0, 0, 0.9f) },               // E53 (reservado: gas)
            { "gas",          new Def("gas_leak", 0, 0, 0.8f) },            // E52 (reservado: gas)
        };

        static Sfx _inst;
        static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();

        AudioSource[] _pool;
        float[] _stopAt;
        int _next;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _inst = null; Clips.Clear(); }

        static Sfx Inst
        {
            get
            {
                if (_inst == null)
                {
                    var go = new GameObject("Sfx");
                    DontDestroyOnLoad(go);
                    _inst = go.AddComponent<Sfx>();
                    _inst.Init();
                }
                return _inst;
            }
        }

        void Init()
        {
            _pool = new AudioSource[16];
            _stopAt = new float[_pool.Length];
            for (int i = 0; i < _pool.Length; i++)
            {
                // Un objeto hijo por fuente: cada una necesita su propia posición para el sonido 3D.
                var child = new GameObject("Voice" + i);
                child.transform.SetParent(transform, false);
                var s = child.AddComponent<AudioSource>();
                s.playOnAwake = false;
                _pool[i] = s;
            }
        }

        public static AudioClip GetClip(string file)
        {
            if (!Clips.TryGetValue(file, out var c))
            {
                c = Resources.Load<AudioClip>("Audio/" + file);
                if (c == null) Debug.LogWarning("[Sfx] No se encontró Resources/Audio/" + file);
                Clips[file] = c;
            }
            return c;
        }

        /// <summary>Sonido 3D en un punto del mundo (se apaga a ~18 m: no se oyen otras arenas).</summary>
        public static void Play(string id, Vector3 position, float volume = 1f, float pitchJitter = 0.04f) =>
            Inst.PlayInternal(id, position, true, volume, pitchJitter);

        /// <summary>Sonido de interfaz (2D).</summary>
        public static void Play2D(string id, float volume = 1f, float pitchJitter = 0.02f) =>
            Inst.PlayInternal(id, Vector3.zero, false, volume, pitchJitter);

        void PlayInternal(string id, Vector3 pos, bool spatial, float volume, float jitter)
        {
            if (!Defs.TryGetValue(id, out var d)) { Debug.LogWarning("[Sfx] Id desconocido: " + id); return; }
            var clip = GetClip(d.file);
            if (clip == null) return;

            int idx = -1;
            for (int k = 0; k < _pool.Length; k++)
            {
                int i = (_next + k) % _pool.Length;
                if (!_pool[i].isPlaying) { idx = i; break; }
            }
            if (idx < 0) idx = _next;   // todas ocupadas: reutiliza la más antigua
            _next = (idx + 1) % _pool.Length;

            var s = _pool[idx];
            s.Stop();
            s.clip = clip;
            s.volume = Mathf.Clamp01(d.volume * volume);
            float pitch = 1f + Random.Range(-jitter, jitter);
            s.pitch = pitch;
            s.spatialBlend = spatial ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 1.5f;
            s.maxDistance = 18f;
            s.transform.position = pos;

            float start = Mathf.Clamp(d.start, 0f, Mathf.Max(0f, clip.length - 0.05f));
            float end = d.end > 0f ? Mathf.Min(d.end, clip.length) : clip.length;
            s.time = start;
            s.Play();
            _stopAt[idx] = d.start > 0f || d.end > 0f ? Time.unscaledTime + Mathf.Max(0.05f, (end - start) / pitch) : float.MaxValue;
        }

        void Update()
        {
            for (int i = 0; i < _pool.Length; i++)
                if (_pool[i].isPlaying && Time.unscaledTime >= _stopAt[i]) _pool[i].Stop();
        }

        /// <summary>Crea un AudioSource en bucle (ambientes). El que llama decide su volumen.</summary>
        public static AudioSource MakeLoop(GameObject host, string file, float volume, bool spatial = false)
        {
            var clip = GetClip(file);
            var s = host.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.volume = volume;
            s.spatialBlend = spatial ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 1.5f;
            s.maxDistance = 15f;
            s.playOnAwake = false;
            if (clip != null) s.Play();
            return s;
        }
    }
}
