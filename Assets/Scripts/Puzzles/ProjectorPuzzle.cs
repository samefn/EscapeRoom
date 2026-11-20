using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Q4 - Proyector de diapositivas. Hay 3 diapositivas repartidas por el quirófano. Con las tres puestas, el proyector
    /// enseña 3 símbolos. Hay que traducirlos con la leyenda de las cajas del Operador (M1): son la primera mitad del código de la puerta.
    /// </summary>
    public class ProjectorPuzzle : PuzzleBase
    {
        int _inserted;
        Transform _projector;
        Light _beam;

        protected override void Setup()
        {
            _projector = Find("Prop_Projector");
            Hook(_projector, _ => Use(), "Proyector", "Usar", "Necesita 3 diapositivas");
            var l = Find("ProjectorBeam");
            if (l != null) { _beam = l.GetComponent<Light>(); if (_beam != null) _beam.enabled = false; }
        }

        public override void Restore(int mask)
        {
            if (!Has(mask, Bit.Q4)) return;
            _inserted = 3;
            LocalProgress.Set("q4_seen");
            if (_beam != null) _beam.enabled = true;
        }

        void Use()
        {
            int have = Inventory.CountOf(ItemId.Slide);
            if (_inserted < 3 && have > 0)
            {
                int put = Mathf.Min(have, 3 - _inserted);
                Inventory.Remove(ItemId.Slide, put);
                _inserted += put;
                Sfx.Play("place", _projector.position);
                if (_inserted < 3) { Toast($"Diapositivas colocadas: {_inserted}/3."); return; }
                Toast("Las tres diapositivas están dentro. El proyector se enciende.");
                if (_beam != null) _beam.enabled = true;
                Solved(Bit.Q4);
            }
            else if (_inserted < 3)
            {
                Sfx.Play("denied", _projector.position);
                Toast($"Faltan diapositivas ({_inserted}/3). Búscalas por la sala.");
                return;
            }

            LocalProgress.Set("q4_seen");
            var lines = new List<NoteLine> { new NoteLine(PuzzleContext.EffectiveSolo ? "Proyección (tradúcela con la nota de la leyenda):" : "Proyección (tradúcela con la leyenda del Operador):") };
            for (int i = 0; i < 3; i++) lines.Add(new NoteLine($"Símbolo {i + 1}", PuzzleData.SymbolsA[i]));
            PuzzleUI.ShowNote("Proyector", lines, "proyector");
        }
    }
}
