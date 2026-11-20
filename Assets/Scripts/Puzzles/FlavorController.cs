using System.Text.RegularExpressions;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Da respuesta a TODOS los objetos interactuables de la arena que no son parte directa de un puzzle. Antes decían
    /// "todavía no tiene función"; ahora describen lo que son y, cuando toca, empujan hacia el puzzle que sí importa.
    /// Los puzzles que enganchan un objeto borran su mensaje, así que esto solo rellena lo que quede.
    /// </summary>
    public class FlavorController : PuzzleBase
    {
        const string Marker = "todavía no tiene función";

        static readonly (Regex pattern, string verb, string text)[] Rules =
        {
            (new Regex(@"^MedRackDoor_[LR]$"), "Abrir", "Armario vacío: polvo y frascos rotos."),
            (new Regex(@"^DoorD_"), "Examinar", "La puerta doble está sellada. Se abre con el código del teclado y la palanca final."),
            (new Regex(@"^Door"), "Examinar", "La puerta de salida está soldada. Se liberará cuando la máquina se apague."),
            (new Regex(@"^Tap"), "Abrir", "Sale agua turbia. No sirve de nada más."),
            (new Regex(@"^Sink_V1"), "Examinar", "Un lavabo seco. Junto a él sale la boca del tubo neumático."),
            (new Regex(@"^SmallMetalicCase"), "Abrir", "Una caja de herramientas oxidada. Está vacía."),
            (new Regex(@"^Case_Door"), "Abrir", "Una taquilla atascada por el óxido."),
            (new Regex(@"^CaseMetallic"), "Abrir", "Una taquilla atascada por el óxido."),
            (new Regex(@"^Box_V"), "Examinar", "Una caja sellada. Las que llevan etiqueta con símbolo forman la leyenda de la pizarra."),
            (new Regex(@"^BigShelf"), "Examinar", "Una estantería cargada de cajas. Busca las etiquetas con símbolos."),
            (new Regex(@"^Mattress"), "Examinar", "Un colchón mohoso. Algo metálico brilla encima."),
            (new Regex(@"^SurgeryTable"), "Examinar", "Una mesa de operaciones con correas. Sobre ella hay un expediente y una diapositiva."),
            (new Regex(@"^Paper"), "Leer", "Una nota arrugada. Está ilegible."),
            (new Regex(@"^Pipe"), "Examinar", "Una tubería caliente. El volante regula su presión."),
            (new Regex(@"^Tank"), "Examinar", "Un tanque metálico. Se llena desde el panel de la pared."),
            (new Regex(@"^Heater"), "Examinar", "Un calentador industrial."),
        };

        protected override void Setup()
        {
            foreach (var it in arena.GetComponentsInChildren<Interactable>(true))
            {
                if (string.IsNullOrEmpty(it.message) || !it.message.Contains(Marker)) continue;

                string text = "No hay nada útil aquí.";
                string name = it.gameObject.name;
                foreach (var r in Rules)
                {
                    if (!r.pattern.IsMatch(name)) continue;
                    text = r.text;
                    if (name.StartsWith("MedRackDoor") && it.transform.parent != null && it.transform.parent.name == "MedRack_V2 (1)")
                        text = "Cerrado con cerradura electrónica. Necesita el código del teclado de la pared.";
                    if (!string.IsNullOrEmpty(r.verb)) it.verb = r.verb;
                    break;
                }
                it.message = text;
            }
        }
    }
}
