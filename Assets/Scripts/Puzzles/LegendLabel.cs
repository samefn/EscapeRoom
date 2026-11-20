using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// M1 - Etiqueta de una caja: lleva un símbolo y una cifra (la cifra sale de la semilla). Al examinarla se añade a la leyenda
    /// de la pizarra. Con las 8 etiquetas el Operador puede traducir los símbolos que ve el Cirujano en el proyector.
    /// </summary>
    public class LegendLabel : PuzzleBase
    {
        [Range(0, 7)] public int symbol;
        TextMesh _digit;
        bool _set;

        protected override void Setup()
        {
            var d = transform.Find("Digit");
            _digit = d != null ? d.GetComponent<TextMesh>() : null;
            Hook(transform, _ => Examine(), "Etiqueta", "Examinar");
        }

        public override void Restore(int mask)
        {
            if (Has(mask, Bit.M1)) LocalProgress.Set("leg" + symbol);
        }

        void Update()
        {
            if (_set || !PuzzleData.Ready || _digit == null) return;
            _digit.text = PuzzleData.LegendM[symbol].ToString();
            _set = true;
        }

        void Examine()
        {
            bool first = !LocalProgress.Has("leg" + symbol);
            LocalProgress.Set("leg" + symbol);
            Sfx.Play("page", transform.position);

            int found = 0;
            for (int s = 0; s < 8; s++) if (LocalProgress.Has("leg" + s)) found++;

            var lines = new List<NoteLine> { new NoteLine($"= {PuzzleData.LegendM[symbol]}", symbol), new NoteLine($"Etiquetas encontradas: {found}/8") };
            PuzzleUI.ShowNote("Etiqueta de la caja", lines);

            if (first && found == 8)
            {
                Solved(Bit.M1);
                Toast("Leyenda completa. Está apuntada en la pizarra.");
            }
        }
    }
}
