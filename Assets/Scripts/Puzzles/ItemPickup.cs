using System;
using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>Objeto que se recoge y pasa al inventario.</summary>
    [RequireComponent(typeof(Interactable))]
    public class ItemPickup : MonoBehaviour
    {
        public ItemId item = ItemId.KeyCabinet;
        public int count = 1;
        public string label = "Objeto";

        public static event Action<ItemPickup> PickedUp;

        void Awake()
        {
            var it = GetComponent<Interactable>();
            it.displayName = label;
            it.verb = "Recoger";
            it.message = "";
            it.onInteract.AddListener(OnInteract);
        }

        void OnInteract(Interactable i)
        {
            var arena = GetComponentInParent<ArenaSlot>();
            if (arena == null || !PuzzleContext.Ready || arena.slotIndex != PuzzleContext.LocalSlot || PuzzleContext.GameOver) return;
            if (!Inventory.Add(item, count))
            {
                PuzzleUI.Toast("Inventario lleno.");
                Sfx.Play2D("denied");
                return;
            }
            Sfx.Play2D(item == ItemId.KeyCabinet || item == ItemId.KeyLocker ? "keys" : "pickup");
            PuzzleUI.Toast(count > 1 ? $"Recogido: {label} x{count}" : $"Recogido: {label}");
            gameObject.SetActive(false);
            PickedUp?.Invoke(this);
        }
    }
}
