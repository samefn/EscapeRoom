using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Mensajes rápidos entre compañeros: 12 frases fijas, sin texto libre (las pruebas se hacen con los jugadores en la misma sala,
    /// así que esto refuerza la información; no la sustituye). Viajan por TeamState como un contador + el número de frase:
    /// solo se envían 24 bits, no texto. Cada mensaje enviado y recibido tiene su sonido.
    /// </summary>
    public static class QuickMessages
    {
        public static readonly string[] Phrases =
        {
            "¿Qué ves en tu sala?",
            "Necesito un código",
            "Ya lo tengo resuelto",
            "Te envío algo por el tubo",
            "Revisa el tubo",
            "Dime los números que tienes",
            "Dime qué símbolos ves",
            "Espera un momento",
            "¡Prepárate para la palanca!",
            "¡Tira ahora!",
            "Estoy atascado",
            "¡Bien hecho!",
        };

        public struct Entry { public bool mine; public string text; public float time; }

        public static readonly List<Entry> Log = new List<Entry>();
        const float Cooldown = 0.8f;

        static int _seen = -1;
        static float _lastSend = -10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();

        public static void Clear() { Log.Clear(); _seen = -1; _lastSend = -10f; }

        public static string PartnerName => PuzzleContext.LocalRole == Role.Cirujano ? "Operador" : "Cirujano";

        public static bool Send(int index)
        {
            if (!PuzzleContext.Ready || PuzzleContext.Backend == null || index < 0 || index >= Phrases.Length) return false;
            if (Time.unscaledTime - _lastSend < Cooldown) return false;
            _lastSend = Time.unscaledTime;
            PuzzleContext.Backend.RequestMessage(PuzzleContext.LocalRole, index);
            Add(true, Phrases[index]);
            Sfx.Play2D("msg_sent");
            return true;
        }

        /// <summary>Se llama cada fotograma: ¿ha enviado algo el compañero?</summary>
        public static void Poll()
        {
            var b = PuzzleContext.Backend;
            if (b == null) return;
            int v = b.Get(TeamKeys.Msg(TeamKeys.Other(PuzzleContext.LocalRole)));
            int seq = v >> 8;
            if (_seen < 0) { _seen = seq; return; }   // lo que ya había antes de entrar no suena
            if (seq == _seen) return;
            _seen = seq;
            int id = v & 0xFF;
            if (id < 0 || id >= Phrases.Length) return;
            Add(false, Phrases[id]);
            Sfx.Play2D("msg_recv");
        }

        static void Add(bool mine, string text)
        {
            Log.Add(new Entry { mine = mine, text = text, time = Time.unscaledTime });
            if (Log.Count > 30) Log.RemoveAt(0);
        }
    }
}
