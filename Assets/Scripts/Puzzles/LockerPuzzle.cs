using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// M3 - Taquillas. La llave está bajo el colchón. Solo una de las tres taquillas (la elige la semilla) guarda la palanca,
    /// que hay que enviar al Cirujano por el tubo neumático.
    /// </summary>
    public class LockerPuzzle : PuzzleBase
    {
        class Locker { public Transform root; public List<Transform> doors = new List<Transform>(); public bool open; }
        readonly List<Locker> _lockers = new List<Locker>();

        protected override void Setup()
        {
            var roots = PuzzleKit.FindAll(arena.transform, t => t.name.StartsWith("CaseMetallic ("));
            roots.Sort((a, b) => a.position.z.CompareTo(b.position.z));   // taquilla 1 = la más al norte

            foreach (var r in roots)
            {
                var l = new Locker { root = r };
                foreach (var d in PuzzleKit.FindAll(r, t => t.name.StartsWith("Case_Door")))
                    l.doors.Add(d);
                if (l.doors.Count == 0) continue;
                int idx = _lockers.Count;
                _lockers.Add(l);
                foreach (var d in l.doors) Hook(d, _ => Open(idx), $"Taquilla {idx + 1}", "Abrir");
                Hook(r, _ => Open(idx), $"Taquilla {idx + 1}", "Abrir");   // la propia caja también responde
                if (_lockers.Count == 3) break;
            }
        }

        void Open(int i)
        {
            var l = _lockers[i];
            if (l.open) { Toast("Ya está abierta."); return; }
            if (!Inventory.Has(ItemId.KeyLocker))
            {
                Sfx.Play("denied", l.root.position);
                Toast("Cerrada con llave.");
                return;
            }

            l.open = true;
            Vector3 outward = PuzzleKit.TowardRoom(arena, l.root.position);
            foreach (var d in l.doors)
            {
                var m = d.GetComponent<Mover>() ?? d.gameObject.AddComponent<Mover>();
                m.Configure(Vector3.zero, Mover.SwingSign(d, outward, 105f) * 105f, 0.8f).SetOpen(true);
            }
            Sfx.Play(i % 2 == 0 ? "locker2" : "locker_a", l.root.position);

            if (i == PuzzleData.CrowbarLocker)
            {
                var crowbar = Find("Pickup_Crowbar");
                if (crowbar != null)
                {
                    // Cae al suelo, delante de la taquilla, en un hueco libre (dentro del armario podía quedar tapada).
                    PuzzleKit.TryFindFreeSpot(PuzzleKit.FloorBelow(l.root.position), outward, out var spot, 0.8f, arena);
                    crowbar.position = spot;
                    crowbar.gameObject.SetActive(true);
                }
                if (PuzzleContext.EffectiveSolo)
                {
                    // Sin compañero que mande los fusibles por el tubo: están aquí dentro.
                    var fuses = Find("Pickup_Fuses");
                    if (fuses != null)
                    {
                        PuzzleKit.TryFindFreeSpot(PuzzleKit.FloorBelow(l.root.position), outward, out var spot2, 1.5f, arena);
                        fuses.position = spot2;
                        fuses.gameObject.SetActive(true);
                    }
                }
                Solved(Bit.M3);
                Toast(PuzzleContext.EffectiveSolo ? "Dentro hay una palanca y unos fusibles: han caído al suelo." : "Dentro hay una palanca (ha caído al suelo). El Cirujano la necesita: mándasela por el tubo.");
            }
            else Toast("La taquilla está vacía.");
        }
    }
}
