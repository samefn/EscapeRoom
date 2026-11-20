using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    public enum ItemId
    {
        None = 0,
        KeyCabinet = 1,   // llave de los cajones (sale del armario de Q1)
        KeyLocker = 2,    // llave de las taquillas (bajo el colchón, M3)
        Fuse = 3,         // fusibles (Q3 -> tubo -> M5)
        Crowbar = 4,      // la palanca (M3 -> tubo -> Q6)
        Slide = 5,        // diapositivas del proyector (Q4)
    }

    /// <summary>Inventario local del jugador: 5 huecos; los objetos iguales se apilan (3 fusibles = 1 hueco).</summary>
    public static class Inventory
    {
        public const int Slots = 5;

        public struct Stack { public ItemId item; public int count; }

        static readonly List<Stack> Items = new List<Stack>();
        public static int Selected { get; private set; }

        public static int Count => Items.Count;
        public static Stack At(int i) => i >= 0 && i < Items.Count ? Items[i] : default;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();

        public static void Clear() { Items.Clear(); Selected = 0; }

        public static ItemId SelectedItem => At(Selected).item;

        public static void Select(int i) { if (i >= 0 && i < Slots) Selected = i; }

        public static int CountOf(ItemId item)
        {
            foreach (var s in Items) if (s.item == item) return s.count;
            return 0;
        }

        public static bool Has(ItemId item, int count = 1) => CountOf(item) >= count;

        /// <summary>False si no cabe (inventario lleno).</summary>
        public static bool Add(ItemId item, int count = 1)
        {
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].item == item) { Items[i] = new Stack { item = item, count = Items[i].count + count }; return true; }
            if (Items.Count >= Slots) return false;
            Items.Add(new Stack { item = item, count = count });
            return true;
        }

        public static bool Remove(ItemId item, int count = 1)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].item != item) continue;
                if (Items[i].count < count) return false;
                int left = Items[i].count - count;
                if (left == 0) Items.RemoveAt(i); else Items[i] = new Stack { item = item, count = left };
                return true;
            }
            return false;
        }

        public static string NameOf(ItemId item)
        {
            switch (item)
            {
                case ItemId.KeyCabinet: return "Llave de cajón";
                case ItemId.KeyLocker: return "Llave de taquilla";
                case ItemId.Fuse: return "Fusible";
                case ItemId.Crowbar: return "Palanca";
                case ItemId.Slide: return "Diapositiva";
                default: return "";
            }
        }

        public static Color ColorOf(ItemId item)
        {
            switch (item)
            {
                case ItemId.KeyCabinet: return new Color(0.95f, 0.8f, 0.25f);
                case ItemId.KeyLocker: return new Color(0.85f, 0.6f, 0.3f);
                case ItemId.Fuse: return new Color(0.9f, 0.3f, 0.25f);
                case ItemId.Crowbar: return new Color(0.55f, 0.65f, 0.8f);
                case ItemId.Slide: return new Color(0.7f, 0.9f, 0.95f);
                default: return Color.gray;
            }
        }
    }

    /// <summary>Pistas ya vistas. Se abre con Tab; cada pista aparece una sola vez.</summary>
    public static class Notebook
    {
        public struct Entry { public string key, title, text; }
        public static readonly List<Entry> Entries = new List<Entry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();

        public static void Clear() => Entries.Clear();

        public static void Add(string key, string title, string text)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].key == key) { Entries[i] = new Entry { key = key, title = title, text = text }; return; }
            Entries.Add(new Entry { key = key, title = title, text = text });
        }
    }
}
