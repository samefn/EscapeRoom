using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// M4 - Tanques. Medir exactamente 4 litros con un tanque de 5 y otro de 3, usando los 6 botones del panel
    /// (llenar, trasvasar, vaciar). Al conseguirlo se abre una escotilla con una placa que lleva 3 números:
    /// son los cajones que el Cirujano debe abrir en Q3.
    /// </summary>
    public class TankPuzzle : PuzzleBase
    {
        static readonly string[] BtnName =
        {
            "Llenar tanque A (5 L)", "Llenar tanque B (3 L)", "Trasvasar A → B", "Trasvasar B → A", "Vaciar tanque A", "Vaciar tanque B"
        };

        int _a, _b;
        bool _done;
        TextMesh _display;
        Transform _hatch, _plate;
        Interactable _plateIt;

        protected override void Setup()
        {
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                Hook(Find("Tank_Btn_" + i), _ => Press(idx), BtnName[i], "Pulsar");
            }
            var d = Find("Tank_Display");
            _display = d != null ? d.GetComponent<TextMesh>() : null;
            _hatch = Find("Tank_Hatch");
            _plate = Find("Prop_Plate");
            // Los dos tanques grandes también se pueden examinar: dicen cuánta agua llevan.
            Hook(Find("Tank"), _ => Toast($"Tanque A: {_a}/{PuzzleData.TankA} L."), "Tanque A (5 L)", "Medir");
            Hook(Find("Tank_V2"), _ => Toast($"Tanque B: {_b}/{PuzzleData.TankB} L."), "Tanque B (3 L)", "Medir");
            _plateIt = Hook(_plate, _ => ReadPlate(), "Placa metálica", "Leer");
            if (_plateIt != null) _plateIt.available = false;
            Show();
        }

        public override void Restore(int mask)
        {
            if (!Has(mask, Bit.M4)) return;
            _done = true;
            _a = PuzzleData.TankGoal;
            Show();
            if (_hatch != null) { var m = _hatch.GetComponent<Mover>() ?? _hatch.gameObject.AddComponent<Mover>(); m.Configure(Vector3.up * 0.7f, 0f, 0.1f).SetOpen(true); }
            if (_plateIt != null) _plateIt.available = true;
        }

        void Press(int i)
        {
            if (_done) { Toast("La escotilla ya está abierta."); return; }
            Sfx.Play(i < 2 ? "water_fill" : "water_drain", transform.position);
            switch (i)
            {
                case 0: _a = PuzzleData.TankA; break;
                case 1: _b = PuzzleData.TankB; break;
                case 2: { int m = Mathf.Min(_a, PuzzleData.TankB - _b); _a -= m; _b += m; break; }
                case 3: { int m = Mathf.Min(_b, PuzzleData.TankA - _a); _b -= m; _a += m; break; }
                case 4: _a = 0; break;
                default: _b = 0; break;
            }
            Show();

            if (_a == PuzzleData.TankGoal)
            {
                _done = true;
                Show();
                Sfx.Play("bell", transform.position);
                Sfx.Play("hatch", _hatch != null ? _hatch.position : transform.position);
                Solved(Bit.M4);
                if (_hatch != null)
                {
                    var m = _hatch.GetComponent<Mover>() ?? _hatch.gameObject.AddComponent<Mover>();
                    m.Configure(Vector3.up * 0.7f, 0f, 1.2f).SetOpen(true);
                }
                if (_plateIt != null) _plateIt.available = true;
                Toast("¡4 litros exactos! La escotilla se abre y deja ver una placa.");
            }
        }

        void Show()
        {
            if (_display == null) return;
            _display.text = $"A {_a}/{PuzzleData.TankA} L\nB {_b}/{PuzzleData.TankB} L\nMeta: {PuzzleData.TankGoal} L en A";
            _display.color = _done ? new Color(0.4f, 1f, 0.5f) : new Color(0.6f, 0.9f, 1f);
        }

        void ReadPlate()
        {
            Sfx.Play("page", transform.position);
            var lines = new List<NoteLine>
            {
                new NoteLine("Placa grabada con tres números:"),
                new NoteLine($"      {PuzzleData.DrawerText}"),
                new NoteLine(PuzzleContext.EffectiveSolo ? "«Abre los cajones en este orden.»" : "«Abre los cajones en este orden.» (Díselo al Cirujano.)"),
            };
            PuzzleUI.ShowNote("Placa metálica", lines, "placa");
        }
    }
}
