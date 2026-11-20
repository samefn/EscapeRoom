using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using EscapeRoom.Net;
using NetworkObject = Fusion.NetworkObject;
using NetworkCharacterController = Fusion.NetworkCharacterController;

namespace EscapeRoom.EditorTools
{
    /// <summary>
    /// Menú "Escape Room": construye las arenas, la escena Game y la selección de personajes
    /// a partir de Scene 1, Scene 2 y New Scene. No modifica esos tres archivos originales.
    /// </summary>
    public static class EscapeRoomSetup
    {
        const string ArenaDir = "Assets/Prefabs/Arenas";
        const string CharPrefabDir = "Assets/Prefabs/Characters";
        const string DataDir = "Assets/Data/Characters";
        const string ScenesDir = "Assets/Scenes";
        const string NetPrefabDir = "Assets/Prefabs/Network";
        const string NewScenePath = "Assets/New Scene.unity";   // fuente de los personajes (se va con la limpieza)
        const string MenuScenePath = ScenesDir + "/Menu.unity";
        const string GameScenePath = ScenesDir + "/Game.unity";
        const string SelectScenePath = ScenesDir + "/CharacterSelect.unity";
        const float SlotSpacing = 200f;   // metros entre arenas (evita que se vean y que las luces se mezclen)

        // Posiciones de aparición por defecto. Ajustarlas en el editor moviendo el hijo "SpawnPoint" del prefab.
        static readonly Vector3 SpawnQuirofano = new Vector3(-3.2f, 3.9f, -8.8f);
        static readonly Vector3 SpawnMaquinas = new Vector3(9.8f, -4.8f, -1.0f);

        // Walking(n).fbx y el Animator Controller que ya usa cada personaje en "New Scene".
        static readonly Dictionary<string, string> ControllerOf = new Dictionary<string, string>
        {
            { "Walking(1)", "New Animator Controller" },
            { "Walking(2)", "New Animator Controller 1" },
            { "Walking(3)", "New Animator Controller 2" },
            { "Walking(4)", "New Animator Controller 3" },
        };

        /// <summary>
        /// Nombres de los personajes ordenados de IZQUIERDA a DERECHA tal como se ven desde la cámara de la escena.
        /// El orden en pantalla no sigue el número del FBX (y depende de hacia dónde mire la cámara), por eso se calcula.
        /// </summary>
        static string[] OrderedFbxNames(Scene scene)
        {
            var names = ControllerOf.Keys.OrderBy(n => n).ToList();
            Camera cam = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                cam = root.GetComponentInChildren<Camera>();
                if (cam != null) break;
            }
            if (cam == null) return names.ToArray();

            var x = new Dictionary<string, float>();
            foreach (var root in scene.GetRootGameObjects())
                if (ControllerOf.ContainsKey(root.name))
                    x[root.name] = cam.transform.InverseTransformPoint(root.transform.position).x;

            return names.OrderBy(n => x.TryGetValue(n, out var v) ? v : float.MaxValue).ToArray();
        }

        // ------------------------------------------------------------------ menú

