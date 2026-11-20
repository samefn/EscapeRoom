using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Tubo neumático. Hay una boca en cada sala (junto al lavabo). Se elige un objeto del inventario (1-5) y se envía:
    /// la cápsula tarda unos segundos y aparece en la boca del compañero. Lleva un objeto (o un montón) a la vez.
    /// El estado viaja por TeamState: lo único que cruza de una sala a la otra.
    /// </summary>
    public class TubeMouth : PuzzleBase
    {
        const float TravelSeconds = 4f;

        Transform _capsule;
        Interactable _it;
        int _lastSeq = -1;
        float _arriveAt = -1f;
        bool _arrived;
        bool _ignoreUntilEmpty;   // acabo de recoger la cápsula: el estado de red tarda un instante en vaciarse

        protected override void Setup()
        {
            var c = Find("TubeCapsule_" + (arena != null ? arena.slotIndex : 0));
            _capsule = c != null ? c : PuzzleKit.Find(transform, "TubeCapsule");
            if (_capsule != null) _capsule.gameObject.SetActive(false);
            _it = Hook(transform, _ => Use(), "Tubo neumático", "Usar");
        }

        static int Encode(ItemId item, int count) => (int)item | (count << 8);
        static ItemId ItemOf(int payload) => (ItemId)(payload & 0xFF);
        static int CountOf(int payload) => payload >> 8;

        void Update()
        {
            if (!Active) return;
            if (PuzzleContext.EffectiveSolo)
            {
                if (_it != null) { _it.available = false; _it.hint = "Sin compañero"; }
                if (_capsule != null) _capsule.gameObject.SetActive(false);
                return;
            }
            if (_it != null) _it.available = true;
            Role me = MyRole;
            int seq = TeamLink.Get(TeamKeys.TubeSeq(me));
            int payload = TeamLink.Get(TeamKeys.TubeItem(me));

            if (_lastSeq < 0) _lastSeq = seq;   // lo que ya estaba antes de empezar no cuenta como envío nuevo
            if (seq != _lastSeq)
            {
                _lastSeq = seq;
                _arrived = false;
                _arriveAt = Time.time + TravelSeconds;
                if (_capsule != null) _capsule.gameObject.SetActive(false);
                Toast("Se oye algo en el tubo...");
                Sfx.Play("tube_send", transform.position, 0.5f);
            }

            if (payload == 0)
            {
                _ignoreUntilEmpty = false;
                _arrived = false;
                _arriveAt = -1f;
                if (_capsule != null) _capsule.gameObject.SetActive(false);
            }
            else if (_ignoreUntilEmpty) { }
            else if (!_arrived && _arriveAt > 0f && Time.time >= _arriveAt)
            {
                _arrived = true;
                if (_capsule != null) _capsule.gameObject.SetActive(true);
                Sfx.Play("capsule", transform.position);
                Toast("¡Ha llegado una cápsula por el tubo!");
            }
            else if (!_arrived && _arriveAt < 0f)
            {
                _arrived = true;   // ya había algo cuando se entró (reconexión)
                if (_capsule != null) _capsule.gameObject.SetActive(true);
            }

            if (_it != null)
            {
                _it.verb = _arrived ? "Abrir" : "Enviar por";
                _it.hint = _arrived ? "Hay una cápsula" : (payload != 0 ? "Cápsula en camino..." : "Elige un objeto con 1-5");
            }
        }

        void Use()
        {
            Role me = MyRole;
            int mine = TeamLink.Get(TeamKeys.TubeItem(me));

            // 1) Recoger lo que ha llegado
            if (mine != 0)
            {
                if (!_arrived) { Toast("La cápsula todavía viaja..."); Sfx.Play("denied", transform.position); return; }
                var item = ItemOf(mine);
                int count = CountOf(mine);
                if (!Inventory.Add(item, count)) { Toast("Inventario lleno."); Sfx.Play("denied", transform.position); return; }
                TeamLink.B.RequestTubeTake(me);
                _arrived = false;
                _ignoreUntilEmpty = true;
                if (_capsule != null) _capsule.gameObject.SetActive(false);
                Sfx.Play("pickup", transform.position);
                Toast($"Recibido: {Inventory.NameOf(item)}" + (count > 1 ? $" x{count}" : ""));
                return;
            }

            // 2) Enviar el objeto seleccionado
            var st = Inventory.At(Inventory.Selected);
            if (st.item == ItemId.None)
            {
                Toast("Elige primero un objeto del inventario (teclas 1-5).");
                Sfx.Play("denied", transform.position);
                return;
            }
            Role other = TeamKeys.Other(me);
            if (TeamLink.Get(TeamKeys.TubeItem(other)) != 0)
            {
                Toast("El tubo todavía está ocupado.");
                Sfx.Play("denied", transform.position);
                return;
            }

            TeamLink.B.RequestTubeSend(other, Encode(st.item, st.count));
            Inventory.Remove(st.item, st.count);
            Sfx.Play("tube_send", transform.position);
            Toast($"{Inventory.NameOf(st.item)} enviado. Tarda unos segundos.");
        }
    }
}
