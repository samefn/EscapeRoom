using System.Collections.Generic;
using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// Q6 / M6 - Palanca final sincronizada. El Cirujano monta la palanca que le envió el Operador y desbloquea la puerta;
    /// el Operador usa el interruptor general cuando la máquina está en espera. Los dos tienen que tirar con menos de 5 segundos
    /// de diferencia. El host decide (TeamState.RPC_Press). Éxito: la puerta del Quirófano se abre y la máquina se apaga.
    /// </summary>
    public class SyncLever : PuzzleBase
    {
        public Role role = Role.Cirujano;

        const float WaitSeconds = 5.5f;

        bool _mounted;
        bool _waiting;
        float _pressedAt;
        bool _fired;
        Transform _handle, _crowbarVisual;
        Quaternion _handleRest;
        AudioSource _beat;

        // apagado de la máquina
        readonly List<Light> _lights = new List<Light>();
        readonly List<float> _intensity = new List<float>();
        float _fade = -1f;

        protected override void Setup()
        {
            _handle = PuzzleKit.Find(transform, "Handle");
            if (_handle != null) _handleRest = _handle.localRotation;
            var cv = PuzzleKit.Find(transform, "Lever_Crowbar");
            _crowbarVisual = cv;
            if (role == Role.Cirujano && _crowbarVisual != null) _crowbarVisual.gameObject.SetActive(false);
            Hook(transform, _ => Use(), role == Role.Cirujano ? "Palanca de la puerta" : "Interruptor general", "Tirar");
            _beat = Sfx.MakeLoop(gameObject, "heartbeat", 0f, true);   // latido mientras esperas al compañero
        }

        void Use()
        {
            if (TeamLink.Finished) { Toast("Ya habéis escapado."); return; }

            // Modo individual (1v1, prueba o compañero desconectado): primero hay que haber resuelto TODOS los demás puzzles de la sala
            bool solo = PuzzleContext.EffectiveSolo;
            if (solo)
            {
                int first = role == Role.Cirujano ? 0 : 6, missing = 0;
                for (int i = first; i < first + 5; i++) if (!TeamLink.IsSolved(i)) missing++;
                if (missing > 0)
                {
                    Sfx.Play("denied", transform.position);
                    Toast($"Aún te faltan {missing} puzzles de esta sala antes de usar la palanca.");
                    return;
                }
            }

            if (role == Role.Cirujano)
            {
                if (!TeamLink.IsSolved(Bit.Q5))
                {
                    Sfx.Play("denied", transform.position);
                    Toast("La cerradura de la puerta sigue bloqueada (teclado).");
                    return;
                }
                if (!_mounted)
                {
                    if (!Inventory.Has(ItemId.Crowbar))
                    {
                        Sfx.Play("denied", transform.position);
                        Toast(PuzzleContext.EffectiveSolo ? "Falta la palanca. Estaba en el último cajón que abriste." : "Falta la palanca. El Operador la tiene en una taquilla: que te la envíe por el tubo.");
                        return;
                    }
                    Inventory.Remove(ItemId.Crowbar);
                    _mounted = true;
                    if (_crowbarVisual != null) _crowbarVisual.gameObject.SetActive(true);
                    Sfx.Play("place", transform.position);
                    Toast(PuzzleContext.EffectiveSolo ? "Palanca instalada. Vuelve a usar la palanca para tirar de ella." : "Palanca instalada. Avisa a tu compañero: tirad a la vez.");
                    return;
                }
            }
            else if (!TeamLink.IsSolved(Bit.M5))
            {
                Sfx.Play("denied", transform.position);
                Toast("La máquina todavía no está en espera (panel de fusibles).");
                return;
            }

            _waiting = true;
            _pressedAt = Time.time;
            TeamLink.B.RequestPress(role);
            Sfx.Play(role == Role.Cirujano ? "lever" : "lever_big", transform.position);
            if (_handle != null) _handle.localRotation = _handleRest * Quaternion.Euler(55f, 0f, 0f);
            Toast(solo ? "Tiras de la palanca..." : "Esperando a tu compañero (5 s)... ¡que tire también!");
        }

        void Update()
        {
            if (arena == null || !PuzzleContext.Ready) return;

            // Resultado (lo ve cada jugador en su propia arena, también el que no estaba tirando)
            if (_beat != null) _beat.volume = Mathf.MoveTowards(_beat.volume, Active && _waiting && !TeamLink.Finished ? 0.7f : 0f, Time.deltaTime * 3f);
            if (InMyArena && TeamLink.Finished && !_fired)
            {
                _fired = true;
                _waiting = false;
                Sfx.Play("sync_ok", transform.position);
                OnFinished();
            }

            if (Active && _waiting && !TeamLink.Finished && Time.time - _pressedAt > WaitSeconds)
            {
                _waiting = false;
                if (_handle != null) _handle.localRotation = _handleRest;
                Sfx.Play("sync_fail", transform.position);
                if (PuzzleContext.EffectiveSolo) Toast("No ha pasado nada. Inténtalo otra vez.");
                else TeamLink.Penalize(20, "Palancas desincronizadas");
            }

            if (_fade >= 0f && _fade < 1f)
            {
                _fade = Mathf.Min(1f, _fade + Time.deltaTime / 4f);
                for (int i = 0; i < _lights.Count; i++)
                    if (_lights[i] != null) _lights[i].intensity = Mathf.Lerp(_intensity[i], 0f, _fade);
            }
        }

        void OnFinished()
        {
            if (role == Role.Cirujano)
            {
                OpenDoors();
                Toast("¡La puerta se abre! Habéis escapado.");
            }
            else
            {
                foreach (var l in arena.GetComponentsInChildren<Light>())
                {
                    _lights.Add(l);
                    _intensity.Add(l.intensity);
                }
                _fade = 0f;
                Sfx.Play("power_down", transform.position);

                // La puerta de salida se desbloquea y se abre (gira sobre su bisagra hacia el interior).
                var frame = Find("Door_V3 (2)");
                var door = frame != null ? PuzzleKit.Find(frame, "Door") : null;
                if (door != null)
                {
                    Vector3 outward = PuzzleKit.TowardRoom(arena, door.position);
                    var m = door.GetComponent<Mover>() ?? door.gameObject.AddComponent<Mover>();
                    m.Configure(Vector3.zero, Mover.SwingSign(door, outward, 100f) * 100f, 2.5f).SetOpen(true);
                }
                Toast("La máquina se apaga. Habéis escapado.");
            }
        }

        void OpenDoors()
        {
            var doors = new List<Transform>();
            foreach (var n in new[] { "DoorD_Left", "DoorD_Right" })
            {
                var d = Find(n);
                if (d != null) doors.Add(d);
            }
            if (doors.Count == 0) return;

            Vector3 center = Vector3.zero;
            foreach (var d in doors) center += d.position;
            center /= doors.Count;

            foreach (var d in doors)
            {
                Vector3 away = d.position - center;
                Vector3 axis = Mathf.Abs(away.x) >= Mathf.Abs(away.z) ? new Vector3(Mathf.Sign(away.x == 0f ? 1f : away.x), 0f, 0f)
                                                                       : new Vector3(0f, 0f, Mathf.Sign(away.z));
                float width = 1.8f;
                if (PuzzleKit.TryBounds(d.gameObject, out var b)) width = Mathf.Max(Mathf.Abs(axis.x) > 0 ? b.size.x : b.size.z, 0.8f);
                var m = d.GetComponent<Mover>() ?? d.gameObject.AddComponent<Mover>();
                m.Configure(axis * width * 0.95f, 0f, 2.5f).SetOpen(true);
            }
            Sfx.Play("door_open", doors[0].position);
        }
    }
}