        [MenuItem("Escape Room/0 - Construir todo")]
        public static void BuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildCharacterAssets();
            BuildCharacterSelectScene();
            BuildArenasAndGameScene();
            BuildNetworkAssets();
            AddInteractables();
            PuzzleSetup.BuildPuzzles();
            Debug.Log("[EscapeRoom] Todo construido. Abre Assets/Scenes/Menu.unity y pulsa Play.");
        }

        // ------------------------------------------------------------------ 7. objetos interactuables

        // (patrón del nombre del objeto en la arena, nombre mostrado, verbo). Los puzzles se enganchan después.
        static readonly (string pattern, string name, string verb)[] InteractRules =
        {
            (@"^SheetRackCase_(\d+)$", "Cajón $1", "Abrir"),
            (@"^MedRackDoor_[LR]$", "Puerta del armario", "Abrir"),
            (@"^DoorD_(V1|Left|Right)$", "Puerta", "Abrir"),
            (@"^Door(_V\d)?( \(\d+\))?$", "Puerta", "Abrir"),
            (@"^Tap( \(\d+\))?$", "Grifo", "Abrir"),
            (@"^Heater$", "Calentador", "Usar"),
            (@"^Paper( \(\d+\))?$", "Nota", "Leer"),
            (@"^PosterHuman$", "Póster anatómico", "Examinar"),
            (@"^SurgeryTable$", "Mesa de operaciones", "Examinar"),
            (@"^Pipe( \(\d+\))?$", "Válvula", "Girar"),
            (@"^Case_Door_[LR]$", "Taquilla", "Abrir"),
            (@"^(CaseMetallic|SmallMetalicCase)( \(\d+\))?$", "Caja metálica", "Abrir"),
            (@"^Box_V[12]( \(\d+\))?$", "Caja", "Examinar"),
            (@"^Tank(_V2)?$", "Tanque", "Examinar"),
            (@"^Sink_V1( \(\d+\))?$", "Lavabo", "Examinar"),
            (@"^Mattress( \(\d+\))?$", "Colchón", "Examinar"),
            (@"^BigShelf_Stock( \(\d+\))?$", "Estantería", "Examinar"),
        };

        /// <summary>
        /// Añade el componente Interactable (y un collider si falta) a los objetos de las dos arenas que van a ser
        /// puzzles: cajones, puertas, grifos, válvulas... No toca los que ya lo tienen. Se puede repetir sin problema.
        /// </summary>
        [MenuItem("Escape Room/7 - Añadir objetos interactuables a las arenas")]
        public static void AddInteractables()
        {
            var rules = InteractRules.Select(r => (re: new Regex(r.pattern), r.name, r.verb)).ToList();
            int total = 0;

            foreach (var path in new[] { ArenaDir + "/Arena_Quirofano.prefab", ArenaDir + "/Arena_CuartoMaquinas.prefab" })
            {
                if (!File.Exists(path)) { Debug.LogWarning($"[EscapeRoom] Falta {path}. Ejecuta Escape Room > 0 primero."); continue; }

                var root = PrefabUtility.LoadPrefabContents(path);
                int added = 0, boxes = 0;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    foreach (var rule in rules)
                    {
                        var m = rule.re.Match(t.name);
                        if (!m.Success) continue;

                        if (t.GetComponent<Interactable>() == null)
                        {
                            var it = t.gameObject.AddComponent<Interactable>();
                            it.displayName = m.Result(rule.name);
                            it.verb = rule.verb;
                            it.message = $"{it.displayName}: todavía no tiene función (el puzzle se añadirá más adelante).";
                            added++;
                            if (EnsureCollider(t.gameObject)) boxes++;
                        }
                        break;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                Debug.Log($"[EscapeRoom] {Path.GetFileName(path)}: {added} objetos interactuables nuevos ({boxes} con BoxCollider añadido).");
                total += added;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[EscapeRoom] Interactuables añadidos: {total}");
        }

        /// <summary>Si el objeto no tiene ningún collider (ni él ni sus hijos), le pone un BoxCollider del tamaño de su malla.</summary>
        static bool EnsureCollider(GameObject go)
        {
            if (go.GetComponentInChildren<Collider>(true) != null) return false;

            var local = new Bounds();
            bool init = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = go.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!init) { local = new Bounds(p, Vector3.zero); init = true; }
                    else local.Encapsulate(p);
                }
            }
            if (!init) return false;

            var box = go.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
            return true;
        }

        // ------------------------------------------------------------------ 4. prefabs de red + escena Menu

        [MenuItem("Escape Room/4 - Prefabs de red y escena Menu")]
        public static void BuildNetworkAssets()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureDir(NetPrefabDir);
            EnsureDir(ScenesDir);

            var db = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(DataDir + "/CharacterDatabase.asset");
            if (db == null)
            {
                BuildCharacterAssets();
                db = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(DataDir + "/CharacterDatabase.asset");
            }

            // PlayerSession: un objeto de red por jugador (personaje elegido, listo).
            var sessionGo = new GameObject("PlayerSession");
            sessionGo.AddComponent<NetworkObject>();
            sessionGo.AddComponent<PlayerSession>();
            var sessionPrefab = PrefabUtility.SaveAsPrefabAsset(sessionGo, NetPrefabDir + "/PlayerSession.prefab");
            Object.DestroyImmediate(sessionGo);

            // PlayerAvatar: personaje en la arena (NetworkObject + CharacterController + NetworkCharacterController).
            var avatarGo = new GameObject("PlayerAvatar");
            avatarGo.AddComponent<NetworkObject>();
            var cc = avatarGo.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.3f;
            var ncc = avatarGo.AddComponent<NetworkCharacterController>();
            ncc.maxSpeed = 3.5f;
            var avatar = avatarGo.AddComponent<PlayerAvatar>();
            SetReference(avatar, "database", db);
            var avatarPrefab = PrefabUtility.SaveAsPrefabAsset(avatarGo, NetPrefabDir + "/PlayerAvatar.prefab");
            Object.DestroyImmediate(avatarGo);

            // Escena Menu: cámara + lanzador de Fusion + menú.
            var menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();

            // FusionLauncher (+ panel de sala) pasa a DontDestroyOnLoad. El menú va en otro objeto
            // porque, al volver a la escena Menu, la copia del launcher se descarta pero el menú debe seguir.
            var launcherGo = new GameObject("FusionLauncher");
            var launcher = launcherGo.AddComponent<FusionLauncher>();
            launcherGo.AddComponent<LobbyOverlay>();
            SetReference(launcher, "sessionPrefab", sessionPrefab.GetComponent<NetworkObject>());
            SetReference(launcher, "avatarPrefab", avatarPrefab.GetComponent<NetworkObject>());

            var menuGo = new GameObject("MenuUI");
            menuGo.AddComponent<MenuUI>();

            EditorSceneManager.SaveScene(menu, MenuScenePath);

            // Build Settings: Menu primero, luego CharacterSelect y Game.
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != MenuScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(MenuScenePath, true));
            foreach (var p in new[] { SelectScenePath, GameScenePath })
                if (File.Exists(p) && !scenes.Any(s => s.path == p)) scenes.Add(new EditorBuildSettingsScene(p, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            // Fusion registra los prefabs de red en su Network Prefab Table.
            EditorApplication.ExecuteMenuItem("Tools/Fusion/Rebuild Prefab Table");
            AssetDatabase.SaveAssets();
            Debug.Log("[EscapeRoom] Prefabs de red y escena Menu creados. Abre Assets/Scenes/Menu.unity y pulsa Play.");
        }

        // ------------------------------------------------------------------ 6. limpieza del proyecto

        const string RemovedRoot = "_RemovedAssets";   // junto a Assets/, fuera de Unity: nada se borra, solo se mueve

        // Todo lo que cuelga de estas rutas se conserva aunque ninguna escena lo referencie directamente.
        static readonly string[] KeepPrefixes =
        {
            "Assets/Scripts/", "Assets/Editor/", "Assets/Photon/", "Assets/Settings/", "Assets/Data/",
            "Assets/Prefabs/Network/", "Assets/Prefabs/Arenas/", "Assets/Prefabs/Characters/",
            "Assets/Scenes/Menu", "Assets/Scenes/CharacterSelect", "Assets/Scenes/Game",   // escenas y sus datos de iluminación
            "Assets/InputSystem_Actions", "Assets/Read_me.txt",                              // Input System y créditos del pack
            "Assets/Resources/", "Assets/Models/Puzzles/", "Assets/Materials/Puzzles/", "Assets/Textures/Symbols/",   // puzzles: audio (se carga por nombre), modelos y materiales
        };

        [MenuItem("Escape Room/6 - Limpiar proyecto (mover lo que no se usa)")]
        public static void CleanProject() => RunClean(!Application.isBatchMode);

        /// <summary>Construye todo y limpia. Para ejecutar en batchmode: -executeMethod ...BuildAllAndClean -cleanconfirm</summary>
        public static void BuildAllAndClean()
        {
            BuildAll();
            RunClean(false);
        }

        static void RunClean(bool interactive)
        {
            var keepScenes = new[] { MenuScenePath, SelectScenePath, GameScenePath };
            var missing = keepScenes.Where(p => !File.Exists(p)).ToList();
            if (missing.Count > 0)
            {
                Debug.LogError("[EscapeRoom] No limpio: faltan " + string.Join(", ", missing) + ". Ejecuta Escape Room > 0 primero.");
                return;
            }

            // Todo lo que alcanzan las 3 escenas de juego (prefabs, materiales, texturas, modelos, controllers...).
            var used = new HashSet<string>(AssetDatabase.GetDependencies(keepScenes, true));

            var candidates = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/") && !AssetDatabase.IsValidFolder(p) && File.Exists(p))
                .Where(p => !used.Contains(p) && !KeepPrefixes.Any(k => p.StartsWith(k)))
                .ToList();

            long bytes = candidates.Sum(p => new FileInfo(p).Length);
            string summary = $"{candidates.Count} archivos, {bytes / (1024 * 1024)} MB";

            // Resumen por carpeta de primer nivel
            var byFolder = candidates
                .GroupBy(p => p.Split('/').Length > 2 ? p.Split('/')[1] : "(raíz de Assets)")
                .Select(g => $"  {g.Key}: {g.Count()} archivos");
            Debug.Log("[EscapeRoom] Sin uso (" + summary + "):\n" + string.Join("\n", byFolder));

            if (candidates.Count == 0) { Debug.Log("[EscapeRoom] No hay nada que limpiar."); return; }

            if (interactive)
            {
                if (!EditorUtility.DisplayDialog("Limpiar proyecto",
                        $"Se van a mover {summary} que no usa ninguna escena de juego.\n\n" +
                        $"No se borran: van a la carpeta '{RemovedRoot}/' junto a Assets/, y puedes copiarlos de vuelta.\n" +
                        "Incluye Scene 1, Scene 2, New Scene y SampleScene.\n\n" +
                        "Después de limpiar, 'Escape Room > 0' ya no podrá reconstruir las arenas (las escenas fuente se van).",
                        "Mover", "Cancelar"))
                    return;
            }
            else if (!System.Environment.GetCommandLineArgs().Contains("-cleanconfirm"))
            {
                Debug.LogWarning("[EscapeRoom] Limpieza omitida: en batchmode hace falta el argumento -cleanconfirm.");
                return;
            }

            string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string dest = Path.Combine(RemovedRoot, stamp);

            int moved = 0;
            foreach (var p in candidates)
            {
                MoveToBackup(p, dest);
                MoveToBackup(p + ".meta", dest);
                moved++;
            }

            File.WriteAllText(Path.Combine(dest, "LEEME.txt"),
                $"Movido por 'Escape Room > 6' el {System.DateTime.Now}.\n{summary}\n\n" +
                "Para recuperar algo, copia el archivo (y su .meta) a la misma ruta dentro de Assets/ con Unity cerrado.\n");

            RemoveEmptyFolders("Assets");

            // Build Settings: solo las 3 escenas de juego
            EditorBuildSettings.scenes = keepScenes.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();

            AssetDatabase.Refresh();
            Debug.Log($"[EscapeRoom] Limpieza hecha: {moved} archivos movidos a {dest}");
        }

        static void MoveToBackup(string assetPath, string destRoot)
        {
            if (!File.Exists(assetPath)) return;
            string target = Path.Combine(destRoot, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Move(assetPath, target);
        }

        /// <summary>Quita carpetas vacías (y su .meta) para no dejar esqueletos.</summary>
        static void RemoveEmptyFolders(string folder)
        {
            foreach (var sub in Directory.GetDirectories(folder))
                RemoveEmptyFolders(sub);

            if (folder == "Assets" || Directory.GetFileSystemEntries(folder).Length > 0) return;
            Directory.Delete(folder);
            string meta = folder + ".meta";
            if (File.Exists(meta)) File.Delete(meta);
        }

        // ------------------------------------------------------------------ 5. build de prueba para Windows

        [MenuItem("Escape Room/5 - Build de prueba (Windows)")]
        public static void BuildWindowsPlayer()
        {
            var scenes = new[] { MenuScenePath, SelectScenePath, GameScenePath };
            var missing = scenes.Where(p => !File.Exists(p)).ToList();
            if (missing.Count > 0)
            {
                Debug.LogError("[EscapeRoom] Faltan escenas: " + string.Join(", ", missing) + ". Ejecuta Escape Room > 0 - Construir todo.");
                return;
            }

            // Antes de construir se comprueba que el juego se puede terminar (también en 2v2) y que puede conectar.
            var validation = PuzzleValidator.Run();
            Debug.Log(validation.ToText());
            if (!validation.Ok)
            {
                Debug.LogError($"[EscapeRoom] Build cancelada: la verificación encontró {validation.Errors.Count} errores (Logs/ValidacionPuzzles.txt).");
                if (!Application.isBatchMode)
                    EditorUtility.DisplayDialog("Build cancelada",
                        "El juego no está listo para compartir:\n\n" + string.Join("\n", validation.Errors.Take(8).Select(e => "• " + e)) +
                        "\n\nCorrige esto (o ejecuta Escape Room > 8) y vuelve a construir.", "Vale");
                return;
            }
            if (validation.Warnings.Count > 0 && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog("Hay avisos",
                    $"La verificación no encontró errores, pero sí {validation.Warnings.Count} avisos (objetos difíciles de apuntar, audio ausente...). " +
                    "Están en la consola y en Logs/ValidacionPuzzles.txt.\n\n¿Construir igualmente?", "Construir", "Cancelar"))
                return;

            // Con dos builds en el mismo PC, la que no tiene el foco debe seguir corriendo (si no, Photon la desconecta).
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;

            string dir = "Builds/EscapeRoom";
            Directory.CreateDirectory(dir);
            string exe = dir + "/EscapeRoom.exe";

            var report = UnityEditor.BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = exe,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });

            var result = report.summary.result;
            if (result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.Log($"[EscapeRoom] Build lista: {exe} ({report.summary.totalSize / (1024 * 1024)} MB). " +
                          "Copia la carpeta Builds/EscapeRoom entera a otro PC para probar online.");
                if (!Application.isBatchMode) EditorUtility.RevealInFinder(exe);
            }
            else
            {
                Debug.LogError($"[EscapeRoom] La build falló: {result}");
            }
        }

        static void SetReference(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogError($"[EscapeRoom] {target.GetType().Name} no tiene el campo '{field}'"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ 1. arenas + escena Game

        [MenuItem("Escape Room/1 - Arenas y escena Game")]
        public static void BuildArenasAndGameScene()
        {
            // Tras "Escape Room > 6" las escenas fuente ya no están: si el resultado existe, no hay nada que reconstruir.
            if (!File.Exists("Assets/Scene 1.unity") || !File.Exists("Assets/Scene 2.unity"))
            {
                if (File.Exists(GameScenePath))
                    Debug.Log("[EscapeRoom] Arenas: Scene 1/Scene 2 ya no están (limpieza hecha). Se conserva Game.unity tal como está.");
                else
                    Debug.LogError("[EscapeRoom] Faltan Scene 1/Scene 2 y no existe Game.unity. Recupera las escenas de _RemovedAssets/.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureDir(ArenaDir);
            EnsureDir(ScenesDir);

            // La escena Game es la escena activa mientras se abren las originales en modo aditivo.
            var game = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var quirofano = BuildArenaPrefab("Assets/Scene 1.unity", "Arena_Quirofano", MapType.Quirofano,
                SpawnQuirofano, 180f, captureEnvironment: true, out var env, out var volumePrefab);
            var maquinas = BuildArenaPrefab("Assets/Scene 2.unity", "Arena_CuartoMaquinas", MapType.CuartoMaquinas,
                SpawnMaquinas, 0f, captureEnvironment: false, out _, out _);

            if (quirofano == null || maquinas == null)
            {
                Debug.LogError("[EscapeRoom] No se pudieron crear los prefabs de arena. Revisa los mensajes anteriores.");
                return;
            }

            SceneManager.SetActiveScene(game);

            // 4 arenas: 0 Quirófano, 1 Cuarto de máquinas, 2 Quirófano, 3 Cuarto de máquinas (ver MatchLayout).
            var prefabs = new[] { quirofano, maquinas, quirofano, maquinas };
            var slots = new List<ArenaSlot>();
            for (int i = 0; i < prefabs.Length; i++)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[i], game);
                inst.name = $"Arena_{i}_{prefabs[i].GetComponent<ArenaSlot>().map}";
                inst.transform.position = new Vector3(i * SlotSpacing, 0f, 0f);
                var slot = inst.GetComponent<ArenaSlot>();
                slot.slotIndex = i;
                slots.Add(slot);
            }

            var registryGo = new GameObject("ArenaRegistry");
            registryGo.AddComponent<ArenaRegistry>().slots = slots.ToArray();

            if (volumePrefab != null) PrefabUtility.InstantiatePrefab(volumePrefab, game);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<FollowCamera>();
            camGo.transform.position = new Vector3(0f, 6f, -14f);

            if (env != null) ApplyEnvironment(env);

            EditorSceneManager.MarkSceneDirty(game);
            EditorSceneManager.SaveScene(game, GameScenePath);
            AddToBuildSettings(GameScenePath);
            Debug.Log("[EscapeRoom] Escena Game creada con 4 arenas. Pasos manuales: Window > Rendering > Lighting > Generate Lighting, " +
                      "y ajustar el SpawnPoint de cada arena.");
        }

        static GameObject BuildArenaPrefab(string scenePath, string arenaName, MapType map, Vector3 spawn, float spawnYaw,
            bool captureEnvironment, out SceneEnvironment env, out GameObject volumePrefab)
        {
            env = null;
            volumePrefab = null;

            if (!File.Exists(scenePath))
            {
                Debug.LogError($"[EscapeRoom] No existe {scenePath}");
                return null;
            }

            var source = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            var previousActive = SceneManager.GetActiveScene();

            if (captureEnvironment)
            {
                SceneManager.SetActiveScene(source);
                env = SceneEnvironment.Capture();
                SceneManager.SetActiveScene(previousActive);
            }

            var arena = new GameObject(arenaName);
            SceneManager.MoveGameObjectToScene(arena, source);

            foreach (var root in source.GetRootGameObjects())
            {
                if (root == arena) continue;

                // La cámara la aporta cada jugador; el volumen global se instancia una sola vez en Game.
                if (root.GetComponent<Camera>() != null) continue;
                if (root.name == "Global Volume")
                {
                    if (captureEnvironment)
                        volumePrefab = PrefabUtility.SaveAsPrefabAsset(root, $"{ArenaDir}/GlobalVolume.prefab");
                    continue;
                }
                root.transform.SetParent(arena.transform, true);
            }

            var sp = new GameObject("SpawnPoint");
            sp.transform.SetParent(arena.transform, false);
            sp.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0f, spawnYaw, 0f));   // mira hacia el interior de la sala

            var slot = arena.AddComponent<ArenaSlot>();
            slot.map = map;
            slot.spawnPoint = sp.transform;

            var prefab = PrefabUtility.SaveAsPrefabAsset(arena, $"{ArenaDir}/{arenaName}.prefab");
            EditorSceneManager.CloseScene(source, true);   // descarta los cambios: Scene 1/2 quedan intactas
            return prefab;
        }

        class SceneEnvironment
        {
            public Material skybox;
            public AmbientMode mode;
            public Color light, sky, equator, ground;
            public float intensity;

            public static SceneEnvironment Capture() => new SceneEnvironment
            {
                skybox = RenderSettings.skybox,
                mode = RenderSettings.ambientMode,
                light = RenderSettings.ambientLight,
                sky = RenderSettings.ambientSkyColor,
                equator = RenderSettings.ambientEquatorColor,
                ground = RenderSettings.ambientGroundColor,
                intensity = RenderSettings.ambientIntensity
            };
        }

        static void ApplyEnvironment(SceneEnvironment e)
        {
            RenderSettings.skybox = e.skybox;
            RenderSettings.ambientMode = e.mode;
            RenderSettings.ambientLight = e.light;
            RenderSettings.ambientSkyColor = e.sky;
            RenderSettings.ambientEquatorColor = e.equator;
            RenderSettings.ambientGroundColor = e.ground;
            RenderSettings.ambientIntensity = e.intensity;
        }

        // ------------------------------------------------------------------ 2. personajes

        [MenuItem("Escape Room/2 - Personajes (prefabs y base de datos)")]
        public static void BuildCharacterAssets()
        {
            if (!File.Exists(NewScenePath))
            {
                if (AssetDatabase.LoadAssetAtPath<CharacterDatabase>(DataDir + "/CharacterDatabase.asset") != null)
                    Debug.Log("[EscapeRoom] Personajes: New Scene ya no está (limpieza hecha). Se conservan los personajes existentes.");
                else
                    Debug.LogError("[EscapeRoom] Falta New Scene.unity y no hay base de datos de personajes. Recupérala de _RemovedAssets/.");
                return;
            }

            EnsureDir(CharPrefabDir);
            EnsureDir(DataDir);

            // Orden de izquierda a derecha en pantalla, según New Scene.
            var newScene = EditorSceneManager.OpenScene(NewScenePath, OpenSceneMode.Additive);
            string[] order = OrderedFbxNames(newScene);
            EditorSceneManager.CloseScene(newScene, true);

            var defs = new List<CharacterDefinition>();
            for (int i = 0; i < order.Length; i++)
            {
                string fbxPath = $"Assets/{order[i]}.fbx";
                string controllerName = ControllerOf[order[i]];
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>($"Assets/{controllerName}.controller");
                if (model == null || controller == null)
                {
                    Debug.LogWarning($"[EscapeRoom] Falta {fbxPath} o su controller ({controllerName}). Se omite el personaje {i + 1}.");
                    continue;
                }

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                inst.name = $"Character_{i + 1}";

                var anim = inst.GetComponent<Animator>();
                if (anim == null) anim = inst.AddComponent<Animator>();
                if (anim.avatar == null)
                    anim.avatar = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Avatar>().FirstOrDefault();
                anim.runtimeAnimatorController = controller;
                anim.applyRootMotion = false;

                var prefab = PrefabUtility.SaveAsPrefabAsset(inst, $"{CharPrefabDir}/Character_{i + 1}.prefab");
                Object.DestroyImmediate(inst);

                string defPath = $"{DataDir}/Character_{i + 1}.asset";
                var def = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(defPath);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<CharacterDefinition>();
                    def.displayName = $"Personaje {i + 1}";
                    AssetDatabase.CreateAsset(def, defPath);
                }
                def.visualPrefab = prefab;
                def.controller = controller;
                EditorUtility.SetDirty(def);
                defs.Add(def);
            }

            string dbPath = $"{DataDir}/CharacterDatabase.asset";
            var db = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(dbPath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<CharacterDatabase>();
                AssetDatabase.CreateAsset(db, dbPath);
            }
            db.characters = defs.ToArray();
            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            Debug.Log($"[EscapeRoom] {defs.Count} personajes listos en {DataDir}. Cambia el displayName de cada Character_N.asset si quieres.");
        }

        // ------------------------------------------------------------------ 3. escena de selección

        [MenuItem("Escape Room/3 - Escena de selección de personaje")]
        public static void BuildCharacterSelectScene()
        {
            if (!File.Exists(NewScenePath))
            {
                if (File.Exists(SelectScenePath))
                    Debug.Log("[EscapeRoom] Selección: New Scene ya no está (limpieza hecha). Se conserva CharacterSelect.unity tal como está.");
                else
                    Debug.LogError("[EscapeRoom] Falta New Scene.unity y no existe CharacterSelect.unity. Recupera la escena de _RemovedAssets/.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureDir(ScenesDir);

            const string dbPath = DataDir + "/CharacterDatabase.asset";
            var db = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(dbPath);
            if (db == null)
            {
                BuildCharacterAssets();
                db = AssetDatabase.LoadAssetAtPath<CharacterDatabase>(dbPath);
            }

            // "New Scene" ya contiene los 4 personajes Walking(1..4) con su Animator Controller.
            var scene = EditorSceneManager.OpenScene(NewScenePath, OpenSceneMode.Single);

            // previews[i] = modelo del personaje i de la base de datos (izquierda -> derecha en pantalla).
            string[] order = OrderedFbxNames(scene);
            var previews = new Transform[order.Length];
            for (int i = 0; i < order.Length; i++)
            {
                var go = GameObject.Find(order[i]);
                if (go == null) Debug.LogWarning($"[EscapeRoom] No encontré '{order[i]}' en New Scene.");
                previews[i] = go != null ? go.transform : null;
            }

            var ui = new GameObject("CharacterSelect");
            var sel = ui.AddComponent<CharacterSelectUI>();
            sel.database = db;
            sel.previews = previews;

            // Guarda como Scene nueva (New Scene.unity no se toca).
            EditorSceneManager.SaveScene(scene, SelectScenePath, false);
            AddToBuildSettings(SelectScenePath);
            Debug.Log("[EscapeRoom] Escena CharacterSelect creada a partir de New Scene.");
        }

        // ------------------------------------------------------------------ utilidades

        static void AddToBuildSettings(string path)
        {
            var list = EditorBuildSettings.scenes.ToList();
            if (list.Any(s => s.path == path)) return;
            list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void EnsureDir(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            EnsureDir(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(assetPath));
        }
    }
}
