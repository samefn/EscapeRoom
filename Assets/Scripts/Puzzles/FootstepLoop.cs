using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Pasos de un avatar. Un tramo de audio con varios pasos se repite mientras el personaje camina y se corta al pararse.
    /// Baldosa en el Quirófano (tramo 6-9 s de steps_tile) y rejilla metálica en el Cuarto de máquinas (tramo 5-7 s de steps_metal).
    /// Es 3D: se oyen los pasos del compañero solo si está cerca (no pasa: está en otra arena, a 200 m).
    /// </summary>
    public class FootstepLoop : MonoBehaviour
    {
        AudioSource _src;
        float _start, _end;
        bool _moving;

        public void Init(MapType map)
        {
            bool tile = map == MapType.Quirofano;
            var clip = Sfx.GetClip(tile ? "steps_tile" : "steps_metal");
            _start = tile ? 6f : 5f;
            _end = tile ? 9f : 7f;
            if (clip == null) { enabled = false; return; }

            _end = Mathf.Min(_end, clip.length);
            _src = gameObject.AddComponent<AudioSource>();
            _src.clip = clip;
            _src.loop = false;
            _src.playOnAwake = false;
            _src.spatialBlend = 1f;
            _src.rolloffMode = AudioRolloffMode.Linear;
            _src.minDistance = 1.5f;
            _src.maxDistance = 14f;
            _src.volume = 0.55f;
        }

        public void SetMoving(bool moving)
        {
            if (_src == null || moving == _moving) return;
            _moving = moving;
            if (moving)
            {
                _src.time = _start;
                _src.Play();
            }
            else _src.Stop();
        }

        void Update()
        {
            if (_src == null || !_moving) return;
            if (!_src.isPlaying || _src.time >= _end - 0.02f)
            {
                _src.time = _start;   // vuelve al principio del tramo
                if (!_src.isPlaying) _src.Play();
            }
        }
    }
}
