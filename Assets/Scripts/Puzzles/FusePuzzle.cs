using UnityEngine;

namespace EscapeRoom.Puzzles
{
    /// <summary>
    /// M5 - Panel de fusibles. Los 3 fusibles los manda el Cirujano por el tubo (salen del último cajón de Q3). Con los tres
    /// colocados se activan los interruptores de colores, que hay que pulsar en el orden que indica el registro del monitor.
    /// Al acertar la máquina queda en espera y se desbloquea el interruptor general (M6).
    /// </summary>
    public class FusePuzzle : PuzzleBase
    {
        int _fuses;
        int _step;
        bool _done;
        Transform[] _switch = new Transform[3];

        protected override void Setup()
        {
            Hook(Find("Prop_FuseBox"), _ => Insert(), "Panel de fusibles", "Revisar", "Necesita 3 fusibles");
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                _switch[i] = Find("Switch_" + i);
                Hook(_switch[i], _ => Flip(idx), $"Interruptor {PuzzleData.FuseColorNames[i].ToLower()}", "Accionar");
            }
        }

        public override void Restore(int mask)
        {
            if (!Has(mask, Bit.M5)) return;
            _fuses = 3;
            _step = 3;
            _done = true;
            LocalProgress.Set("m5");
        }

        void Insert()
        {
            var box = Find("Prop_FuseBox");
            if (_fuses >= 3) { Toast(_done ? "La máquina está en espera." : "Pulsa los interruptores en el orden del registro."); return; }

            int have = Inventory.CountOf(ItemId.Fuse);
            if (have == 0)
            {
                Sfx.Play("denied", box.position);
                Toast(PuzzleContext.EffectiveSolo ? $"Faltan fusibles ({_fuses}/3). Busca en las taquillas." : $"Faltan fusibles ({_fuses}/3). Los tiene el Cirujano: tienen que llegar por el tubo.");
                return;
            }
            int put = Mathf.Min(have, 3 - _fuses);
            Inventory.Remove(ItemId.Fuse, put);
            _fuses += put;
            Sfx.Play("fuse_in", box.position);
            Toast(_fuses < 3 ? $"Fusibles colocados: {_fuses}/3." : "Fusibles instalados. Mira el registro del monitor y activa los interruptores.");
        }

        void Flip(int i)
        {
            if (_done) { Toast("Ya está hecho."); return; }
            if (_fuses < 3)
            {
                Sfx.Play("denied", _switch[i].position);
                Toast("Sin fusibles no hay corriente.");
                return;
            }

            Sfx.Play("toggle", _switch[i].position);
            if (i == PuzzleData.FuseOrder[_step])
            {
                _step++;
                if (_step >= 3)
                {
                    _done = true;
                    LocalProgress.Set("m5");
                    Solved(Bit.M5);
                    Sfx.Play("power_up", _switch[i].position);
                    Toast("Secuencia correcta. La máquina pasa a espera: ya puedes usar el interruptor general.");
                }
                else Toast($"Correcto ({_step}/3).");
            }
            else
            {
                _step = 0;
                Sfx.Play("error", _switch[i].position);
                TeamLink.Penalize(10, "Orden incorrecto");
                Toast("Orden incorrecto. Los interruptores se reinician.");
            }
        }
    }
}
