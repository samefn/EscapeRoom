using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>Animación sencilla de puertas y cajones (deslizar o girar) con suavizado.</summary>
    public class Mover : MonoBehaviour
    {
        Vector3 _closedPos, _openPos;
        Quaternion _closedRot, _openRot;
        float _t = 1f, _dur = 0.7f;
        bool _opening, _init;
        public bool IsOpen { get; private set; }

        void Init() { if (_init) return; _init = true; _closedPos = transform.position; _closedRot = transform.rotation; _openPos = _closedPos; _openRot = _closedRot; }

        /// <summary>Define el movimiento de apertura: desplazamiento en el mundo y/o giro (grados sobre Y, alrededor del origen del objeto).</summary>
        public Mover Configure(Vector3 worldSlide, float yawDegrees = 0f, float duration = 0.7f)
        {
            Init();
            _openPos = _closedPos + worldSlide;
            _openRot = Quaternion.AngleAxis(yawDegrees, Vector3.up) * _closedRot;
            _dur = duration;
            return this;
        }

        /// <summary>Elige el sentido de giro que lleva la puerta hacia 'outward' (el interior de la sala).</summary>
        public static float SwingSign(Transform door, Vector3 outward, float angle)
        {
            if (!PuzzleKit.TryBounds(door.gameObject, out var b)) return 1f;
            Vector3 rel = b.center - door.position;
            Vector3 a = Quaternion.AngleAxis(angle, Vector3.up) * rel;
            Vector3 c = Quaternion.AngleAxis(-angle, Vector3.up) * rel;
            return Vector3.Dot(a, outward) >= Vector3.Dot(c, outward) ? 1f : -1f;
        }

        public void SetOpen(bool open)
        {
            Init();
            IsOpen = open;
            _opening = open;
            _t = 0f;
            _fromPos = transform.position;
            _fromRot = transform.rotation;
            enabled = true;
        }

        Vector3 _fromPos;
        Quaternion _fromRot;

        void Update()
        {
            if (_t >= 1f) { enabled = false; return; }
            _t = Mathf.Min(1f, _t + Time.deltaTime / Mathf.Max(0.05f, _dur));
            float e = Mathf.SmoothStep(0f, 1f, _t);
            transform.position = Vector3.Lerp(_fromPos, _opening ? _openPos : _closedPos, e);
            transform.rotation = Quaternion.Slerp(_fromRot, _opening ? _openRot : _closedRot, e);
        }
    }
}
