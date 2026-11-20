using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Q3 - Cajones numerados. El primer archivador (SheetRack) tiene los cajones 1-12. Hace falta la llave del armario (Q1)
    /// y hay que abrir 3 cajones en el orden que dicta el Operador (la placa de M4). Un fallo los cierra todos.
    /// Al acertar aparecen los fusibles, que van al Operador por el tubo.
    /// </summary>
    public class DrawerPuzzle : PuzzleBase
    {
        class Drawer { public int number; public Transform t; public Mover mover; }

        readonly Dictionary<int, Drawer> _drawers = new Dictionary<int, Drawer>();
        int _next;
        bool _done;
        Vector3 _outward = Vector3.left;

        protected override void Setup()
        {
            // Cajones del archivador "SheetRack" (el segundo, "SheetRack (1)", es decoración).
            foreach (var t in PuzzleKit.FindAll(arena.transform, x => x.name.StartsWith("SheetRackCase_")))
            {
                bool first = t.parent != null && t.parent.name == "SheetRack";
                if (!int.TryParse(t.name.Substring("SheetRackCase_".Length), out int n)) continue;

                if (first && !_drawers.ContainsKey(n))
                {
                    var d = new Drawer { number = n, t = t };
                    _drawers[n] = d;
                    int num = n;
                    Hook(t, _ => OnDrawer(num), $"Cajón {n}", "Abrir");
                    _outward = PuzzleKit.TowardRoom(arena, t.position);
                }
                else
                {
                    Hook(t, _ => { Sfx.Play("denied", transform.position, 0.6f); Toast("Este cajón está vacío."); }, "Cajón", "Abrir");
                }
            }
        }

        void OnDrawer(int n)
        {
            var d = _drawers[n];
            if (_done) { Toast("Ya has sacado lo que había."); return; }
            if (!Inventory.Has(ItemId.KeyCabinet))
            {
                Sfx.Play("denied", d.t.position);
                Toast(LocalProgress.Has("q1") ? "Cerrado con llave. ¿Has cogido la llave?" : "Cerrado con llave.");
                return;
            }
            if (d.mover != null && d.mover.IsOpen) { Toast("Ese cajón ya está abierto."); return; }

            if (n == PuzzleData.DrawerSeq[_next])
            {
                d.mover = d.t.GetComponent<Mover>() ?? d.t.gameObject.AddComponent<Mover>();
                d.mover.Configure(_outward * 0.32f, 0f, 0.5f).SetOpen(true);
                if (_next == 0) Sfx.Play("key_turn", d.t.position);
                Sfx.Play("drawer_open", d.t.position);
                _next++;
                if (_next >= 3) Finish(d);
                else Toast($"Cajón {n} abierto ({_next}/3).");
            }
            else
            {
                Sfx.Play("error", d.t.position);
                TeamLink.Penalize(20, "Orden incorrecto");
                foreach (var o in _drawers.Values)
                    if (o.mover != null && o.mover.IsOpen) { o.mover.SetOpen(false); Sfx.Play("drawer_close", o.t.position, 0.7f); }
                _next = 0;
                Toast("Orden incorrecto: todos los cajones se cierran (+20 s de gas).");
            }
        }

        public override void Restore(int mask) { if (Has(mask, Bit.Q3)) _done = true; }

        void Finish(Drawer last)
        {
            _done = true;
            Solved(Bit.Q3);
            // Lo que hay en el cajón cae al suelo, delante del archivador, en un hueco libre (así siempre se ve y se puede coger).
            Vector3 floor = PuzzleKit.FloorBelow(last.t.position);
            var fuses = Find("Pickup_Fuses");
            if (fuses != null)
            {
                PuzzleKit.TryFindFreeSpot(floor, _outward, out var spot, 0.8f, arena);
                fuses.position = spot;
                fuses.gameObject.SetActive(true);
            }
            // Sin compañero (1v1, prueba o compañero desconectado) la palanca no puede venir por el tubo: sale del mismo cajón.
            if (PuzzleContext.EffectiveSolo)
            {
                var crowbar = Find("Pickup_Crowbar");
                if (crowbar != null)
                {
                    PuzzleKit.TryFindFreeSpot(floor, _outward, out var spot2, 1.5f, arena);
                    crowbar.position = spot2;
                    crowbar.gameObject.SetActive(true);
                }
                Toast("Secuencia correcta. Los fusibles y una palanca han caído al suelo, delante del archivador.");
            }
            else Toast("Secuencia correcta. Los fusibles han caído al suelo, delante del archivador.");
        }
    }
}
