using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>Herramientas comunes para los puzzles (búsqueda de objetos de la arena, textos 3D, colores).</summary>
    public static class PuzzleKit
    {
        /// <summary>Primer hijo (incluso inactivo) con ese nombre exacto.</summary>
        public static Transform Find(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        public static List<Transform> FindAll(Transform root, Func<Transform, bool> predicate)
        {
            var list = new List<Transform>();
            if (root == null) return list;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (predicate(t)) list.Add(t);
            return list;
        }

        /// <summary>Caja que envuelve todos los renderers del objeto (false si no tiene).</summary>
        public static bool TryBounds(GameObject go, out Bounds b)
        {
            b = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is TextMesh || r.GetComponent<TextMesh>() != null) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        static Font _font;

        /// <summary>heightMeters = altura aproximada de una letra en el mundo (TextMesh: caracter ~ characterSize x fontSize x 0.1).</summary>
        public static TextMesh MakeText(Transform parent, string text, Vector3 localPos, float heightMeters, Color color, Quaternion? localRot = null)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot ?? Quaternion.identity;
            var tm = go.AddComponent<TextMesh>();
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = _font;
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = heightMeters / (tm.fontSize * 0.1f);
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _font.material;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return tm;
        }

        /// <summary>
        /// Busca un sitio del SUELO, delante del objeto, donde no haya nada (para que una llave o una palanca que "cae" se pueda coger).
        /// Prueba varias distancias y desplazamientos laterales. False si no encuentra ninguno.
        /// </summary>
        public static bool TryFindFreeSpot(Vector3 floorPoint, Vector3 outward, out Vector3 spot, float minDistance = 0.8f, ArenaSlot lastResort = null)
        {
            Vector3 lat = Vector3.Cross(Vector3.up, outward);
            var cands = new List<(float cost, Vector3 p)>();
            for (float d = minDistance; d <= minDistance + 2.4f; d += 0.3f)
                for (float l = -2.1f; l <= 2.11f; l += 0.3f)
                    cands.Add((d + Mathf.Abs(l) * 0.6f, floorPoint + outward * d + lat * l));
            cands.Sort((x, y) => x.cost.CompareTo(y.cost));

            foreach (var c in cands)
                if (Standable(c.p, floorPoint.y, out var ground)) { spot = ground + Vector3.up * 0.12f; return true; }

            // Último recurso: junto al punto de aparición (siempre es suelo andable y alcanzable).
            if (lastResort != null)
            {
                Vector3 sp = lastResort.SpawnPosition;
                for (float r = 0.9f; r <= 3.0f; r += 0.3f)
                    for (int k = 0; k < 12; k++)
                    {
                        float ang = k * Mathf.PI * 2f / 12f;
                        var p = new Vector3(sp.x + Mathf.Cos(ang) * r, floorPoint.y, sp.z + Mathf.Sin(ang) * r);
                        if (Standable(p, floorPoint.y, out var ground)) { spot = ground + Vector3.up * 0.12f; return true; }
                    }
            }
            spot = floorPoint + outward * 1.0f + Vector3.up * 0.12f;
            return false;
        }

        /// <summary>¿Cabe un jugador de pie aquí? Suelo a la altura esperada y cápsula (radio 0,32) sin chocar con nada.</summary>
        public static bool Standable(Vector3 p, float floorY, out Vector3 ground)
        {
            ground = p;
            if (!Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out var gh, 2.5f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(gh.point.y - floorY) > 0.15f) return false;   // tiene que ser suelo, no encima de un mueble
            ground = gh.point;
            return !Physics.CheckCapsule(gh.point + Vector3.up * 0.45f, gh.point + Vector3.up * 1.5f, 0.32f, ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Distancia al plano horizontal más cercano de dos direcciones: sirve para saber si un objeto "mira" a +x/-x/+z/-z.</summary>
        public static Vector3 SnapToAxis(Vector3 v)
        {
            v.y = 0f;
            if (v.sqrMagnitude < 1e-6f) return Vector3.forward;
            return Mathf.Abs(v.x) >= Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        /// <summary>Punto del suelo de la sala justo debajo de p (ignora muebles: solo cuenta la malla del suelo).</summary>
        public static Vector3 FloorBelow(Vector3 p)
        {
            var hits = Physics.RaycastAll(p + Vector3.up * 0.1f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
                if (h.collider.name.EndsWith("_floor") && h.normal.y > 0.7f) return h.point;
            foreach (var h in hits)
                if (h.normal.y > 0.7f && h.collider.GetComponentInParent<Interactable>() == null) return h.point;
            return new Vector3(p.x, p.y - 1f, p.z);
        }

        static bool IsShell(string n) => n == "Surgery" || n == "MachineRoom" || n.EndsWith("_facing");

        /// <summary>
        /// Dirección (eje X o Z) desde un objeto hacia el interior de la sala. Se busca la pared de la sala MÁS CERCANA al objeto
        /// (ignorando muebles) y se devuelve la dirección contraria. Antes se usaba el punto de aparición, que fallaba con los
        /// objetos pegados a una pared a lo largo de la cual está el jugador (el armario del Q1 "miraba" hacia el armario de al lado).
        /// </summary>
        public static Vector3 TowardRoom(ArenaSlot arena, Vector3 from)
        {
            var origin = from + Vector3.up * 0.5f;
            float best = float.MaxValue;
            Vector3 wall = Vector3.zero;
            foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                var hits = Physics.RaycastAll(origin, d, 2.0f, ~0, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    if (!IsShell(h.collider.name) || Mathf.Abs(h.normal.y) > 0.4f) continue;
                    if (h.distance < best) { best = h.distance; wall = d; }
                    break;
                }
            }
            if (wall != Vector3.zero) return -wall;
            return TowardSpawn(arena, from);
        }

        /// <summary>Solo si no se encuentra ninguna pared cerca: hacia el punto de aparición.</summary>
        static Vector3 TowardSpawn(ArenaSlot arena, Vector3 from)
        {
            Vector3 target = arena != null ? arena.SpawnPosition : from + Vector3.forward;
            return SnapToAxis(target - from);
        }
    }

    /// <summary>
    /// Base de los puzzles. Están en la arena (los hay en las 4 copias), pero solo reaccionan los de la arena del jugador local.
    /// Cada puzzle enlaza sus objetos buscándolos por nombre dentro de su arena.
    /// </summary>
    public abstract class PuzzleBase : MonoBehaviour
    {
        protected ArenaSlot arena;
        bool _started;

        /// <summary>Esta arena es la del jugador local (aunque la partida haya terminado).</summary>
        public bool InMyArena => arena != null && PuzzleContext.Ready && PuzzleContext.LocalSlot == arena.slotIndex;
        /// <summary>Se puede jugar: es mi arena y nadie ha ganado todavía.</summary>
        public bool Active => InMyArena && !PuzzleContext.GameOver;
        protected Role MyRole => PuzzleContext.LocalRole;

        protected virtual void Awake() => arena = GetComponentInParent<ArenaSlot>();

        void Start()
        {
            if (_started) return;
            _started = true;
            Setup();
        }

        /// <summary>Se llama una vez, al empezar: aquí se buscan los objetos y se enganchan las interacciones.</summary>
        protected abstract void Setup();

        /// <summary>Reconexión: el equipo ya había resuelto estos puzzles (bits de Bit); reconstruye el estado local de la sala.</summary>
        public virtual void Restore(int solvedMask) { }

        protected static bool Has(int mask, int bit) => ((mask >> bit) & 1) == 1;

        protected Transform Find(string name) => PuzzleKit.Find(arena != null ? arena.transform : transform, name);

        /// <summary>Engancha una acción a un Interactable (lo crea si falta). Solo se ejecuta si la arena es la del jugador.</summary>
        protected Interactable Hook(Transform t, Action<Interactable> action, string displayName = null, string verb = null, string hint = null)
        {
            if (t == null) return null;
            var it = t.GetComponent<Interactable>();
            if (it == null) it = t.gameObject.AddComponent<Interactable>();
            it.message = "";   // anula el texto genérico "todavía no tiene función"
            if (displayName != null) it.displayName = displayName;
            if (verb != null) it.verb = verb;
            if (hint != null) it.hint = hint;
            it.onInteract.AddListener(i => { if (Active) action(i); });
            return it;
        }

        protected void Toast(string msg) => PuzzleUI.Toast(msg);
        protected void Solved(int bit) { TeamLink.Solve(bit); Sfx.Play2D("solved"); }
        protected static Vector3 P(Component c) => c.transform.position;
    }
}
