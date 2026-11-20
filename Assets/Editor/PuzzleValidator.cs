using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EscapeRoom.Net;
using EscapeRoom.Puzzles;
using NetworkObject = Fusion.NetworkObject;

namespace EscapeRoom.EditorTools
{
    public class ValidationReport
    {
        public readonly List<string> Errors = new List<string>();     // el juego NO se puede resolver o no puede conectar: bloquean la build
        public readonly List<string> Warnings = new List<string>();   // dudas (alcance de objetos, audio ausente...): se muestran pero no bloquean
        public readonly List<string> Infos = new List<string>();
        public bool Ok => Errors.Count == 0;
        public void Err(string s) => Errors.Add(s);
        public void Warn(string s) => Warnings.Add(s);
        public void Info(string s) => Infos.Add(s);

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== Verificación de puzzles: {Errors.Count} errores, {Warnings.Count} avisos ===");
            foreach (var e in Errors) sb.AppendLine("[ERROR] " + e);
            foreach (var w in Warnings) sb.AppendLine("[aviso] " + w);
            foreach (var i in Infos) sb.AppendLine("[ok]    " + i);
            return sb.ToString();
        }
    }

    /// <summary>
    /// Comprueba, ANTES de construir, que el juego se puede terminar:
    ///  1. Datos: miles de semillas generan códigos válidos (y la secuencia de cajones solo usa cajones que existen).
    ///  2. Grafo de dependencias: existe un orden de resolución para 2v2 y para el modo individual.
    ///  3. Proyecto: escenas en Build Settings, prefabs de red asignados, App Id de Photon, reparto 1v1/2v2 correcto.
    ///  4. Escena Game: las 4 arenas y TODOS los objetos que exige cada puzzle (nombre, componente, configuración).
    ///  5. Alcance: cada objeto con el que hay que interactuar se puede apuntar desde un sitio al que el jugador llega andando.
    /// Menú: Escape Room > 9. La build de Windows (menú 5 o File > Build) se niega a construir si hay errores.
    /// </summary>
    public static class PuzzleValidator
    {
        const string MenuScenePath = "Assets/Scenes/Menu.unity";
        const string SelectScenePath = "Assets/Scenes/CharacterSelect.unity";
        const string GameScenePath = "Assets/Scenes/Game.unity";
        const string NetDir = "Assets/Prefabs/Network";
        const string PrefTicks = "EscapeRoom.ValidatedUtcTicks";
        const string PrefOk = "EscapeRoom.ValidatedOk";

        static readonly string[] WatchedFiles =
        {
            "Assets/Prefabs/Arenas/Arena_Quirofano.prefab", "Assets/Prefabs/Arenas/Arena_CuartoMaquinas.prefab",
            MenuScenePath, SelectScenePath, GameScenePath,
            NetDir + "/PlayerSession.prefab", NetDir + "/PlayerAvatar.prefab", NetDir + "/TeamState.prefab",
        };

        // ------------------------------------------------------------------ menú y estado

        [MenuItem("Escape Room/9 - Verificar que el juego se puede resolver")]
        public static void Menu()
        {
            var r = Run();
            Debug.Log(r.ToText());
            string head = r.Ok
                ? $"Todo correcto. {r.Warnings.Count} avisos (míralos en la consola)."
                : $"{r.Errors.Count} errores. El juego NO está listo:\n\n" + string.Join("\n", r.Errors.Take(8).Select(e => "• " + e));
            EditorUtility.DisplayDialog("Verificación de puzzles", head + "\n\nInforme completo: Logs/ValidacionPuzzles.txt", "Vale");
        }

        /// <summary>¿Hay una verificación correcta posterior a la última modificación de arenas, escenas y prefabs de red?</summary>
        public static bool IsFresh(out string why)
        {
            why = "";
            if (!EditorPrefs.GetBool(PrefOk, false) || !long.TryParse(EditorPrefs.GetString(PrefTicks, "0"), out long ticks) || ticks == 0)
            {
                why = "Todavía no se ha verificado el juego (o la última verificación tuvo errores).";
                return false;
            }
            var when = new DateTime(ticks, DateTimeKind.Utc);
            foreach (var f in WatchedFiles)
            {
                if (!File.Exists(f)) continue;
                if (File.GetLastWriteTimeUtc(f) > when) { why = $"{f} cambió después de la última verificación."; return false; }
            }
            return true;
        }

        public static void Remember(bool ok)
        {
            EditorPrefs.SetBool(PrefOk, ok);
            EditorPrefs.SetString(PrefTicks, DateTime.UtcNow.Ticks.ToString());
        }

        // ------------------------------------------------------------------ ejecución completa

        public static ValidationReport Run()
        {
            var r = new ValidationReport();
            if (EditorApplication.isPlaying) { r.Err("Sal del modo Play antes de verificar."); return r; }

            try
            {
                CheckData(r);
                CheckDependencyGraph(r);
                CheckProject(r);
                CheckGameScene(r);
                CheckAssets(r);
            }
            catch (Exception e)
            {
                r.Err("La verificación se interrumpió por una excepción: " + e.Message);
                Debug.LogException(e);
            }

            try { File.WriteAllText("Logs/ValidacionPuzzles.txt", r.ToText()); } catch { }
            Remember(r.Ok);
            return r;
        }

        // ------------------------------------------------------------------ 1. datos

        static void CheckData(ValidationReport r)
        {
            bool prev = PuzzleData.Verbose;
            PuzzleData.Verbose = false;
            int bad = 0;
            string firstBad = "";
            const int Seeds = 5000;

            void Fail(int seed, string what) { bad++; if (firstBad == "") firstBad = $"semilla {seed}: {what}"; }
            bool Distinct(int[] a) => a.Distinct().Count() == a.Length;

            for (int i = 1; i <= Seeds; i++)
            {
                int seed = i * 7919 + 13;
                PuzzleData.Ensure(seed);

                if (!Distinct(PuzzleData.OrganNumber) || PuzzleData.OrganNumber.Any(v => v < 1 || v > 9)) Fail(seed, "números de órgano");
                if (!Distinct(PuzzleData.Q1Order) || PuzzleData.Q1Order.Any(v => v < 0 || v > 4)) Fail(seed, "orden del expediente");
                if (PuzzleData.Q1Code.Length != 3) Fail(seed, "código del armario");
                if (PuzzleData.Pressure.Any(v => v < 1 || v > 9)) Fail(seed, "presión fuera de 1-9 (las válvulas llegan a 0-9)");
                if (!Distinct(PuzzleData.DrawerSeq) || PuzzleData.DrawerSeq.Any(v => !PuzzleData.DrawerNumbers.Contains(v))) Fail(seed, "cajón que no existe");
                if (!Distinct(PuzzleData.LegendQ) || !Distinct(PuzzleData.LegendM) || PuzzleData.LegendQ.Length != 8 || PuzzleData.LegendM.Length != 8) Fail(seed, "leyendas");
                if (!Distinct(PuzzleData.SymbolsA) || !Distinct(PuzzleData.SymbolsB)) Fail(seed, "símbolos repetidos");
                if (PuzzleData.DoorCode.Length != 6 || !PuzzleData.DoorCode.All(char.IsDigit)) Fail(seed, "código de la puerta");
                if (!Distinct(PuzzleData.FuseOrder) || PuzzleData.FuseOrder.Any(v => v < 0 || v > 2)) Fail(seed, "orden de fusibles");
                if (PuzzleData.CrowbarLocker < 0 || PuzzleData.CrowbarLocker > 2) Fail(seed, "taquilla de la palanca");
            }
            PuzzleData.Verbose = prev;

            if (bad > 0) r.Err($"Datos: {bad} comprobaciones fallaron en {Seeds} semillas (primera: {firstBad}).");
            else r.Info($"Datos: {Seeds} semillas generan códigos, símbolos y secuencias válidos.");

            // Los tanques de 5 y 3 litros deben poder dejar 4 en el grande.
            var seen = new HashSet<(int, int)> { (0, 0) };
            var q = new Queue<(int a, int b)>();
            q.Enqueue((0, 0));
            bool reach = false;
            while (q.Count > 0 && !reach)
            {
                var (a, b) = q.Dequeue();
                if (a == PuzzleData.TankGoal) { reach = true; break; }
                int pAB = Math.Min(a, PuzzleData.TankB - b), pBA = Math.Min(b, PuzzleData.TankA - a);
                foreach (var n in new[] { (PuzzleData.TankA, b), (a, PuzzleData.TankB), (a - pAB, b + pAB), (a + pBA, b - pBA), (0, b), (a, 0) })
                    if (seen.Add(n)) q.Enqueue(n);
            }
            if (reach) r.Info($"Tanques: {PuzzleData.TankA} L y {PuzzleData.TankB} L pueden dejar {PuzzleData.TankGoal} L en el grande.");
            else r.Err("Tanques: con esas capacidades no se puede medir la cantidad pedida.");
        }

        // ------------------------------------------------------------------ 2. grafo de dependencias

        class Step
        {
            public string id, who; public string[] needs, gives;
            public Step(string id, string who, string[] needs, string[] gives) { this.id = id; this.who = who; this.needs = needs; this.gives = gives; }
        }

        /// <summary>
        /// Comprueba que existe un orden en el que TODOS los puzzles se pueden resolver, con la información y los objetos que
        /// de verdad cruzan entre salas. Si un puzzle esperase a otro que espera a este, saldría aquí como bloqueo.
        /// </summary>
        static void CheckDependencyGraph(ValidationReport r)
        {
            // 2v2: la información cruza hablando; los objetos, por el tubo. (Mismos requisitos que el código de cada puzzle.)
            var team = new List<Step>
            {
                new Step("Q1", "Cirujano", new string[0], new[] { "llaveCajón" }),
                new Step("Q2", "Cirujano", new string[0], new[] { "presión" }),
                new Step("Q4", "Cirujano", new[] { "diapositivas" }, new[] { "símbolosA" }),
                new Step("M1", "Operador", new string[0], new[] { "leyendaM" }),
                new Step("M3", "Operador", new[] { "llaveTaquilla" }, new[] { "palanca" }),
                new Step("M4", "Operador", new string[0], new[] { "orden-cajones" }),
                new Step("Q3", "Cirujano", new[] { "llaveCajón", "orden-cajones" }, new[] { "fusibles" }),
                new Step("M2", "Operador", new[] { "presión" }, new[] { "símbolosB" }),
                new Step("M5", "Operador", new[] { "fusibles" }, new[] { "máquina-en-espera" }),
                new Step("Q5", "Cirujano", new[] { "símbolosA", "leyendaM", "símbolosB" }, new[] { "puerta-desbloqueada" }),
                new Step("Q6", "Cirujano", new[] { "puerta-desbloqueada", "palanca" }, new[] { "palanca-montada" }),
                new Step("M6", "Operador", new[] { "máquina-en-espera" }, new[] { "interruptor-listo" }),
                new Step("FINAL", "Los dos", new[] { "palanca-montada", "interruptor-listo" }, new[] { "ESCAPE" }),
            };
            var start = new[] { "diapositivas", "llaveTaquilla" };   // objetos que están a la vista al empezar
            Solve(r, "2v2", team, start, "ESCAPE");

            // Individual: las notas dan la información y las palancas/fusibles salen de los cajones/taquillas;
            // la palanca final exige tener resueltos los demás puzzles de la sala (Q1-Q5 o M1-M5).
            var soloQ = new List<Step>
            {
                new Step("Q1", "Cirujano", new string[0], new[] { "Q1", "llaveCajón" }),
                new Step("Q2", "Cirujano", new string[0], new[] { "Q2" }),
                new Step("Q3", "Cirujano", new[] { "llaveCajón", "nota-cajones" }, new[] { "Q3", "palanca" }),
                new Step("Q4", "Cirujano", new[] { "diapositivas" }, new[] { "Q4", "símbolosA" }),
                new Step("Q5", "Cirujano", new[] { "símbolosA", "nota-leyendaM", "nota-símbolosB" }, new[] { "Q5" }),
                new Step("Q6", "Cirujano", new[] { "Q1", "Q2", "Q3", "Q4", "Q5", "palanca" }, new[] { "ESCAPE" }),
            };
            Solve(r, "individual (Quirófano)", soloQ, new[] { "diapositivas", "nota-cajones", "nota-leyendaM", "nota-símbolosB" }, "ESCAPE");

            var soloM = new List<Step>
            {
                new Step("M1", "Operador", new string[0], new[] { "M1" }),
                new Step("M2", "Operador", new[] { "nota-presión" }, new[] { "M2" }),
                new Step("M3", "Operador", new[] { "llaveTaquilla" }, new[] { "M3", "fusibles" }),
                new Step("M4", "Operador", new string[0], new[] { "M4" }),
                new Step("M5", "Operador", new[] { "fusibles" }, new[] { "M5" }),
                new Step("M6", "Operador", new[] { "M1", "M2", "M3", "M4", "M5" }, new[] { "ESCAPE" }),
            };
            Solve(r, "individual (Cuarto de máquinas)", soloM, new[] { "llaveTaquilla", "nota-presión" }, "ESCAPE");
        }

        static void Solve(ValidationReport r, string mode, List<Step> steps, string[] start, string goal)
        {
            var have = new HashSet<string>(start);
            var done = new List<string>();
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (var s in steps)
                {
                    if (done.Contains(s.id) || !s.needs.All(have.Contains)) continue;
                    done.Add(s.id);
                    foreach (var g in s.gives) have.Add(g);
                    progress = true;
                }
            }
            var stuck = steps.Where(s => !done.Contains(s.id)).Select(s => s.id).ToList();
            if (stuck.Count == 0 && have.Contains(goal)) r.Info($"Orden de resolución ({mode}): {string.Join(" → ", done)}");
            else r.Err($"Dependencias ({mode}): estos puzzles quedan bloqueados: {string.Join(", ", stuck)}");
        }

        // ------------------------------------------------------------------ 3. proyecto

        static void WithScene(string path, Action<Scene> action)
        {
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = false;
            if (!scene.isLoaded) { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); opened = true; }
            try { action(scene); }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        static void CheckProject(ValidationReport r)
        {
            // Build Settings
            var enabled = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToList();
            foreach (var p in new[] { MenuScenePath, SelectScenePath, GameScenePath })
            {
                if (!File.Exists(p)) r.Err($"Falta la escena {p}. Ejecuta Escape Room > 0.");
                else if (!enabled.Contains(p)) r.Err($"{p} no está en Build Settings (Escape Room > 4 lo arregla).");
            }
            if (enabled.Count > 0 && enabled[0] != MenuScenePath) r.Err("La primera escena de Build Settings debe ser Menu.");

            // Prefabs de red
            foreach (var (path, comp) in new[] { (NetDir + "/PlayerSession.prefab", "PlayerSession"), (NetDir + "/PlayerAvatar.prefab", "PlayerAvatar"), (NetDir + "/TeamState.prefab", "TeamState") })
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) { r.Err($"Falta el prefab de red {path}. Ejecuta Escape Room > 4 y > 8."); continue; }
                if (go.GetComponent<NetworkObject>() == null) r.Err($"{path} no tiene NetworkObject.");
                var type = typeof(TeamState).Assembly.GetTypes().FirstOrDefault(t => t.Name == comp);
                if (type == null || go.GetComponent(type) == null) r.Err($"{path} no tiene el componente {comp}.");
            }

            // FusionLauncher de la escena Menu
            if (File.Exists(MenuScenePath))
            {
                WithScene(MenuScenePath, scene =>
                {
                    FusionLauncher launcher = null;
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        launcher = root.GetComponentInChildren<FusionLauncher>(true);
                        if (launcher != null) break;
                    }
                    if (launcher == null) { r.Err("La escena Menu no tiene FusionLauncher."); return; }
                    var so = new SerializedObject(launcher);
                    foreach (var f in new[] { "sessionPrefab", "avatarPrefab", "teamPrefab" })
                    {
                        var p = so.FindProperty(f);
                        if (p == null || p.objectReferenceValue == null)
                            r.Err($"FusionLauncher.{f} está vacío en Menu.unity: sin él {(f == "teamPrefab" ? "el tubo y la palanca final no conectan a los compañeros" : "no se puede jugar")}. Ejecuta Escape Room > 8.");
                    }
                    if (r.Errors.All(e => !e.Contains("FusionLauncher."))) r.Info("FusionLauncher: prefabs de sesión, avatar y equipo asignados.");
                });
            }

            // Photon App Id (nunca se imprime)
            try
            {
                var id = Fusion.Photon.Realtime.PhotonAppSettings.Global.AppSettings.AppIdFusion;
                if (string.IsNullOrWhiteSpace(id)) r.Err("No hay App Id de Photon Fusion (Tools > Fusion > Realtime Settings): nadie podrá conectarse.");
                else r.Info("App Id de Photon Fusion configurado.");
            }
            catch (Exception e) { r.Warn("No se pudo leer el App Id de Photon: " + e.Message); }

            // Reparto de jugadores
            foreach (var mode in new[] { MatchMode.TwoVsTwo, MatchMode.OneVsOne })
            {
                int n = MatchLayout.PlayerCount(mode);
                var a = Enumerable.Range(0, n).Select(i => MatchLayout.Assign(mode, i, MapType.Quirofano)).ToArray();
                if (a.Select(x => x.slotIndex).Distinct().Count() != n) r.Err($"{mode}: dos jugadores comparten arena.");
                if (mode == MatchMode.TwoVsTwo)
                {
                    for (int t = 0; t < 2; t++)
                    {
                        var maps = a.Where(x => x.team == t).Select(x => x.map).ToList();
                        if (maps.Count != 2 || !maps.Contains(MapType.Quirofano) || !maps.Contains(MapType.CuartoMaquinas))
                            r.Err($"2v2: el equipo {t} no tiene un Cirujano y un Operador (una sala de cada).");
                    }
                }
                else if (a.Select(x => x.map).Distinct().Count() != 1) r.Err("1v1: los dos jugadores deberían jugar el mismo tipo de sala.");
            }
            r.Info("Reparto: 2v2 = un Cirujano y un Operador por equipo; 1v1 = misma sala en arenas separadas.");
        }

        // ------------------------------------------------------------------ 4 y 5. escena Game

        static void CheckGameScene(ValidationReport r)
        {
            if (!File.Exists(GameScenePath)) return;
            WithScene(GameScenePath, scene =>
            {
                var slots = UnityEngine.Object.FindObjectsByType<ArenaSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(s => s.gameObject.scene == scene).OrderBy(s => s.slotIndex).ToList();
                if (slots.Count != 4) { r.Err($"La escena Game tiene {slots.Count} arenas; hacen falta 4 (0 Quirófano, 1 Máquinas, 2 Quirófano, 3 Máquinas)."); return; }
                for (int i = 0; i < 4; i++)
                {
                    var s = slots[i];
                    var expected = i % 2 == 0 ? MapType.Quirofano : MapType.CuartoMaquinas;
                    if (s.slotIndex != i) r.Err($"Las arenas no usan los índices 0-3 (hay {s.slotIndex} donde tocaba {i}).");
                    else if (s.map != expected) r.Err($"La arena {i} debería ser {expected} y es {s.map}.");
                }
                if (UnityEngine.Object.FindObjectsByType<ArenaRegistry>(FindObjectsInactive.Include, FindObjectsSortMode.None).All(x => x.gameObject.scene != scene))
                    r.Err("La escena Game no tiene ArenaRegistry.");

                Physics.SyncTransforms();
                foreach (var s in slots)
                {
                    if (s.map == MapType.Quirofano) CheckArena(r, s, true);
                    else CheckArena(r, s, false);
                }
            });
        }

        // Ayudas de búsqueda
        class Arena
        {
            public ArenaSlot slot; public Transform root; public ValidationReport r; public string tag;
            public Dictionary<string, List<Transform>> byName;
            public Transform Find(string n) => byName.TryGetValue(n, out var l) ? l[0] : null;
            public IEnumerable<Transform> StartsWith(string prefix) => byName.Where(k => k.Key.StartsWith(prefix)).SelectMany(k => k.Value);
        }

        static void CheckArena(ValidationReport r, ArenaSlot slot, bool quirofano)
        {
            var a = new Arena { slot = slot, root = slot.transform, r = r, tag = $"Arena {slot.slotIndex} ({(quirofano ? "Quirófano" : "Máquinas")})", byName = new Dictionary<string, List<Transform>>() };
            foreach (var t in slot.GetComponentsInChildren<Transform>(true))
            {
                if (!a.byName.TryGetValue(t.name, out var l)) a.byName[t.name] = l = new List<Transform>();
                l.Add(t);
            }

            var targets = new List<(string name, Transform t)>();   // lo que el jugador tiene que poder apuntar
            int before = r.Errors.Count;

            Transform Need(string n, bool target = true, bool mustBeActive = false)
            {
                var t = a.Find(n);
                if (t == null) { r.Err($"{a.tag}: falta el objeto '{n}'. Ejecuta Escape Room > 8."); return null; }
                if (mustBeActive && !t.gameObject.activeSelf) r.Err($"{a.tag}: '{n}' debería estar activo al empezar.");
                if (target && t.gameObject.activeInHierarchy) targets.Add((n, t));
                return t;
            }
            T NeedComp<T>(string n, bool target = true, bool mustBeActive = false) where T : Component
            {
                var t = Need(n, target, mustBeActive);
                if (t == null) return null;
                var c = t.GetComponent<T>();
                if (c == null) r.Err($"{a.tag}: '{n}' no tiene el componente {typeof(T).Name}.");
                return c;
            }
            void Controller<T>(string n) where T : Component => NeedComp<T>(n, false);

            if (slot.spawnPoint == null) r.Err($"{a.tag}: el ArenaSlot no tiene punto de aparición.");
            if (a.root.GetComponent<ArenaAmbience>() == null) r.Warn($"{a.tag}: sin ArenaAmbience (no habrá sonido ambiente).");

            if (quirofano)
            {
                // Q1: teclado del armario, armario con puertas, expediente, póster, llave oculta
                var kp1 = NeedComp<KeypadTerminal>("Prop_Keypad_Q1");
                if (kp1 != null && kp1.kind != KeypadTerminal.Kind.Cabinet) r.Err($"{a.tag}: Prop_Keypad_Q1 debe ser del tipo Cabinet.");
                var cab = Need("MedRack_V2 (1)", false);
                if (cab != null && cab.GetComponentsInChildren<Transform>(true).Count(x => x.name.StartsWith("MedRackDoor")) < 2) r.Err($"{a.tag}: el armario del Q1 no tiene sus dos puertas (MedRackDoor_L/R).");
                var pk = NeedComp<ItemPickup>("Pickup_KeyCabinet", false);
                if (pk != null && (pk.item != ItemId.KeyCabinet || pk.gameObject.activeSelf)) r.Err($"{a.tag}: Pickup_KeyCabinet debe ser la llave de cajón y empezar oculto.");
                var tab = NeedComp<ClueNote>("Prop_Tablet");
                if (tab != null && tab.clue != ClueNote.Clue.Expediente) r.Err($"{a.tag}: Prop_Tablet debe mostrar el expediente.");
                var poster = NeedComp<ClueNote>("PosterHuman");
                if (poster != null && poster.clue != ClueNote.Clue.Poster) r.Err($"{a.tag}: PosterHuman debe mostrar el póster con los números y la leyenda.");

                // Q2: calentador, grifo y nota térmica
                Need("Heater");
                if (!a.StartsWith("Tap").Any()) r.Err($"{a.tag}: no hay grifo (Tap) para enfriar el calentador.");
                else foreach (var tap in a.StartsWith("Tap")) targets.Add((tap.name, tap));
                var note = NeedComp<TextMesh>("ThermalNote", false);

                // Q3: 11 cajones del archivador "SheetRack" y los fusibles ocultos
                var drawers = a.StartsWith("SheetRackCase_").Where(t => t.parent != null && t.parent.name == "SheetRack").ToList();
                foreach (int n in PuzzleData.DrawerNumbers)
                {
                    var d = drawers.FirstOrDefault(t => t.name == "SheetRackCase_" + n);
                    if (d == null) r.Err($"{a.tag}: falta el cajón {n} en el archivador SheetRack, pero la secuencia puede pedirlo.");
                    else targets.Add(("Cajón " + n, d));
                }
                var fuses = NeedComp<ItemPickup>("Pickup_Fuses", false);
                if (fuses != null && (fuses.item != ItemId.Fuse || fuses.count != 3 || fuses.gameObject.activeSelf)) r.Err($"{a.tag}: Pickup_Fuses debe ser 3 fusibles y empezar oculto.");

                // Q4: proyector, 3 diapositivas visibles, haz de luz
                var proj = Need("Prop_Projector");
                if (proj != null && proj.GetComponentInChildren<Light>(true) == null) r.Warn($"{a.tag}: el proyector no tiene luz (ProjectorBeam).");
                int slides = 0;
                for (int i = 0; i < 3; i++)
                {
                    var s = NeedComp<ItemPickup>("Pickup_Slide_" + i, true, true);
                    if (s != null && s.item == ItemId.Slide && s.count == 1) slides++;
                    else if (s != null) r.Err($"{a.tag}: Pickup_Slide_{i} debe ser UNA diapositiva.");
                }

                // Q5 / Q6
                var kp5 = NeedComp<KeypadTerminal>("Prop_Keypad_Q5");
                if (kp5 != null && kp5.kind != KeypadTerminal.Kind.Door) r.Err($"{a.tag}: Prop_Keypad_Q5 debe ser del tipo Door.");
                var lever = NeedComp<SyncLever>("Prop_Lever_Q6");
                if (lever != null)
                {
                    if (lever.role != Role.Cirujano) r.Err($"{a.tag}: la palanca Q6 debe ser del rol Cirujano.");
                    if (lever.transform.Find("Lever_Crowbar") == null && lever.GetComponentsInChildren<Transform>(true).All(x => x.name != "Lever_Crowbar")) r.Err($"{a.tag}: la palanca Q6 no tiene su Lever_Crowbar (donde se monta la palanca).");
                }
                Need("DoorD_Left", false); Need("DoorD_Right", false);

                // Tubo, palanca del solitario y notas del solitario
                CheckTube(a, targets);
                var cb = NeedComp<ItemPickup>("Pickup_Crowbar", false);
                if (cb != null && (cb.item != ItemId.Crowbar || cb.gameObject.activeSelf)) r.Err($"{a.tag}: Pickup_Crowbar (para el modo individual) debe ser la palanca y empezar oculta.");
                CheckSoloGroup(a, new[] { "Note_SoloDrawers", "Note_SoloLegendM", "Note_SoloSymbolsB" });

                Controller<HeaterPuzzle>("Q2_Heater"); Controller<DrawerPuzzle>("Q3_Drawers"); Controller<ProjectorPuzzle>("Q4_Projector"); Controller<FlavorController>("Flavor");
            }
            else
            {
                // M1: 8 etiquetas con los 8 símbolos distintos y la pizarra
                var symbols = new List<int>();
                for (int i = 0; i < 8; i++)
                {
                    var l = NeedComp<LegendLabel>("Label_" + i);
                    if (l != null) symbols.Add(l.symbol);
                }
                if (symbols.Count == 8 && symbols.Distinct().Count() != 8) r.Err($"{a.tag}: las etiquetas no llevan los 8 símbolos distintos (M1 no se podría completar).");
                var wb = NeedComp<ClueNote>("Prop_Whiteboard");
                if (wb != null && wb.clue != ClueNote.Clue.Legend) r.Err($"{a.tag}: la pizarra debe mostrar la leyenda.");

                // M2: 3 tuberías con volante, lecturas y pantalla que exige las válvulas
                if (a.StartsWith("Pipe").Count() < 3) r.Err($"{a.tag}: hacen falta 3 tuberías (Pipe) para las válvulas.");
                for (int i = 0; i < 3; i++) { Need("Prop_Valve_" + i); NeedComp<TextMesh>("Readout_Valve_" + i, false); }
                var tv = NeedComp<ClueNote>("Prop_TV");
                if (tv != null && (tv.clue != ClueNote.Clue.TvSymbols || tv.requiresFlag != "valves")) r.Err($"{a.tag}: la pantalla debe mostrar los símbolos y exigir las válvulas ('valves').");

                // M3: llave a la vista, 3 taquillas con puerta, palanca oculta
                var kl = NeedComp<ItemPickup>("Pickup_KeyLocker", true, true);
                if (kl != null && kl.item != ItemId.KeyLocker) r.Err($"{a.tag}: Pickup_KeyLocker debe ser la llave de taquilla.");
                var lockers = a.StartsWith("CaseMetallic (").Where(t => t.GetComponentsInChildren<Transform>(true).Any(x => x.name.StartsWith("Case_Door"))).ToList();
                if (lockers.Count < 3) r.Err($"{a.tag}: hacen falta 3 taquillas con puerta (hay {lockers.Count}); la palanca puede estar en cualquiera.");
                else foreach (var l in lockers.Take(3)) { targets.Add((l.name, l)); }
                var cb = NeedComp<ItemPickup>("Pickup_Crowbar", false);
                if (cb != null && (cb.item != ItemId.Crowbar || cb.gameObject.activeSelf)) r.Err($"{a.tag}: Pickup_Crowbar debe ser la palanca y empezar oculta.");
                var fz = NeedComp<ItemPickup>("Pickup_Fuses", false);
                if (fz != null && (fz.item != ItemId.Fuse || fz.count != 3 || fz.gameObject.activeSelf)) r.Err($"{a.tag}: Pickup_Fuses (modo individual) debe ser 3 fusibles y empezar oculto.");

                // M4: tanques, 6 botones, pantalla, escotilla y placa
                Need("Tank"); Need("Tank_V2");
                for (int i = 0; i < 6; i++) Need("Tank_Btn_" + i);
                NeedComp<TextMesh>("Tank_Display", false);
                Need("Tank_Hatch", false);
                Need("Prop_Plate", false);   // detrás de la escotilla: no se apunta hasta abrirla

                // M5: panel, 3 interruptores, monitores
                Need("Prop_FuseBox");
                for (int i = 0; i < 3; i++) Need("Switch_" + i);
                var mon = NeedComp<ClueNote>("Prop_Monitors");
                if (mon != null && mon.clue != ClueNote.Clue.MachineLog) r.Err($"{a.tag}: los monitores deben mostrar el registro de arranque.");

                // M6
                var lever = NeedComp<SyncLever>("Prop_Lever_M6");
                if (lever != null && lever.role != Role.Operador) r.Err($"{a.tag}: el interruptor general debe ser del rol Operador.");
                var doorFrame = Need("Door_V3 (2)", false);
                if (doorFrame != null && doorFrame.Find("Door") == null) r.Warn($"{a.tag}: la puerta de salida no tiene el hijo 'Door' (no se abrirá al apagar la máquina).");

                CheckTube(a, targets);
                CheckSoloGroup(a, new[] { "Note_SoloPressure" });

                Controller<ValvePuzzle>("M2_Valves"); Controller<LockerPuzzle>("M3_Lockers"); Controller<TankPuzzle>("M4_Tanks");
                Controller<FusePuzzle>("M5_Fuses"); Controller<FlavorController>("Flavor");
            }

            // Cámaras extra heredadas de los FBX (dibujan una vista fija sobre la del jugador; CameraGuard las apaga en juego)
            var extraCams = slot.GetComponentsInChildren<Camera>(true);
            if (extraCams.Length > 0)
                r.Warn($"{a.tag}: contiene {extraCams.Length} cámara(s) heredadas de los modelos ({string.Join(", ", extraCams.Select(c => c.name).Distinct())}). El juego las apaga solo; Escape Room > 8 desactiva su importación.");

            // Si faltan objetos, el alcance no tiene sentido comprobarlo
            if (r.Errors.Count > before) return;
            CheckReach(a, targets, quirofano);
            r.Info($"{a.tag}: objetos y configuración completos; {targets.Count} objetos apuntables comprobados.");
        }

        static void CheckTube(Arena a, List<(string, Transform)> targets)
        {
            var tube = a.Find("Prop_Tube");
            if (tube == null) { a.r.Err($"{a.tag}: falta la boca del tubo neumático (Prop_Tube): sin ella los objetos no pasan de una sala a otra y el 2v2 no se puede terminar."); return; }
            if (tube.GetComponent<TubeMouth>() == null) a.r.Err($"{a.tag}: Prop_Tube no tiene el componente TubeMouth.");
            if (tube.GetComponentsInChildren<Transform>(true).All(t => t.name != "TubeCapsule")) a.r.Warn($"{a.tag}: el tubo no tiene su cápsula (TubeCapsule): se enviará, pero no se verá llegar.");
            targets.Add(("Prop_Tube", tube));
        }

        static void CheckSoloGroup(Arena a, string[] notes)
        {
            var g = a.Find("SoloProps");
            if (g == null || g.GetComponent<SoloGroup>() == null) { a.r.Err($"{a.tag}: falta SoloProps/SoloGroup: el modo individual (1v1 y prueba) no tiene las notas que sustituyen al compañero."); return; }
            var content = g.Find("Content");
            if (content == null) { a.r.Err($"{a.tag}: SoloProps no tiene el hijo 'Content'."); return; }
            foreach (var n in notes)
            {
                var t = content.Find(n);
                if (t == null) a.r.Err($"{a.tag}: falta la nota '{n}' del modo individual.");
                else if (t.GetComponent<ClueNote>() == null) a.r.Err($"{a.tag}: '{n}' no tiene ClueNote.");
                else if (t.GetComponent<Collider>() == null) a.r.Err($"{a.tag}: '{n}' no tiene collider (no se podría leer).");
            }
        }

        // ------------------------------------------------------------------ alcance: ¿se puede apuntar a cada objeto desde un sitio al que se llega andando?

        static Bounds BoundsOf(Transform t, bool collidersOnly)
        {
            bool any = false;
            var b = new Bounds(t.position, Vector3.zero);
            if (collidersOnly)
                foreach (var c in t.GetComponentsInChildren<Collider>(false)) { if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds); }
            else
                foreach (var rr in t.GetComponentsInChildren<Renderer>(false)) { if (rr is TextMesh) continue; if (!any) { b = rr.bounds; any = true; } else b.Encapsulate(rr.bounds); }
            return b;
        }

        static void CheckReach(Arena a, List<(string name, Transform t)> targets, bool quirofano)
        {
            var r = a.r;
            Vector3 spawn = a.slot.SpawnPosition;
            float floorY = spawn.y;
            if (Physics.Raycast(spawn + Vector3.up * 0.5f, Vector3.down, out var gh, 3f, ~0, QueryTriggerInteraction.Ignore)) floorY = gh.point.y;

            // Caja de la sala (sin contar los puzzles añadidos)
            Bounds room = default; bool init = false;
            var puzzlesRoot = a.Find("Puzzles");
            foreach (var rr in a.root.GetComponentsInChildren<Renderer>(false))
            {
                if (rr is TextMesh) continue;
                if (puzzlesRoot != null && rr.transform.IsChildOf(puzzlesRoot)) continue;
                if (!init) { room = rr.bounds; init = true; } else room.Encapsulate(rr.bounds);
            }
            if (!init) { r.Warn($"{a.tag}: no se pudo calcular el tamaño de la sala; se omite el alcance."); return; }
            var roomGrow = room; roomGrow.Expand(1.0f);

            // Rejilla de celdas andables conectadas con el punto de aparición
            const float cell = 0.3f;
            const float radius = 0.32f;
            Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.z / cell));
            Vector3 Pos(Vector2Int k) => new Vector3(k.x * cell, floorY, k.y * cell);
            bool Walkable(Vector2Int k)
            {
                var p = Pos(k);
                if (!roomGrow.Contains(new Vector3(p.x, roomGrow.center.y, p.z))) return false;
                if (!Physics.Raycast(p + Vector3.up * 1.0f, Vector3.down, out var h, 1.5f, ~0, QueryTriggerInteraction.Ignore)) return false;
                if (Mathf.Abs(h.point.y - floorY) > 0.12f) return false;   // escalones y fosos: no son suelo llano
                return !Physics.CheckCapsule(p + Vector3.up * (0.45f), p + Vector3.up * 1.5f, radius, ~0, QueryTriggerInteraction.Ignore);
            }

            var reach = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            var s0 = Key(spawn);
            reach.Add(s0); queue.Enqueue(s0);
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            while (queue.Count > 0 && reach.Count < 6000)
            {
                var k = queue.Dequeue();
                foreach (var d in dirs)
                {
                    var n = k + d;
                    if (reach.Contains(n) || !Walkable(n)) continue;
                    reach.Add(n); queue.Enqueue(n);
                }
            }
            if (reach.Count < 20) { r.Warn($"{a.tag}: solo {reach.Count} celdas andables desde el punto de aparición: ¿está bloqueado? (se omite el alcance)."); return; }

            // Cada objeto
            const float range = 2.6f;
            int failed = 0;
            foreach (var (name, t) in targets)
            {
                var cb = BoundsOf(t, true);
                if (t.GetComponentInChildren<Collider>(false) == null) { r.Err($"{a.tag}: '{name}' no tiene collider: no se puede apuntar."); failed++; continue; }
                if (cb.center.y > floorY + 2.4f || cb.center.y < floorY - 0.3f)
                {
                    r.Warn($"{a.tag}: '{name}' está a {(cb.center.y - floorY):0.0} m del suelo (fuera de lo cómodo de apuntar). Posición {cb.center:0.0}.");
                }
                if (!room.Contains(cb.center)) { r.Err($"{a.tag}: '{name}' está FUERA de la sala (posición {cb.center:0.0})."); failed++; continue; }

                var samples = new List<Vector3> { cb.center };
                foreach (var ax in new[] { Vector3.right, Vector3.up, Vector3.forward })
                {
                    samples.Add(cb.center + Vector3.Scale(ax, cb.extents) * 0.7f);
                    samples.Add(cb.center - Vector3.Scale(ax, cb.extents) * 0.7f);
                }

                bool ok = false;
                foreach (var k in reach.OrderBy(c => (Pos(c) - new Vector3(cb.center.x, floorY, cb.center.z)).sqrMagnitude).Take(80))
                {
                    var eye = Pos(k) + Vector3.up * 1.6f;
                    foreach (var sp in samples)
                    {
                        var dir = sp - eye;
                        float dist = dir.magnitude;
                        if (dist > range || dist < 0.05f) continue;
                        if (!Physics.Raycast(eye, dir / dist, out var hit, dist + 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        var ht = hit.collider.transform;
                        if (ht.IsChildOf(t) || t.IsChildOf(ht)) { ok = true; break; }
                    }
                    if (ok) break;
                }
                if (!ok)
                {
                    r.Warn($"[alcance] {a.tag}: no encuentro ningún sitio al que se llegue andando desde el que se pueda apuntar a '{name}' (posición {cb.center:0.0}). " +
                           "Puede estar tapado por muebles, detrás de una barandilla o en una escalera; compruébalo en el editor y muévelo si hace falta.");
                    failed++;
                }
            }
            if (failed == 0) r.Info($"{a.tag}: los {targets.Count} objetos se pueden apuntar desde sitios alcanzables ({reach.Count} celdas andables).");

            bool Accessible(Vector3 p)
            {
                var flat = new Vector3(p.x, floorY, p.z);
                foreach (var k in reach)
                    if ((Pos(k) - flat).sqrMagnitude <= 2.0f * 2.0f) return true;
                return false;
            }

            // Dónde caen los fusibles/palanca al terminar los cajones (cualquiera puede ser el último) o al abrir una taquilla
            var sources = quirofano
                ? a.StartsWith("SheetRackCase_").Where(t => t.parent != null && t.parent.name == "SheetRack" && PuzzleData.DrawerNumbers.Any(n => t.name == "SheetRackCase_" + n)).ToList()
                : a.StartsWith("CaseMetallic (").Where(t => t.GetComponentsInChildren<Transform>(true).Any(x => x.name.StartsWith("Case_Door"))).Take(3).ToList();
            int noSpot = 0;
            foreach (var src in sources)
            {
                var outward = PuzzleKit.TowardRoom(a.slot, src.position);
                var fl = PuzzleKit.FloorBelow(src.position);
                bool okA = PuzzleKit.TryFindFreeSpot(fl, outward, out var s1);
                bool okB = PuzzleKit.TryFindFreeSpot(fl, outward, out var s2, 1.5f);
                bool near = okA && okB && Accessible(s1);
                if (near) continue;

                // No hay hueco cerca del mueble: el juego soltará los objetos junto al punto de aparición (sigue siendo resoluble).
                bool fbA = PuzzleKit.TryFindFreeSpot(fl, outward, out var f1, 0.8f, a.slot);
                bool fbB = PuzzleKit.TryFindFreeSpot(fl, outward, out var f2, 1.5f, a.slot);
                if (fbA && fbB && Accessible(f1))
                    r.Warn($"{a.tag}: delante de '{src.name}' no hay hueco libre (suelo y={fl.y:0.00}, mueble y={src.position.y:0.00}, hacia {outward}); lo que suelte caerá junto al punto de aparición.");
                else
                {
                    r.Err($"{a.tag}: al resolver '{src.name}' lo que cae al suelo no tiene ningún hueco libre y alcanzable (ni cerca del mueble ni junto al punto de aparición). Suelo y={fl.y:0.00}, mueble y={src.position.y:0.00}, hacia {outward}.");
                    noSpot++;
                }
            }
            if (noSpot == 0 && sources.Count > 0) r.Info($"{a.tag}: lo que sueltan los {sources.Count} cajones/taquillas cae siempre en un hueco libre y alcanzable.");

            // Dónde cae la llave del Q1 al abrir el armario: debe quedar un hueco libre delante
            if (quirofano)
            {
                var cab = a.Find("MedRack_V2 (1)");
                if (cab != null)
                {
                    // Es lo mismo que hace el juego al abrir el armario (KeypadTerminal): la llave debe caer en un hueco libre y alcanzable.
                    Vector3 outward = PuzzleKit.TowardRoom(a.slot, cab.position);
                    if (!PuzzleKit.TryFindFreeSpot(cab.position, outward, out var drop, 0.8f, a.slot))
                        r.Err($"{a.tag}: no hay ningún hueco libre delante del armario del Q1 (hacia {outward}): la llave no se podría coger y Q3 sería imposible.");
                    else if (!Accessible(drop))
                        r.Err($"{a.tag}: la llave del Q1 caería en {drop:0.0}, a más de 2 m de cualquier sitio al que se llegue andando.");
                    else r.Info($"{a.tag}: la llave del armario cae en un hueco libre y alcanzable ({drop:0.0}, hacia {outward}).");
                }
            }
        }

        // ------------------------------------------------------------------ audio e interfaz (avisos)

        static void CheckAssets(ValidationReport r)
        {
            var missing = new List<string>();
            var defs = typeof(Sfx).GetField("Defs", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as System.Collections.IDictionary;
            if (defs != null)
            {
                foreach (System.Collections.DictionaryEntry e in defs)
                {
                    var file = e.Value.GetType().GetField("file")?.GetValue(e.Value) as string;
                    if (!string.IsNullOrEmpty(file) && Resources.Load<AudioClip>("Audio/" + file) == null) missing.Add(file);
                }
            }
            foreach (var f in new[] { "fluorescent_hum", "ambient_drone", "gas_leak", "clock_tick", "heater_hum", "heartbeat", "steps_tile", "steps_metal" })
                if (Resources.Load<AudioClip>("Audio/" + f) == null) missing.Add(f);
            missing = missing.Distinct().ToList();
            if (missing.Count > 0) r.Warn("Audio ausente en Assets/Resources/Audio (el juego funciona, sin ese sonido): " + string.Join(", ", missing));
            else r.Info("Audio: todos los sonidos referenciados existen.");

            var ui = new[] { "panel_fill", "panel_crack", "frame_fill", "frame_crack", "tag", "slash", "vignette", "noise", "cracks", "title_scar" }
                .Where(n => Resources.Load<Texture2D>("NightTerror/" + n) == null).ToList();
            if (ui.Count > 0) r.Warn("Faltan imágenes del tema en Assets/Resources/NightTerror: " + string.Join(", ", ui) + " (se usarán cajas planas).");
            if (Resources.Load<Font>("NightTerror/Fonts/SourceSerif4-Regular") == null) r.Warn("Faltan las fuentes del tema (se usará la fuente por defecto).");
        }
    }

    /// <summary>
    /// Impide construir (menú 5 o File > Build) si la última verificación no fue correcta o si algo cambió desde entonces.
    /// Para saltárselo en una prueba rápida: EditorPrefs EscapeRoom.SkipValidation = 1 (no recomendado).
    /// </summary>
    public class PuzzleBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (EditorPrefs.GetInt("EscapeRoom.SkipValidation", 0) == 1) return;
            if (PuzzleValidator.IsFresh(out string why)) return;
            throw new BuildFailedException(
                "El juego no se ha verificado: " + why + " Ejecuta Escape Room > 9 - Verificar que el juego se puede resolver " +
                "(o construye con Escape Room > 5, que lo hace solo) y corrige los errores.");
        }
    }
}
