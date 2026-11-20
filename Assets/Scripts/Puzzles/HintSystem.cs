using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Pistas progresivas. Si el equipo lleva 4 minutos sin resolver nada aparece una pista sobre el siguiente puzzle de TU sala;
    /// la segunda llega a los 3 minutos más, y así. Cada pista suma 15 s de gas y se guarda en el cuaderno.
    /// El reloj de "sin avanzar" se reinicia cuando cualquiera de los dos compañeros resuelve algo.
    /// </summary>
    public static class HintSystem
    {
        const float FirstAfter = 240f, NextAfter = 180f;
        const int Cost = 15;

        static readonly string[] Texts =
        {
            /* Q1 */ "El póster da un número a cada órgano y el expediente de la mesa dice en qué orden. Esos números abren el armario (teclado de la pared).",
            /* Q2 */ "Enciende el calentador y mira la nota de la pared: las cifras aparecen al subir la temperatura. Si llega a 100 °C se borra: enfría con el grifo.",
            /* Q3 */ "Los cajones numerados se abren con la llave que cayó del armario, en el orden que dice la placa del Operador. Un fallo los cierra todos.",
            /* Q4 */ "Hay tres diapositivas repartidas por el quirófano. Colócalas en el proyector de la mesa blanca.",
            /* Q5 */ "La puerta pide 6 cifras: tres de los símbolos del proyector y tres de los de la pantalla del Operador, cada grupo traducido con su leyenda.",
            /* Q6 */ "La palanca de la puerta necesita la palanca de las taquillas del Operador, enviada por el tubo. Luego tirad a la vez.",
            /* M1 */ "Las cajas de las estanterías llevan etiquetas con un símbolo y una cifra. Examina las ocho: se apuntan en la pizarra.",
            /* M2 */ "Las tres válvulas deben marcar la presión que el Cirujano lee en la tinta térmica (cada clic suma 1).",
            /* M3 */ "La llave está bajo el colchón. Solo una de las tres taquillas guarda la palanca.",
            /* M4 */ "Con el tanque de 5 litros y el de 3 hay que dejar exactamente 4 litros en el grande. Usa los seis botones del panel.",
            /* M5 */ "Los fusibles se los pide el Operador al Cirujano. El monitor del estante dice en qué orden pulsar los interruptores de colores.",
            /* M6 */ "El interruptor general funciona cuando la máquina está en espera. Tirad a la vez que vuestro compañero.",
        };

        // Versiones para quien juega solo (no hay compañero ni tubo): lo que cambia respecto a Texts.
        static readonly System.Collections.Generic.Dictionary<int, string> SoloTexts = new System.Collections.Generic.Dictionary<int, string>
        {
            { Bit.Q2, "Enciende el calentador y mira la nota de la pared hasta ver las tres cifras. Si llega a 100 °C se borra: enfría con el grifo." },
            { Bit.Q3, "Los cajones se abren con la llave que cayó del armario, en el orden de la nota pegada a la pared sur (junto al teclado del armario)." },
            { Bit.Q5, "La puerta pide 6 cifras: tres de los símbolos del proyector (con la nota de la leyenda de las cajas) y tres de la nota de símbolos (con la leyenda del póster)." },
            { Bit.Q6, "La palanca estaba en el último cajón. Móntala en la pared junto a la puerta y vuelve a usarla; antes hay que resolver Q1-Q5." },
            { Bit.M2, "Las tres válvulas deben marcar la presión de la nota pegada a la pared norte (cada clic suma 1)." },
            { Bit.M3, "La llave está sobre el colchón. Una taquilla guarda la palanca y los fusibles." },
            { Bit.M5, "Los fusibles estaban en la taquilla. El monitor del estante dice en qué orden pulsar los interruptores de colores." },
            { Bit.M6, "El interruptor general funciona cuando la máquina está en espera. Antes hay que resolver M1-M5." },
        };

        static int _prevMask = -1;
        static float _lastProgress;
        static int _given;

        public static void Clear() { _prevMask = -1; _given = 0; }

        public static void Tick()
        {
            if (PuzzleContext.GameOver) return;
            int mask = TeamLink.Get(TeamKeys.Solved);
            if (_prevMask != mask)
            {
                _prevMask = mask;
                _lastProgress = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - _lastProgress < (_given == 0 ? FirstAfter : NextAfter)) return;

            int first = PuzzleContext.LocalRole == Role.Cirujano ? 0 : 6;
            for (int i = first; i < first + 6; i++)
            {
                if (TeamLink.IsSolved(i)) continue;
                _given++;
                _lastProgress = Time.unscaledTime;
                string text = PuzzleContext.EffectiveSolo && SoloTexts.TryGetValue(i, out var st) ? st : Texts[i];
                Notebook.Add("pista" + _given, $"Pista {_given} ({Bit.Names[i]})", text);
                TeamLink.Penalize(Cost, "Has usado una pista");
                PuzzleUI.Toast("Pista: " + text + "  (+" + Cost + " s de gas)", 10f);
                return;
            }
        }
    }
}
