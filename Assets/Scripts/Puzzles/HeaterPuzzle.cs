using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Q2 - Tinta térmica. El calentador sube la temperatura de una nota de pared; las 3 cifras (la presión que necesita el
    /// Operador para M2) aparecen al pasar 30, 55 y 80 °C. A 100 °C el calentador se sobrecalienta y se apaga unos segundos
    /// (y la nota se borra). El grifo enfría. Hay que leer las tres cifras seguidas.
    /// </summary>
    public class HeaterPuzzle : PuzzleBase
    {
        static readonly float[] Threshold = { 30f, 55f, 80f };
        const float HeatRate = 4.5f, CoolRate = 1.2f, TapCoolRate = 7f, Overheat = 100f;

        float _temp;
        bool _on, _tapOn, _locked, _done;
        float _lockUntil;
        readonly bool[] _shown = new bool[3];

        Transform _heater, _tap;
        Interactable _heaterIt, _tapIt;
        TextMesh _note;
        AudioSource _hum;

        protected override void Setup()
        {
            _heater = Find("Heater");
            var taps = PuzzleKit.FindAll(arena.transform, t => t.name.StartsWith("Tap"));
            _tap = taps.Count > 0 ? taps[0] : null;
            var noteT = Find("ThermalNote");
            _note = noteT != null ? noteT.GetComponent<TextMesh>() : null;

            _heaterIt = Hook(_heater, _ => ToggleHeater(), "Calentador", "Encender");
            _tapIt = Hook(_tap, _ => ToggleTap(), "Grifo", "Abrir");
            if (_heater != null) _hum = Sfx.MakeLoop(_heater.gameObject, "heater_hum", 0f, true);
            Refresh();
        }

        public override void Restore(int mask) { if (Has(mask, Bit.Q2)) _done = true; }

        void ToggleHeater()
        {
            if (_locked) { Toast("El calentador está sobrecalentado. Espera."); Sfx.Play("error", _heater.position); return; }
            _on = !_on;
            Sfx.Play(_on ? "heater_on" : "click", _heater.position);
            Refresh();
        }

        void ToggleTap()
        {
            _tapOn = !_tapOn;
            Sfx.Play(_tapOn ? "tap_on" : "click", _tap.position);
            Refresh();
        }

        void Refresh()
        {
            if (_heaterIt != null) { _heaterIt.verb = _on ? "Apagar" : "Encender"; _heaterIt.hint = $"Temperatura: {_temp:0} °C"; }
            if (_tapIt != null) { _tapIt.verb = _tapOn ? "Cerrar" : "Abrir"; _tapIt.hint = _tapOn ? "El agua enfría la nota" : ""; }
        }

        void Update()
        {
            if (_hum != null) _hum.volume = Mathf.MoveTowards(_hum.volume, Active && _on ? 0.35f : 0f, Time.deltaTime * 1.5f);
            if (!Active) return;

            if (!_done)
            {
                if (_locked && Time.time >= _lockUntil) { _locked = false; Refresh(); }

                float rate = (_on ? HeatRate : -CoolRate) - (_tapOn ? TapCoolRate : 0f);
                _temp = Mathf.Clamp(_temp + rate * Time.deltaTime, 18f, 110f);

                if (_on && _temp >= Overheat)
                {
                    _on = false;
                    _locked = true;
                    _lockUntil = Time.time + 6f;
                    for (int i = 0; i < 3; i++) _shown[i] = false;
                    Sfx.Play("overheat", _heater != null ? _heater.position : transform.position);
                    Toast("¡Sobrecalentado! La nota se borra. Enfría con el grifo.");
                }

                for (int i = 0; i < 3; i++)
                {
                    if (_temp >= Threshold[i]) _shown[i] = true;
                    else if (_temp < 15f) _shown[i] = false;
                }

                bool all = _shown[0] && _shown[1] && _shown[2];
                if (all && _temp >= Threshold[2])
                {
                    _done = true;
                    Notebook.Add("presion", "Presión (nota térmica)", $"Valores: {PuzzleData.PressureText}");
                    Solved(Bit.Q2);
                    Toast(PuzzleContext.EffectiveSolo ? $"Nota térmica leída: presión {PuzzleData.PressureText}. Queda en tu cuaderno (Tab)." : $"Nota térmica leída: presión {PuzzleData.PressureText}. Díselo al Operador.");
                }
                if (_heaterIt != null && Time.frameCount % 15 == 0) _heaterIt.hint = $"Temperatura: {_temp:0} °C";
            }

            if (_note != null)
            {
                string d(int i) => _shown[i] || _done ? PuzzleData.Pressure[i].ToString() : "·";
                _note.text = $"{_temp:0} °C\n{d(0)}   {d(1)}   {d(2)}";
                float k = Mathf.InverseLerp(20f, 100f, _temp);
                _note.color = Color.Lerp(new Color(0.75f, 0.75f, 0.8f), new Color(1f, 0.35f, 0.15f), k);
            }
        }
    }
}
