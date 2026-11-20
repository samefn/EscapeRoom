using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// M2 - Válvulas. Tres tuberías con volante y manómetro. Cada uso suma 1 a la presión (0-9). Hay que dejarlas en los valores
    /// que ha leído el Cirujano en la tinta térmica (Q2). Al acertar se enciende la pantalla con 3 símbolos.
    /// </summary>
    public class ValvePuzzle : PuzzleBase
    {
        readonly int[] _value = new int[3];
        readonly TextMesh[] _readout = new TextMesh[3];
        readonly Transform[] _wheel = new Transform[3];
        bool _done;

        protected override void Setup()
        {
            var pipes = PuzzleKit.FindAll(arena.transform, t => t.name.StartsWith("Pipe"));
            pipes.Sort((a, b) => a.position.x.CompareTo(b.position.x));

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                _wheel[i] = Find("Prop_Valve_" + i);
                var r = Find("Readout_Valve_" + i);
                _readout[i] = r != null ? r.GetComponent<TextMesh>() : null;
                Hook(_wheel[i], _ => Turn(idx), $"Válvula {i + 1}", "Girar", "Clic: +1 a la presión");
                if (i < pipes.Count) Hook(pipes[i], _ => Turn(idx), $"Válvula {i + 1}", "Girar", "Clic: +1 a la presión");
                Show(i);
            }
        }

        public override void Restore(int mask)
        {
            if (!Has(mask, Bit.M2)) return;
            _done = true;
            LocalProgress.Set("valves");
            for (int i = 0; i < 3; i++) { _value[i] = PuzzleData.Pressure[i]; Show(i); }
        }

        void Turn(int i)
        {
            if (_done) { Toast("Las válvulas están fijadas."); return; }
            _value[i] = (_value[i] + 1) % 10;
            if (_wheel[i] != null)
            {
                _wheel[i].Rotate(0f, 0f, 36f, Space.Self);   // el volante gira un poco
                Sfx.Play(_value[i] % 2 == 0 ? "valve" : "valve2", _wheel[i].position);
            }
            Show(i);

            if (_value[0] == PuzzleData.Pressure[0] && _value[1] == PuzzleData.Pressure[1] && _value[2] == PuzzleData.Pressure[2])
            {
                _done = true;
                LocalProgress.Set("valves");
                Solved(Bit.M2);
                Toast("Presión correcta. La pantalla se ha encendido.");
            }
        }

        void Show(int i)
        {
            if (_readout[i] == null) return;
            _readout[i].text = _value[i].ToString();
            _readout[i].color = _done ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.9f, 0.4f);
        }
    }
}
