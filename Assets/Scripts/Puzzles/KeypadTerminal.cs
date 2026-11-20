using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Teclado numérico de pared. Dos usos:
    ///  - Cabinet (Q1): 3 cifras = los números de los órganos del expediente. Abre el armario y deja caer la llave de los cajones.
    ///  - Door (Q5): 6 cifras = 3 de los símbolos del proyector (traducidos con la leyenda del Operador)
    ///    + 3 de la pantalla del Operador (traducidos con la leyenda del póster). Desbloquea la palanca final.
    /// </summary>
    public class KeypadTerminal : PuzzleBase
    {
        public enum Kind { Cabinet, Door }
        public Kind kind = Kind.Cabinet;

        bool _done;
        Interactable _it;

        protected override void Setup()
        {
            _it = Hook(transform, _ => Use(), kind == Kind.Cabinet ? "Teclado del armario" : "Teclado de la puerta", "Usar");
        }

        void Use()
        {
            Sfx.Play("click", transform.position);
            if (_done)
            {
                Toast(kind == Kind.Cabinet ? "El armario ya está abierto." : "Código aceptado. La palanca está desbloqueada.");
                return;
            }
            if (kind == Kind.Door && !LocalProgress.Has("q4_seen"))
                Toast("Pista: el proyector enseña la mitad del código.");

            if (kind == Kind.Cabinet) PuzzleUI.OpenKeypad("Armario clínico", 3, code => Check(code, PuzzleData.Q1Code));
            else PuzzleUI.OpenKeypad("Cerradura de la puerta", 6, code => Check(code, PuzzleData.DoorCode));
        }

        bool Check(string entered, string expected)
        {
            if (entered != expected)
            {
                TeamLink.Penalize(kind == Kind.Door ? 15 : 5, "Código incorrecto");
                return false;
            }
            _done = true;
            if (kind == Kind.Cabinet) OpenCabinet();
            else
            {
                LocalProgress.Set("q5");
                Solved(Bit.Q5);
                Toast("Cerradura abierta. Falta la palanca final.");
            }
            return true;
        }

        public override void Restore(int mask)
        {
            if (kind == Kind.Cabinet && Has(mask, Bit.Q1)) { _done = true; OpenCabinet(true, !Has(mask, Bit.Q3)); }
            else if (kind == Kind.Door && Has(mask, Bit.Q5)) { _done = true; LocalProgress.Set("q5"); }
        }

        void OpenCabinet(bool restoring = false, bool giveKey = true)
        {
            if (!restoring) Solved(Bit.Q1);
            LocalProgress.Set("q1");

            var cabinet = Find("MedRack_V2 (1)");
            Vector3 outward = Vector3.back;
            Vector3 floor = transform.position;
            if (cabinet != null)
            {
                outward = PuzzleKit.TowardRoom(arena, cabinet.position);
                floor = cabinet.position;
                foreach (var d in PuzzleKit.FindAll(cabinet, t => t.name.StartsWith("MedRackDoor")))
                {
                    var m = d.GetComponent<Mover>() ?? d.gameObject.AddComponent<Mover>();
                    float sign = Mover.SwingSign(d, outward, 100f);
                    m.Configure(Vector3.zero, sign * 100f, 0.9f).SetOpen(true);
                }
                Sfx.Play("locker", cabinet.position);
            }

            // La llave cae delante del armario.
            var key = Find("Pickup_KeyCabinet");
            if (key != null && giveKey)
            {
                PuzzleKit.TryFindFreeSpot(floor, outward, out var spot, 0.8f, arena);
                key.position = spot;
                key.gameObject.SetActive(true);
            }
            if (!restoring) Toast("El armario se abre. Algo ha caído al suelo.");
        }
    }
}
