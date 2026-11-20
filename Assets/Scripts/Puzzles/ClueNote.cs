using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>Objeto con un texto o una nota: la lee y la guarda en el cuaderno. Puede requerir una bandera local.</summary>
    [RequireComponent(typeof(Interactable))]
    public class ClueNote : PuzzleBase
    {
        public enum Clue { Story, Poster, Expediente, Bottles, Legend, MachineLog, TvSymbols, SoloDrawers, SoloLegendM, SoloSymbolsB, SoloPressure }

        public Clue clue = Clue.Story;
        public string requiresFlag = "";
        public string lockedMessage = "No funciona todavía.";

        protected override void Setup()
        {
            var it = GetComponent<Interactable>();
            it.message = "";
            it.onInteract.AddListener(_ => { if (Active) Read(); });
        }

        void Read()
        {
            if (!LocalProgress.Has(requiresFlag))
            {
                Toast(lockedMessage);
                Sfx.Play("denied", transform.position);
                return;
            }

            var lines = new List<NoteLine>();
            string title, key;
            switch (clue)
            {
                case Clue.Poster:
                    title = "Póster: anatomía y símbolos"; key = "poster";
                    lines.Add(new NoteLine("Órganos (número de catálogo):"));
                    for (int i = 0; i < PuzzleData.OrganNames.Length; i++)
                        lines.Add(new NoteLine($"   {PuzzleData.OrganNames[i]}  =  {PuzzleData.OrganNumber[i]}"));
                    lines.Add(new NoteLine("Leyenda de símbolos:"));
                    for (int s = 0; s < 8; s++) lines.Add(new NoteLine($"= {PuzzleData.LegendQ[s]}", s));
                    break;
                case Clue.Expediente:
                    title = "Expediente clínico"; key = "expediente";
                    lines.Add(new NoteLine("Paciente 0417. Orden del diagnóstico:"));
                    for (int i = 0; i < 3; i++) lines.Add(new NoteLine($"   {i + 1}.  {PuzzleData.OrganNames[PuzzleData.Q1Order[i]]}"));
                    lines.Add(new NoteLine("«Los números del póster abren el armario.»"));
                    break;
                case Clue.Bottles:
                    title = "Frascos Kronon-X"; key = "frascos";
                    lines.Add(new NoteLine("Experimental. Conservar entre 30 y 90 grados."));
                    lines.Add(new NoteLine("Nota de laboratorio: «la tinta térmica de la pared"));
                    lines.Add(new NoteLine("solo se lee cerca del límite. Enfría con el grifo.»"));
                    break;
                case Clue.Legend:
                    title = "Leyenda de las cajas"; key = "leyenda";
                    int found = 0;
                    for (int s = 0; s < 8; s++)
                    {
                        bool f = LocalProgress.Has("leg" + s);
                        if (f) found++;
                        lines.Add(new NoteLine(f ? $"= {PuzzleData.LegendM[s]}" : "= ?   (busca su etiqueta en las cajas)", s));
                    }
                    lines.Insert(0, new NoteLine($"Etiquetas encontradas: {found}/8"));
                    break;
                case Clue.MachineLog:
                    title = "Registro de la máquina"; key = "registro";
                    lines.Add(new NoteLine("Secuencia de arranque (interruptores):"));
                    for (int i = 0; i < 3; i++) lines.Add(new NoteLine($"   {i + 1}.  {PuzzleData.FuseColorNames[PuzzleData.FuseOrder[i]]}"));
                    break;
                case Clue.TvSymbols:
                    title = "Pantalla de la sala"; key = "pantalla";
                    lines.Add(new NoteLine(PuzzleContext.EffectiveSolo ? "Símbolos en pantalla:" : "Símbolos en pantalla (traduce con la leyenda del Cirujano):"));
                    for (int i = 0; i < 3; i++) lines.Add(new NoteLine($"Símbolo {i + 1}", PuzzleData.SymbolsB[i]));
                    break;
                case Clue.SoloDrawers:
                    title = "Nota: orden de los cajones"; key = "solo_cajones";
                    lines.Add(new NoteLine("Sin compañero que te lo diga, alguien dejó esta nota:"));
                    lines.Add(new NoteLine($"      {PuzzleData.DrawerText}"));
                    lines.Add(new NoteLine("«Abre los cajones en este orden.»"));
                    break;
                case Clue.SoloLegendM:
                    title = "Nota: leyenda de las cajas"; key = "solo_leyenda";
                    lines.Add(new NoteLine("Sin compañero que las lea, la leyenda completa de las cajas:"));
                    for (int s = 0; s < 8; s++) lines.Add(new NoteLine($"= {PuzzleData.LegendM[s]}", s));
                    break;
                case Clue.SoloSymbolsB:
                    title = "Nota: símbolos de la pantalla"; key = "solo_simbolos";
                    lines.Add(new NoteLine("La pantalla de la otra sala enseña estos símbolos (traduce con la leyenda del póster):"));
                    for (int i = 0; i < 3; i++) lines.Add(new NoteLine($"Símbolo {i + 1}", PuzzleData.SymbolsB[i]));
                    break;
                case Clue.SoloPressure:
                    title = "Nota: presión de las válvulas"; key = "solo_presion";
                    lines.Add(new NoteLine("Sin compañero que la lea en la tinta térmica, la presión necesaria:"));
                    lines.Add(new NoteLine($"      {PuzzleData.PressureText}"));
                    break;
                default:
                    title = "Nota"; key = null;
                    lines.Add(new NoteLine("«Si estás leyendo esto, ya has empezado el juego."));
                    lines.Add(new NoteLine("Tu compañero está en otra sala. Habla con él.»"));
                    break;
            }
            Sfx.Play("paper", transform.position);
            PuzzleUI.ShowNote(title, lines, key, false);
        }
    }
}
