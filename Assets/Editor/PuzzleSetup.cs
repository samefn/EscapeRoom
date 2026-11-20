using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EscapeRoom.Net;
using EscapeRoom.Puzzles;
using NetworkObject = Fusion.NetworkObject;

namespace EscapeRoom.EditorTools
{
    /// <summary>
    /// Menú "Escape Room > 8": configura la importación de los modelos y audios, crea los materiales y coloca en las dos arenas
    /// los objetos y puzzles de las dos salas. Se puede repetir: borra lo que colocó antes ("Puzzles") y lo rehace.
    ///
    /// Las posiciones salen de las arenas reales: se instancia una copia temporal y se lanzan rayos contra las paredes y los muebles.
    /// Si un modelo queda mirando hacia la pared, cambia su giro en YawFix (más abajo) y repite el menú.
    /// Los modelos .glb necesitan el paquete glTFast (ya añadido a Packages/manifest.json); si todavía no están importados,
    /// se usa una pieza con geometría básica y se avisa en la consola.
    /// </summary>
    public static class PuzzleSetup
    {
        const string ArenaDir = "Assets/Prefabs/Arenas";
        const string NetPrefabDir = "Assets/Prefabs/Network";
        const string ModelDir = "Assets/Models/Puzzles";
        const string MatDir = "Assets/Materials/Puzzles";
        const string SymbolDir = "Assets/Textures/Symbols";
        const string AudioDir = "Assets/Resources/Audio";
        const string MenuScenePath = "Assets/Scenes/Menu.unity";

        // Giro extra (grados, sobre el eje vertical) para los modelos que quedaron de espaldas. Ejemplo: { "keypad", 180f }.
        static readonly Dictionary<string, float> YawFix = new Dictionary<string, float>
        {
            { "keypad", 0f }, { "fusebox", 0f }, { "whiteboard", 0f }, { "tv", 0f }, { "projector", 0f },
            { "manometer", 0f }, { "wheel", 0f }, { "monitors", 0f }, { "tablet", 0f }, { "tube", 0f },
        };

        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static readonly Dictionary<string, bool> MissingModels = new Dictionary<string, bool>();
        static Probe _probe;

        // ------------------------------------------------------------------ punto de entrada

        [MenuItem("Escape Room/8 - Importar modelos, audio y colocar puzzles")]
        public static void BuildPuzzles()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            MissingModels.Clear();
            Mats.Clear();

            AssetDatabase.Refresh();
            FixGlbImporters();
            ConfigureAudio();
            ConfigureUiAssets();
            ConfigureModelsAndTextures();
            CreateSymbolTextures();
            CreateMaterials();
            BuildTeamStatePrefab();

            foreach (var kv in new[] { ("Arena_Quirofano", true), ("Arena_CuartoMaquinas", false) })
            {
                string path = $"{ArenaDir}/{kv.Item1}.prefab";
                if (!File.Exists(path)) { Debug.LogWarning($"[Puzzles] Falta {path}. Ejecuta Escape Room > 0 primero."); continue; }
                RemoveOld(path);
                BuildArena(path, kv.Item2);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (MissingModels.Count > 0)
                Debug.LogWarning("[Puzzles] Modelos que no se pudieron cargar (se usó geometría básica): " + string.Join(", ", MissingModels.Keys) +
                                 ". Si son .glb, espera a que Unity instale glTFast (Window > Package Manager) y vuelve a ejecutar Escape Room > 8.");
            Debug.Log("[Puzzles] Puzzles colocados en las arenas. Abre Menu.unity y prueba con Escape Room > 5 o con Play.");
        }

        // ------------------------------------------------------------------ importación

        /// <summary>
        /// Los .glb se copiaron antes de instalar glTFast y Unity los importó como archivos genéricos. Se les quita el .meta para que
        /// se reimporten con el importador de glTFast (nadie los referenciaba todavía, así que el cambio de GUID no rompe nada).
        /// </summary>
        static void FixGlbImporters()
        {
            string dir = ModelDir + "/Glb";
            if (!Directory.Exists(dir)) return;
            bool changed = false;
            foreach (var file in Directory.GetFiles(dir, "*.glb"))
            {
                string path = file.Replace("\\", "/");
                var imp = AssetImporter.GetAtPath(path);
                if (imp != null && imp.GetType().Name != "DefaultImporter") continue;
                if (File.Exists(path + ".meta")) { File.Delete(path + ".meta"); changed = true; }
            }
            if (changed) AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            foreach (var file in Directory.GetFiles(dir, "*.glb"))
            {
                string path = file.Replace("\\", "/");
                var imp = AssetImporter.GetAtPath(path);
                if (imp == null || imp.GetType().Name == "DefaultImporter")
                    Debug.LogWarning("[Puzzles] " + path + " no se importó con glTFast (importador: " + (imp != null ? imp.GetType().Name : "ninguno") + "). Se usará geometría básica.");
            }
        }

        static void ConfigureAudio()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as AudioImporter;
                if (imp == null) continue;
                var s = imp.defaultSampleSettings;
                if (s.loadType == AudioClipLoadType.DecompressOnLoad && s.preloadAudioData) continue;
                s.loadType = AudioClipLoadType.DecompressOnLoad;   // Sfx recorta tramos con AudioSource.time: necesita el clip descomprimido
                s.preloadAudioData = true;
                imp.defaultSampleSettings = s;
                imp.SaveAndReimport();
                n++;
            }
            Debug.Log($"[Puzzles] Audio configurado ({n} archivos reimportados).");
        }

        /// <summary>Imágenes del tema Night Terror: sin compresión, sin mipmaps, sin reescalar (title_scar mide 1100x46) y con el borde correcto.</summary>
        public static void ConfigureUiAssets()
        {
            const string dir = "Assets/Resources/NightTerror";
            if (!Directory.Exists(dir)) return;
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                bool tile = Path.GetFileNameWithoutExtension(path) == "noise";
                bool ok = imp.textureCompression == TextureImporterCompression.Uncompressed && !imp.mipmapEnabled &&
                          imp.npotScale == TextureImporterNPOTScale.None && imp.alphaIsTransparency &&
                          imp.wrapMode == (tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp);
                if (ok) continue;
                imp.textureType = TextureImporterType.Default;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.mipmapEnabled = false;
                imp.npotScale = TextureImporterNPOTScale.None;
                imp.alphaIsTransparency = true;
                imp.wrapMode = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                imp.filterMode = FilterMode.Bilinear;
                imp.maxTextureSize = 2048;
                imp.SaveAndReimport();
                n++;
            }
            if (n > 0) Debug.Log($"[Puzzles] Imágenes de la interfaz configuradas ({n}).");
        }

        /// <summary>
        /// Los FBX del Asylum (Cinema 4D) traen una cámara "CINEMA_4D_Editor". Importada, acaba dentro de cada arena y dibuja
        /// una vista fija sobre la del jugador. Se desactiva la importación de cámaras en esos modelos.
        /// </summary>
        public static void StripImportedCameras()
        {
            int n = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null || !imp.importCameras) continue;
                imp.importCameras = false;
                imp.SaveAndReimport();
                n++;
            }
            if (n > 0) Debug.Log($"[Puzzles] Cámaras importadas desactivadas en {n} modelos.");
        }

        static void ConfigureModelsAndTextures()
        {
            StripImportedCameras();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as ModelImporter;
                if (imp == null) continue;
                bool changed = false;
                if (imp.importAnimation) { imp.importAnimation = false; changed = true; }
                if (imp.materialImportMode != ModelImporterMaterialImportMode.None) { imp.materialImportMode = ModelImporterMaterialImportMode.None; changed = true; }
                if (!imp.isReadable) { imp.isReadable = false; }
                if (changed) imp.SaveAndReimport();
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ModelDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp == null) continue;
                bool normal = path.ToLowerInvariant().Contains("normal") || path.ToLowerInvariant().Contains("-nor");
                bool changed = false;
                var wanted = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (imp.textureType != wanted) { imp.textureType = wanted; changed = true; }
                if (imp.maxTextureSize > 2048) { imp.maxTextureSize = 2048; changed = true; }
                if (changed) imp.SaveAndReimport();
            }
        }

        static void CreateSymbolTextures()
        {
            EnsureDir(SymbolDir);
            for (int i = 0; i < 8; i++)
            {
                string path = $"{SymbolDir}/symbol_{i}.png";
                if (!File.Exists(path)) File.WriteAllBytes(path, SymbolLibrary.Get(i).EncodeToPNG());
            }
            AssetDatabase.Refresh();
            for (int i = 0; i < 8; i++)
            {
                var imp = AssetImporter.GetAtPath($"{SymbolDir}/symbol_{i}.png") as TextureImporter;
                if (imp == null) continue;
                if (!imp.alphaIsTransparency || imp.mipmapEnabled)
                {
                    imp.alphaIsTransparency = true;
                    imp.mipmapEnabled = false;
                    imp.wrapMode = TextureWrapMode.Clamp;
                    imp.SaveAndReimport();
                }
            }
        }

        // ------------------------------------------------------------------ materiales

        static Texture2D Tex(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        static Material Lit(string name, Color color, float metallic = 0.1f, float smooth = 0.35f, string baseMap = null, string normal = null,
            string emissionMap = null, Color? emission = null)
        {
            EnsureDir(MatDir);
            string path = $"{MatDir}/{name}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smooth);
            var bt = baseMap != null ? Tex(baseMap) : null;
            m.SetTexture("_BaseMap", bt);
            var nt = normal != null ? Tex(normal) : null;
            m.SetTexture("_BumpMap", nt);
            if (nt != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP");
            var et = emissionMap != null ? Tex(emissionMap) : null;
            if (et != null || emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", et);
                m.SetColor("_EmissionColor", emission ?? Color.white);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(m);
            Mats[name] = m;
            return m;
        }

        static Material SymbolMat(int i)
        {
            EnsureDir(MatDir);
            string path = $"{MatDir}/Symbol_{i}.mat";
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_BaseMap", Tex($"{SymbolDir}/symbol_{i}.png"));
            m.SetColor("_BaseColor", new Color(0.08f, 0.08f, 0.1f, 1f));
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = 2450;
            EditorUtility.SetDirty(m);
            Mats["Symbol_" + i] = m;
            return m;
        }

        static void CreateMaterials()
        {
            string P = ModelDir;
            Lit("FuseBox", Color.white, 0.4f, 0.4f, $"{P}/FuseBox/fuse_box_Color.png", $"{P}/FuseBox/fuse_box_Normal.png");
            Lit("Bottles", Color.white, 0.1f, 0.6f, $"{P}/Bottles/Bottle_KrononX_BaseColor.png", $"{P}/Bottles/Bottle_KrononX_Normal.png",
                $"{P}/Bottles/Bottle_KrononX_Emissive.png", new Color(0.6f, 0.6f, 0.6f));
            Lit("CeilingLamp", Color.white, 0.2f, 0.5f, $"{P}/CeilingLamp/lamp_BaseColor.png", $"{P}/CeilingLamp/lamp_Normal.png",
                $"{P}/CeilingLamp/lamp_Emissive.png", new Color(1f, 1f, 1f));
            Lit("OldTV", Color.white, 0.1f, 0.4f, $"{P}/OldTV/tv_BaseColor.png");
            Lit("Tablet", Color.white, 0.0f, 0.3f, $"{P}/PaperTablet/tablet_BaseColor.png", $"{P}/PaperTablet/tablet_Normal.png");
            Lit("Manometer", Color.white, 0.5f, 0.5f, $"{P}/Manometer/Manometr.fbm/tex.png");
            Lit("Tube", Color.white, 0.5f, 0.45f, $"{P}/Tube/tubo_Albedo.png", $"{P}/Tube/tubo_Normal.png");
            Lit("Whiteboard", Color.white, 0.1f, 0.4f, $"{P}/Whiteboard/whiteboard_BaseColor.png", $"{P}/Whiteboard/whiteboard_Normal.png");
            Lit("KeypadRust", new Color(0.42f, 0.32f, 0.26f), 0.55f, 0.35f, null, $"{P}/Keypad/rusty-metal-shee-nor.png");
            Lit("ProjectorGray", new Color(0.18f, 0.19f, 0.2f), 0.4f, 0.4f);
            Lit("WheelBlack", new Color(0.06f, 0.06f, 0.07f), 0.1f, 0.45f);
            Lit("MetalDark", new Color(0.16f, 0.17f, 0.19f), 0.7f, 0.4f);
            Lit("MetalLight", new Color(0.62f, 0.64f, 0.68f), 0.8f, 0.5f);
            Lit("Gold", new Color(0.95f, 0.78f, 0.25f), 0.85f, 0.6f, null, null, null, new Color(0.15f, 0.1f, 0.02f));
            Lit("PlateWhite", new Color(0.88f, 0.88f, 0.84f), 0.0f, 0.2f);
            Lit("FuseRed", new Color(0.85f, 0.15f, 0.12f), 0.1f, 0.6f, null, null, null, new Color(0.35f, 0.04f, 0.03f));
            Lit("SlideCyan", new Color(0.55f, 0.85f, 0.95f), 0.0f, 0.7f, null, null, null, new Color(0.05f, 0.18f, 0.22f));
            Lit("Black", new Color(0.04f, 0.04f, 0.045f), 0.0f, 0.2f);
            Lit("ScreenGlow", new Color(0.1f, 0.4f, 0.2f), 0.0f, 0.9f, null, null, null, new Color(0.15f, 0.6f, 0.3f));
            Lit("ButtonRed", PuzzleData.FuseColors[0], 0.1f, 0.5f, null, null, null, PuzzleData.FuseColors[0] * 0.5f);
            Lit("ButtonGreen", PuzzleData.FuseColors[1], 0.1f, 0.5f, null, null, null, PuzzleData.FuseColors[1] * 0.5f);
            Lit("ButtonBlue", PuzzleData.FuseColors[2], 0.1f, 0.5f, null, null, null, PuzzleData.FuseColors[2] * 0.5f);
            Lit("TankButton", new Color(0.25f, 0.5f, 0.75f), 0.2f, 0.5f, null, null, null, new Color(0.04f, 0.1f, 0.18f));
            Lit("Hatch", new Color(0.3f, 0.3f, 0.32f), 0.8f, 0.35f);
            for (int i = 0; i < 8; i++) SymbolMat(i);
            AssetDatabase.SaveAssets();
        }

        static void EnsureDir(string assetPath)
        {
            if (!Directory.Exists(assetPath)) Directory.CreateDirectory(assetPath);
        }

        static Material M(string name) => Mats.TryGetValue(name, out var m) ? m : null;

        // ------------------------------------------------------------------ prefab de red del equipo

        static void BuildTeamStatePrefab()
        {
            EnsureDir(NetPrefabDir);
            var go = new GameObject("TeamState");
            go.AddComponent<NetworkObject>();
            go.AddComponent<TeamState>();
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, NetPrefabDir + "/TeamState.prefab");
            Object.DestroyImmediate(go);

            // Asignarlo al FusionLauncher de la escena Menu.
            if (!File.Exists(MenuScenePath)) { Debug.LogWarning("[Puzzles] No existe Menu.unity: ejecuta Escape Room > 4 y repite el 8."); return; }
            var scene = SceneManager.GetSceneByPath(MenuScenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Additive);
            foreach (var root in scene.GetRootGameObjects())
            {
                var launcher = root.GetComponentInChildren<FusionLauncher>(true);
                if (launcher == null) continue;
                var so = new SerializedObject(launcher);
                var prop = so.FindProperty("teamPrefab");
                if (prop != null)
                {
                    prop.objectReferenceValue = prefab.GetComponent<NetworkObject>();
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }
            EditorSceneManager.SaveScene(scene);
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
            EditorApplication.ExecuteMenuItem("Tools/Fusion/Rebuild Prefab Table");
        }

        // ------------------------------------------------------------------ sonda: copia temporal de la arena para lanzar rayos

        class Probe : System.IDisposable
        {
            readonly Scene _scene;
            public readonly GameObject root;

            public Probe(string prefabPath)
            {
                _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _scene);
                Physics.SyncTransforms();
            }

            public void Dispose() => EditorSceneManager.CloseScene(_scene, true);

            public bool Ray(Vector3 origin, Vector3 dir, float max, out RaycastHit hit) =>
                Physics.Raycast(origin, dir, out hit, max, ~0, QueryTriggerInteraction.Ignore);

            /// <summary>Como Ray, pero ignora muebles, tuberías y tanques: solo la cáscara de la sala (paredes verticales).</summary>
            public bool RayWall(Vector3 origin, Vector3 dir, float max, out RaycastHit hit)
            {
                var hits = Physics.RaycastAll(origin, dir, max, ~0, QueryTriggerInteraction.Ignore);
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    string n = h.collider.name;
                    bool shell = n == "Surgery" || n == "MachineRoom" || n.EndsWith("_facing");
                    if (shell && Mathf.Abs(h.normal.y) < 0.4f) { hit = h; return true; }
                }
                hit = default;
                return false;
            }

            public Transform Find(string name) =>
                root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

            public Transform FindPrefix(string prefix, int index = 0) =>
                root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(prefix)).Skip(index).FirstOrDefault();

            public bool BoundsOf(Transform t, out Bounds b)
            {
                b = default;
                if (t == null) return false;
                bool any = false;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                return any;
            }

            public bool BoundsOf(string name, out Bounds b) => BoundsOf(Find(name), out b);
        }

        static Vector3 Snap(Vector3 v) => PuzzleKit.SnapToAxis(v);

        /// <summary>Rayo horizontal desde dentro de la sala hasta la pared. normal = hacia la sala (alineada a un eje).</summary>
        static bool Wall(Vector3 from, Vector3 dir, out Vector3 point, out Vector3 normal)
        {
            if (_probe.RayWall(from, dir, 14f, out var hit) || _probe.Ray(from, dir, 14f, out hit))
            {
                point = hit.point;
                normal = Snap(hit.normal);
                if (Vector3.Dot(normal, dir) > 0f) normal = -normal;   // por si el rayo salió por detrás de una cara
                return true;
            }
            point = from + dir * 3f;
            normal = -dir;
            Debug.LogWarning($"[Puzzles] Un rayo no encontró pared (desde {from}). Se usó una posición aproximada.");
            return false;
        }

        static float FloorY(Vector3 above)
        {
            if (_probe.Ray(above + Vector3.up * 0.5f, Vector3.down, 4f, out var hit)) return hit.point.y;
            return above.y;
        }

        /// <summary>Parte superior (y) y centro de un objeto de la arena, para dejar cosas encima.</summary>
        static Vector3 TopOf(string name, int index = 0, string prefixOverride = null)
        {
            Transform t = prefixOverride != null ? _probe.FindPrefix(prefixOverride, index) : _probe.Find(name);
            if (t != null && _probe.BoundsOf(t, out var b)) return new Vector3(b.center.x, b.max.y, b.center.z);
            Debug.LogWarning($"[Puzzles] No encontré '{name}' en la arena; se usa una posición aproximada.");
            return Vector3.zero;
        }

        // ------------------------------------------------------------------ construcción de piezas

        enum Place { Floor, Wall, Center }

        static Bounds RenderBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true).Where(r => !(r is TextMesh)).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        static void SetMaterial(GameObject go, Material mat)
        {
            if (mat == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is TextMesh) continue;
                var arr = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                r.sharedMaterials = arr;
            }
        }

        static void EnsureCollider(GameObject go)
        {
            if (go.GetComponentInChildren<Collider>(true) != null) return;
            var local = new Bounds();
            bool init = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = go.transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!init) { local = new Bounds(p, Vector3.zero); init = true; } else local.Encapsulate(p);
                }
            }
            if (!init) return;
            var box = go.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
        }

        /// <summary>
        /// Instancia un modelo, lo escala a 'targetSize' (lado mayor, en metros), lo orienta y lo coloca.
        /// facing = hacia dónde debe mirar su frente (cero = sin girar). panel = placa fina: su eje más delgado se alinea con facing.
        /// Devuelve null si el modelo no se puede cargar (el llamador usa geometría básica).
        /// </summary>
        static GameObject Model(string key, string assetPath, string name, Transform parent, Material mat, float targetSize,
            Vector3 pos, Vector3 facing, Place mode, bool panel = false)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null) { MissingModels[Path.GetFileName(assetPath)] = true; return null; }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var b = RenderBounds(go);
            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
            if (longest < 1e-5f) { Object.DestroyImmediate(go); MissingModels[Path.GetFileName(assetPath)] = true; return null; }
            go.transform.localScale = Vector3.one * (targetSize / longest);

            Quaternion rot = Quaternion.identity;
            if (facing.sqrMagnitude > 0.001f)
            {
                b = RenderBounds(go);
                var look = Quaternion.LookRotation(facing.normalized, Vector3.up);
                rot = panel ? PanelRotation(go, facing, look) : look;
                if (YawFix.TryGetValue(key, out float yaw) && Mathf.Abs(yaw) > 0.01f) rot = Quaternion.Euler(0f, yaw, 0f) * rot;
            }
            go.transform.rotation = rot;

            b = RenderBounds(go);
            Vector3 delta;
            switch (mode)
            {
                case Place.Floor:
                    delta = new Vector3(pos.x - b.center.x, pos.y - b.min.y, pos.z - b.center.z);
                    break;
                case Place.Wall:
                    {
                        var n = facing.normalized;
                        float half = Mathf.Abs(n.x) * b.extents.x + Mathf.Abs(n.y) * b.extents.y + Mathf.Abs(n.z) * b.extents.z;
                        delta = pos + n * (half + 0.01f) - b.center;
                        break;
                    }
                default:
                    delta = pos - b.center;
                    break;
            }
            go.transform.position += delta;

            SetMaterial(go, mat);
            EnsureCollider(go);
            return go;
        }

        /// <summary>
        /// Gira una placa para que su frente mire hacia 'facing'. Eje más delgado = grosor; el lado con más vértices
        /// (botones, tirador, bisagras) es el frente. El eje vertical del modelo se mantiene vertical.
        /// </summary>
        static Quaternion PanelRotation(GameObject go, Vector3 facing, Quaternion look)
        {
            var pts = new List<Vector3>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var l2w = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) pts.Add(l2w.MultiplyPoint3x4(v));
            }
            if (pts.Count < 8) return look;

            Vector3 mn = pts[0], mx = pts[0];
            foreach (var p in pts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            Vector3 size = mx - mn;
            int thin = size.x <= size.y && size.x <= size.z ? 0 : (size.y <= size.z ? 1 : 2);

            float lo = mn[thin], hi = mx[thin], span = Mathf.Max(1e-5f, hi - lo);
            int front = 0, back = 0;
            foreach (var p in pts)
            {
                if (p[thin] > hi - span * 0.25f) front++;
                else if (p[thin] < lo + span * 0.25f) back++;
            }
            float sign = front >= back ? 1f : -1f;
            Vector3 normalAxis = (thin == 0 ? Vector3.right : thin == 1 ? Vector3.up : Vector3.forward) * sign;
            Vector3 upAxis = thin != 1 ? Vector3.up : (size.x >= size.z ? Vector3.right : Vector3.forward);

            var basis = Quaternion.LookRotation(normalAxis, upAxis);   // dónde está hoy el frente y qué lado es "arriba"
            return look * Quaternion.Inverse(basis);
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = rot ?? Quaternion.identity;
            var ps = parent != null ? parent.lossyScale : Vector3.one;   // el tamaño que se pide es el del MUNDO, aunque el padre esté escalado
            go.transform.localScale = new Vector3(scale.x / ps.x, scale.y / ps.y, scale.z / ps.z);
            SetMaterial(go, mat);
            return go;
        }

        static GameObject Group(string name, Transform parent, Vector3 pos, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = rot ?? Quaternion.identity;
            return go;
        }

        /// <summary>Texto 3D que se lee desde el lado de 'normal' (el texto mira hacia -normal).</summary>
        static TextMesh Text(Transform parent, string name, string text, Vector3 worldPos, Vector3 normal, float height, Color color)
        {
            var tm = PuzzleKit.MakeText(parent, text, Vector3.zero, height, color);
            tm.gameObject.name = name;
            tm.transform.position = worldPos;
            tm.transform.rotation = Quaternion.LookRotation(-normal.normalized, Vector3.up);
            return tm;
        }

        static void Pickup(GameObject go, ItemId item, int count, string label, bool startActive)
        {
            var p = go.GetComponent<ItemPickup>() ?? go.AddComponent<ItemPickup>();
            p.item = item;
            p.count = count;
            p.label = label;
            go.SetActive(startActive);
        }

        // ------------------------------------------------------------------ piezas con modelo o geometría básica

        static GameObject KeypadProp(string name, Transform parent, Vector3 p, Vector3 n)
        {
            var go = Model("keypad", $"{ModelDir}/Keypad/numpad.fbx", name, parent, M("KeypadRust"), 0.26f, p, n, Place.Wall, true);
            if (go != null) return go;
            go = Prim(PrimitiveType.Cube, name, parent, p + n * 0.03f, new Vector3(0.2f, 0.28f, 0.05f), M("KeypadRust"), Quaternion.LookRotation(n));
            var scr = Prim(PrimitiveType.Cube, "Screen", go.transform, p + n * 0.062f + Vector3.up * 0.09f, new Vector3(0.14f, 0.05f, 0.005f), M("ScreenGlow"), Quaternion.LookRotation(n));
            Object.DestroyImmediate(scr.GetComponent<Collider>());
            for (int i = 0; i < 9; i++)
            {
                var k = Prim(PrimitiveType.Cube, "Key", go.transform, p + n * 0.062f + Vector3.up * (0.03f - (i / 3) * 0.045f) + Vector3.Cross(Vector3.up, n) * ((i % 3 - 1) * 0.045f),
                    new Vector3(0.035f, 0.03f, 0.01f), M("MetalLight"), Quaternion.LookRotation(n));
                Object.DestroyImmediate(k.GetComponent<Collider>());
            }
            return go;
        }

        static GameObject KeyProp(string name, Transform parent, Vector3 pos, bool active, ItemId item, string label)
        {
            var go = Model("key", $"{ModelDir}/Glb/llaves_ornamentadas.glb", name, parent, null, 0.16f, pos, Vector3.zero, Place.Floor);
            if (go == null)
            {
                go = Group(name, parent, pos);
                var head = Prim(PrimitiveType.Cylinder, "Head", go.transform, pos + Vector3.up * 0.02f, new Vector3(0.05f, 0.005f, 0.05f), M("Gold"));
                var shaft = Prim(PrimitiveType.Cube, "Shaft", go.transform, pos + new Vector3(0.07f, 0.02f, 0f), new Vector3(0.1f, 0.012f, 0.014f), M("Gold"));
                var tooth = Prim(PrimitiveType.Cube, "Tooth", go.transform, pos + new Vector3(0.11f, 0.02f, 0.014f), new Vector3(0.015f, 0.012f, 0.02f), M("Gold"));
                foreach (var c in new[] { head, shaft, tooth }) Object.DestroyImmediate(c.GetComponent<Collider>());
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0.05f, 0.02f, 0f);
                box.size = new Vector3(0.2f, 0.06f, 0.08f);
            }
            else if (go.GetComponent<Collider>() == null) EnsureCollider(go);
            Pickup(go, item, 1, label, active);
            return go;
        }

        static GameObject ModelOrPlaceholder(string key, string path, string name, Transform parent, Material mat, float size, Vector3 pos, Vector3 facing,
            Place mode, bool panel, System.Func<GameObject> fallback)
        {
            var go = Model(key, path, name, parent, mat, size, pos, facing, mode, panel);
            return go != null ? go : fallback();
        }

        static GameObject CrowbarProp(string name, Transform parent, Vector3 pos, bool pickup)
        {
            var go = Model("crowbar", $"{ModelDir}/Glb/crowbar_half-life.glb", name, parent, null, 0.7f, pos, Vector3.zero, Place.Center);
            if (go == null)
            {
                go = Group(name, parent, pos);
                var bar = Prim(PrimitiveType.Cylinder, "Bar", go.transform, pos, new Vector3(0.035f, 0.32f, 0.035f), M("MetalLight"), Quaternion.Euler(0f, 0f, 90f));
                var hook = Prim(PrimitiveType.Cylinder, "Hook", go.transform, pos + new Vector3(0.32f, -0.05f, 0f), new Vector3(0.035f, 0.07f, 0.035f), M("MetalLight"), Quaternion.Euler(0f, 0f, 35f));
                foreach (var c in new[] { bar, hook }) Object.DestroyImmediate(c.GetComponent<Collider>());
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0.14f, 0f, 0f);
                box.size = new Vector3(0.75f, 0.14f, 0.12f);
            }
            if (pickup) Pickup(go, ItemId.Crowbar, 1, "Palanca", false);
            return go;
        }

        // ------------------------------------------------------------------ arenas

        static void RemoveOld(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(x => x.name == "Puzzles" && x.parent == root.transform).ToList())
            {
                Object.DestroyImmediate(t.gameObject);
                changed = true;
            }
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        static void BuildArena(string path, bool quirofano)
        {
            using (_probe = new Probe(path))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                var slot = root.GetComponent<ArenaSlot>();
                Vector3 spawn = slot != null && slot.spawnPoint != null ? slot.spawnPoint.position : Vector3.zero;
                float F = FloorY(spawn);

                var puzzles = new GameObject("Puzzles").transform;
                puzzles.SetParent(root.transform, false);
                var props = new GameObject("Props").transform; props.SetParent(puzzles, false);
                var ctl = new GameObject("Controllers").transform; ctl.SetParent(puzzles, false);

                if (quirofano) BuildQuirofano(root, props, ctl, F, spawn);
                else BuildMaquinas(root, props, ctl, F, spawn);

                if (root.GetComponent<ArenaAmbience>() == null) root.AddComponent<ArenaAmbience>();
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                Debug.Log($"[Puzzles] {Path.GetFileName(path)}: puzzles y objetos colocados.");
            }
            _probe = null;
        }

        static T Add<T>(GameObject go) where T : Component => go.GetComponent<T>() ?? go.AddComponent<T>();

        static void Controller(Transform ctl, string name, System.Type type) => new GameObject(name).AddComponent(type).transform.SetParent(ctl, false);

        static void Clue(Transform root, string objectName, ClueNote.Clue clue, string requires = "", string locked = "")
        {
            var t = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == objectName);
            if (t == null) { Debug.LogWarning($"[Puzzles] No encontré '{objectName}' para la pista {clue}."); return; }
            var c = Add<ClueNote>(t.gameObject);
            c.clue = clue;
            c.requiresFlag = requires;
            if (!string.IsNullOrEmpty(locked)) c.lockedMessage = locked;
            EnsureCollider(t.gameObject);
        }

        static Vector3 FreeSide(Vector3 from, Vector3 lateral, float dist)
        {
            // Devuelve +lateral o -lateral según cuál tenga más espacio libre.
            bool a = _probe.Ray(from, lateral, dist, out _);
            bool b = _probe.Ray(from, -lateral, dist, out _);
            if (a && !b) return -lateral;
            return lateral;
        }

        static void CeilingLamp(Transform props, Vector3 below, string name)
        {
            if (!_probe.Ray(below, Vector3.up, 8f, out var hit)) return;
            var lamp = Model("lamp", $"{ModelDir}/CeilingLamp/ceiling_lamp.fbx", name, props, M("CeilingLamp"), 0.8f, hit.point + Vector3.down * 0.01f, Vector3.zero, Place.Center);
            if (lamp == null) return;
            // colgada del techo: su parte alta toca el techo
            var b = RenderBounds(lamp);
            lamp.transform.position += Vector3.up * (hit.point.y - b.max.y);
            foreach (var c in lamp.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        }

        // ---------------------------------------------------------------- Quirófano

        static void BuildQuirofano(GameObject root, Transform props, Transform ctl, float F, Vector3 spawn)
        {
            Vector3 p, n;

            // Q1: teclado del armario (pared sur, a la derecha del armario MedRack_V2 (1))
            Wall(new Vector3(0.95f, F + 1.35f, -9f), Vector3.forward, out p, out n);
            var kp1 = KeypadProp("Prop_Keypad_Q1", props, p, n);
            Add<KeypadTerminal>(kp1).kind = KeypadTerminal.Kind.Cabinet;

            // Q5: teclado de la puerta (junto a la puerta doble)
            Wall(new Vector3(-2.55f, F + 1.35f, -9f), Vector3.forward, out p, out n);
            var kp5 = KeypadProp("Prop_Keypad_Q5", props, p, n);
            Add<KeypadTerminal>(kp5).kind = KeypadTerminal.Kind.Door;

            // Q6: palanca de la puerta (pared oeste, cerca de la puerta)
            Wall(new Vector3(-5.0f, F + 1.15f, -7.7f), Vector3.left, out p, out n);
            BuildLever("Prop_Lever_Q6", props, p, n, Role.Cirujano, true);

            // Llave del armario: aparece al abrirlo (empieza oculta)
            KeyProp("Pickup_KeyCabinet", props, new Vector3(-0.4f, F + 0.15f, -8.2f), false, ItemId.KeyCabinet, "Llave de cajón");

            // Fusibles (empiezan ocultos: salen del último cajón)
            var fuses = Group("Pickup_Fuses", props, new Vector3(2f, F + 1f, -9f));
            for (int i = 0; i < 3; i++)
            {
                var f = Prim(PrimitiveType.Cylinder, "Fuse", fuses.transform, fuses.transform.position + new Vector3(0f, 0.0f, (i - 1) * 0.06f),
                    new Vector3(0.03f, 0.035f, 0.03f), M("FuseRed"), Quaternion.Euler(90f, 0f, 0f));
                Object.DestroyImmediate(f.GetComponent<Collider>());
            }
            var fb = fuses.AddComponent<BoxCollider>(); fb.size = new Vector3(0.1f, 0.1f, 0.22f);
            Pickup(fuses, ItemId.Fuse, 3, "Fusibles", false);

            // Q2: nota térmica sobre el calentador
            Vector3 heater = TopOf("Heater");
            if (heater != Vector3.zero)
            {
                Wall(new Vector3(heater.x, F + 1.7f, -11.5f), Vector3.back, out p, out n);
                var plate = Prim(PrimitiveType.Cube, "ThermalPlate", props, p + n * 0.02f, new Vector3(0.62f, 0.34f, 0.03f), M("Black"), Quaternion.LookRotation(n));
                Object.DestroyImmediate(plate.GetComponent<Collider>());
                Text(props, "ThermalNote", "18 °C\n·   ·   ·", p + n * 0.045f, n, 0.07f, new Color(0.75f, 0.75f, 0.8f));
            }

            // Q4: proyector sobre la mesa blanca + 3 diapositivas por la sala
            Vector3 table = TopOf("TableWhite");
            if (table != Vector3.zero)
            {
                var proj = ModelOrPlaceholder("projector", $"{ModelDir}/Projector/projector.fbx", "Prop_Projector", props, M("ProjectorGray"), 0.45f,
                    table, Vector3.left, Place.Floor, false, () => Prim(PrimitiveType.Cube, "Prop_Projector", props, table + Vector3.up * 0.1f, new Vector3(0.3f, 0.2f, 0.25f), M("ProjectorGray")));
                var beam = new GameObject("ProjectorBeam");
                beam.transform.SetParent(proj.transform, true);
                beam.transform.position = RenderBounds(proj).center + Vector3.left * 0.2f;
                beam.transform.rotation = Quaternion.LookRotation(Vector3.left);
                var l = beam.AddComponent<Light>();
                l.type = LightType.Spot; l.range = 9f; l.spotAngle = 38f; l.intensity = 25f; l.color = new Color(0.8f, 0.9f, 1f); l.enabled = false;
            }
            // Superficies BAJAS y visibles desde arriba (mesa de operaciones, mesa blanca, archivador B): antes una quedó sobre un armario de 2 m.
            Vector3[] slideSpots = { TopOf("SurgeryTable") + new Vector3(0.35f, 0.02f, 0.2f), TopOf("TableWhite") + new Vector3(0.1f, 0.02f, 0.45f), TopOf("SheetRack (1)") + new Vector3(0f, 0.02f, 0f) };
            for (int i = 0; i < 3; i++)
            {
                var s = Prim(PrimitiveType.Cube, "Pickup_Slide_" + i, props, slideSpots[i] + Vector3.up * 0.006f, new Vector3(0.14f, 0.012f, 0.1f), M("SlideCyan"), Quaternion.Euler(0f, 20f * i, 0f));
                Pickup(s, ItemId.Slide, 1, "Diapositiva", true);
            }

            // Q1: expediente (tablet) sobre la mesa de operaciones; frascos Kronon-X sobre el mueble del oeste
            Vector3 surgery = TopOf("SurgeryTable");
            if (surgery != Vector3.zero)
            {
                var tab = ModelOrPlaceholder("tablet", $"{ModelDir}/PaperTablet/paper_tablet.fbx", "Prop_Tablet", props, M("Tablet"), 0.36f,
                    surgery + new Vector3(-0.3f, 0f, -0.1f), Vector3.zero, Place.Floor, false, () => Prim(PrimitiveType.Cube, "Prop_Tablet", props, surgery + Vector3.up * 0.01f, new Vector3(0.3f, 0.02f, 0.4f), M("PlateWhite")));
                if (tab != null) { Add<ClueNote>(tab).clue = ClueNote.Clue.Expediente; EnsureCollider(tab); }
            }
            Vector3 westRack = TopOf("MedRack (1)");   // mueble bajo del oeste (el "MedRack" a secas es el armario alto)
            if (westRack != Vector3.zero)
            {
                var bot = ModelOrPlaceholder("bottles", $"{ModelDir}/Bottles/Bottles_Kronon-X.fbx", "Prop_Bottles", props, M("Bottles"), 0.32f,
                    westRack + new Vector3(0f, 0f, 0.35f), Vector3.right, Place.Floor, false, () => Prim(PrimitiveType.Cylinder, "Prop_Bottles", props, westRack + Vector3.up * 0.1f, new Vector3(0.08f, 0.1f, 0.08f), M("SlideCyan")));
                if (bot != null) { Add<ClueNote>(bot).clue = ClueNote.Clue.Bottles; EnsureCollider(bot); }
            }

            // Pistas en objetos que ya existían
            Clue(root.transform, "PosterHuman", ClueNote.Clue.Poster);
            Clue(root.transform, "Paper (3)", ClueNote.Clue.Story);

            // Boca del tubo neumático junto al lavabo
            BuildTube(props, root.transform, "Sink_V1", F, Vector3.right);

            // Decoración: lámpara de techo
            CeilingLamp(props, new Vector3(-2.2f, F + 2f, -10.2f), "Prop_CeilingLamp");

            // Modo individual (1v1, prueba, compañero desconectado): la palanca sale del último cajón y las notas sustituyen al Operador
            CrowbarProp("Pickup_Crowbar", props, new Vector3(2f, F + 1f, -9f), true);
            var soloQ = Group("SoloProps", props, Vector3.zero);
            soloQ.AddComponent<SoloGroup>();
            var soloQc = Group("Content", soloQ.transform, Vector3.zero);
            Wall(new Vector3(1.7f, F + 1.5f, -9f), Vector3.forward, out p, out n);
            BuildNote(soloQc.transform, "Note_SoloDrawers", p, n, ClueNote.Clue.SoloDrawers);
            Wall(new Vector3(1.0f, F + 1.5f, -11.5f), Vector3.back, out p, out n);
            BuildNote(soloQc.transform, "Note_SoloLegendM", p, n, ClueNote.Clue.SoloLegendM);
            Wall(new Vector3(-5.4f, F + 1.5f, -11.5f), Vector3.back, out p, out n);
            BuildNote(soloQc.transform, "Note_SoloSymbolsB", p, n, ClueNote.Clue.SoloSymbolsB);
            soloQc.SetActive(false);

            // Controladores
            Controller(ctl, "Flavor", typeof(FlavorController));
            Controller(ctl, "Q2_Heater", typeof(HeaterPuzzle));
            Controller(ctl, "Q3_Drawers", typeof(DrawerPuzzle));
            Controller(ctl, "Q4_Projector", typeof(ProjectorPuzzle));
        }

        // ---------------------------------------------------------------- Cuarto de máquinas

        static void BuildMaquinas(GameObject root, Transform props, Transform ctl, float F, Vector3 spawn)
        {
            Vector3 p, n;

            // M1: 8 etiquetas con símbolo sobre las dos estanterías
            var shelves = new[] { _probe.FindPrefix("BigShelf_Stock", 0), _probe.FindPrefix("BigShelf_Stock", 1) };
            for (int s = 0; s < 8; s++)
            {
                var shelf = shelves[s % 2] != null ? shelves[s % 2] : shelves[0];
                if (shelf == null || !_probe.BoundsOf(shelf, out var sb)) continue;
                int k = s / 2;   // 0..3 por estantería
                float z = Mathf.Lerp(sb.min.z, sb.max.z, k % 2 == 0 ? 0.28f : 0.72f);
                // Las estanterías son marcos abiertos: un rayo atraviesa los huecos y la etiqueta quedaría DENTRO o detrás.
                // Se pegan al plano frontal de la estantería (mira hacia la sala, +x), a dos alturas, y sobresalen para poder apuntarlas.
                float y = Mathf.Lerp(sb.min.y, sb.max.y, k < 2 ? 0.28f : 0.62f);
                Vector3 pos = new Vector3(sb.max.x + 0.03f, y, z);
                BuildLabel(props, s, pos, Vector3.right);
            }

            // M1: pizarra de la leyenda (pared norte)
            Wall(new Vector3(9.3f, F + 1.0f, -2f), Vector3.back, out p, out n);
            var wb = ModelOrPlaceholder("whiteboard", $"{ModelDir}/Whiteboard/whiteboard.obj", "Prop_Whiteboard", props, M("Whiteboard"), 1.5f,
                new Vector3(p.x, F, p.z) + n * 0.1f, n, Place.Floor, true,
                () => Prim(PrimitiveType.Cube, "Prop_Whiteboard", props, new Vector3(p.x, F + 1.2f, p.z) + n * 0.1f, new Vector3(1.4f, 0.9f, 0.05f), M("PlateWhite"), Quaternion.LookRotation(n)));
            Add<ClueNote>(wb).clue = ClueNote.Clue.Legend;
            EnsureCollider(wb);

            // M2: volantes (volante de coche) y manómetros en las 3 tuberías + lecturas
            var pipes = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Pipe")).OrderBy(t => t.position.x).Take(3).ToList();
            for (int i = 0; i < 3; i++)
            {
                if (i >= pipes.Count) break;
                var pb = RenderBounds(pipes[i].gameObject);
                Vector3 baseP = new Vector3(pb.center.x, F, pb.max.z + 0.14f);
                var wheel = ModelOrPlaceholder("wheel", $"{ModelDir}/SteeringWheel/steering_wheel.fbx", "Prop_Valve_" + i, props, M("WheelBlack"), 0.42f,
                    new Vector3(baseP.x, F + 1.1f, baseP.z), Vector3.forward, Place.Center, true,
                    () => Prim(PrimitiveType.Cylinder, "Prop_Valve_" + i, props, new Vector3(baseP.x, F + 1.1f, baseP.z), new Vector3(0.4f, 0.015f, 0.4f), M("WheelBlack"), Quaternion.Euler(90f, 0f, 0f)));
                EnsureCollider(wheel);

                var gauge = ModelOrPlaceholder("manometer", $"{ModelDir}/Manometer/Manometr.fbx", "Prop_Manometer_" + i, props, M("Manometer"), 0.25f,
                    new Vector3(baseP.x, F + 1.75f, baseP.z - 0.05f), Vector3.forward, Place.Center, true,
                    () => Prim(PrimitiveType.Cylinder, "Prop_Manometer_" + i, props, new Vector3(baseP.x, F + 1.75f, baseP.z), new Vector3(0.2f, 0.03f, 0.2f), M("MetalLight"), Quaternion.Euler(90f, 0f, 0f)));
                foreach (var c in gauge.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                Text(props, "Readout_Valve_" + i, "0", new Vector3(baseP.x, F + 2.05f, baseP.z + 0.02f), Vector3.forward, 0.12f, new Color(1f, 0.9f, 0.4f));
            }
            Controller(ctl, "M2_Valves", typeof(ValvePuzzle));

            // Modo individual: la presión viene en una nota y los fusibles salen de la taquilla de la palanca
            var soloM = Group("SoloProps", props, Vector3.zero);
            soloM.AddComponent<SoloGroup>();
            var soloMc = Group("Content", soloM.transform, Vector3.zero);
            Wall(new Vector3(8.2f, F + 1.6f, -2f), Vector3.back, out p, out n);
            BuildNote(soloMc.transform, "Note_SoloPressure", p, n, ClueNote.Clue.SoloPressure);
            soloMc.SetActive(false);
            var soloFuses = Group("Pickup_Fuses", props, new Vector3(12.5f, F + 1f, 1f));
            for (int i = 0; i < 3; i++)
            {
                var f = Prim(PrimitiveType.Cylinder, "Fuse", soloFuses.transform, soloFuses.transform.position + new Vector3(0f, 0f, (i - 1) * 0.06f),
                    new Vector3(0.03f, 0.035f, 0.03f), M("FuseRed"), Quaternion.Euler(90f, 0f, 0f));
                Object.DestroyImmediate(f.GetComponent<Collider>());
            }
            var sfb = soloFuses.AddComponent<BoxCollider>(); sfb.size = new Vector3(0.1f, 0.1f, 0.22f);
            Pickup(soloFuses, ItemId.Fuse, 3, "Fusibles", false);

            // M2: televisión (símbolos) sobre la caja del oeste
            Vector3 box = TopOf("Box_V2 (4)");
            if (box == Vector3.zero) box = new Vector3(7.2f, F + 0.7f, -0.13f);
            var tv = ModelOrPlaceholder("tv", $"{ModelDir}/OldTV/old_tv.obj", "Prop_TV", props, M("OldTV"), 0.6f, box, Vector3.right, Place.Floor, false,
                () => Prim(PrimitiveType.Cube, "Prop_TV", props, box + Vector3.up * 0.25f, new Vector3(0.5f, 0.4f, 0.4f), M("Black")));
            var tvc = Add<ClueNote>(tv);
            tvc.clue = ClueNote.Clue.TvSymbols; tvc.requiresFlag = "valves"; tvc.lockedMessage = "La pantalla está apagada. Las válvulas no tienen la presión correcta.";
            EnsureCollider(tv);

            // M3: llave bajo el colchón + palanca en una taquilla (oculta hasta abrirla)
            Vector3 matt = TopOf("Mattress (1)");
            if (matt == Vector3.zero) matt = new Vector3(8f, F + 0.4f, -4.8f);
            KeyProp("Pickup_KeyLocker", props, matt + Vector3.up * 0.02f, true, ItemId.KeyLocker, "Llave de taquilla");
            CrowbarProp("Pickup_Crowbar", props, new Vector3(12.5f, F + 1f, 1f), true);
            Controller(ctl, "M3_Lockers", typeof(LockerPuzzle));

            // M4: consola de tanques, escotilla y placa (pared sur)
            Wall(new Vector3(10.8f, F + 1.5f, 0f), Vector3.forward, out p, out n);
            BuildTankConsole(props, p, n, F);
            Wall(new Vector3(12.0f, F + 1.9f, 0f), Vector3.forward, out var hp, out var hn);
            var plate = ModelOrPlaceholder("plate", $"{ModelDir}/Glb/placa_mercosul.glb", "Prop_Plate", props, null, 0.34f, hp + hn * 0.01f + Vector3.zero, hn, Place.Wall, true,
                () => Prim(PrimitiveType.Cube, "Prop_Plate", props, hp + hn * 0.03f, new Vector3(0.34f, 0.17f, 0.02f), M("PlateWhite"), Quaternion.LookRotation(hn)));
            EnsureCollider(plate);
            var hatch = Prim(PrimitiveType.Cube, "Tank_Hatch", props, hp + hn * 0.12f, new Vector3(0.5f, 0.35f, 0.04f), M("Hatch"), Quaternion.LookRotation(hn));
            Controller(ctl, "M4_Tanks", typeof(TankPuzzle));

            // M5: panel de fusibles, interruptores de colores y monitores con el registro (pared norte, este)
            Wall(new Vector3(9.0f, F + 1.7f, 1.2f), Vector3.left, out p, out n);   // pared oeste, entre la estantería y el tanque
            var fb = ModelOrPlaceholder("fusebox", $"{ModelDir}/FuseBox/fuse_box.fbx", "Prop_FuseBox", props, M("FuseBox"), 0.65f, p + Vector3.zero, n, Place.Wall, true,
                () => Prim(PrimitiveType.Cube, "Prop_FuseBox", props, p + n * 0.05f, new Vector3(0.55f, 0.65f, 0.1f), M("MetalDark"), Quaternion.LookRotation(n)));
            EnsureCollider(fb);
            Vector3 lat = Vector3.Cross(Vector3.up, n);
            // Los interruptores van en la PARED (punto p), justo debajo del panel, sobre una placa oscura.
            var fbb = RenderBounds(fb);
            float swY = Mathf.Max(F + 0.75f, fbb.min.y - 0.22f);
            var swPlate = Prim(PrimitiveType.Cube, "SwitchPlate", props, new Vector3(p.x, swY, p.z) + n * 0.02f, new Vector3(0.8f, 0.24f, 0.03f), M("MetalDark"), Quaternion.LookRotation(n));
            Object.DestroyImmediate(swPlate.GetComponent<Collider>());
            for (int i = 0; i < 3; i++)
            {
                string[] mn = { "ButtonRed", "ButtonGreen", "ButtonBlue" };
                Prim(PrimitiveType.Cube, "Switch_" + i, props, new Vector3(p.x, swY, p.z) + n * 0.06f + lat * ((i - 1) * 0.24f),
                    new Vector3(0.14f, 0.14f, 0.08f), M(mn[i]), Quaternion.LookRotation(n));
            }
            Vector3 shelfTop = TopOf("BigShelf_Stock (6)");
            if (shelfTop == Vector3.zero) shelfTop = TopOf("", 0, "BigShelf_Stock");
            var mon = ModelOrPlaceholder("monitors", $"{ModelDir}/Glb/basic_pc_monitors.glb", "Prop_Monitors", props, null, 0.7f,
                shelfTop == Vector3.zero ? new Vector3(6.8f, F + 2f, -1.6f) : shelfTop, Vector3.right, Place.Floor, false,
                () => Prim(PrimitiveType.Cube, "Prop_Monitors", props, (shelfTop == Vector3.zero ? new Vector3(6.8f, F + 2f, -1.6f) : shelfTop) + Vector3.up * 0.25f, new Vector3(0.4f, 0.4f, 0.3f), M("Black")));
            Add<ClueNote>(mon).clue = ClueNote.Clue.MachineLog;
            EnsureCollider(mon);
            Controller(ctl, "M5_Fuses", typeof(FusePuzzle));
            Controller(ctl, "Flavor", typeof(FlavorController));

            // M6: interruptor general (pared oeste, norte)
            Wall(new Vector3(9.0f, F + 1.3f, -4.7f), Vector3.left, out p, out n);
            BuildLever("Prop_Lever_M6", props, p, n, Role.Operador, false);

            // Boca del tubo junto al lavabo
            BuildTube(props, root.transform, "Sink_V1 (6)", F, Vector3.left);

            CeilingLamp(props, new Vector3(9.7f, F + 2f, 0.5f), "Prop_CeilingLamp");
        }

        // ---------------------------------------------------------------- piezas compuestas

        /// <summary>Nota de papel pegada a la pared (modo individual).</summary>
        static void BuildNote(Transform parent, string name, Vector3 wallPoint, Vector3 n, ClueNote.Clue clue)
        {
            var go = Group(name, parent, wallPoint + n * 0.012f, Quaternion.LookRotation(n));
            var plate = Prim(PrimitiveType.Cube, "Paper", go.transform, go.transform.position, new Vector3(0.3f, 0.38f, 0.012f), M("PlateWhite"), go.transform.rotation);
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            var tm = PuzzleKit.MakeText(go.transform, "NOTA", new Vector3(0f, 0.08f, 0.009f), 0.06f, new Color(0.05f, 0.05f, 0.08f), Quaternion.Euler(0f, 180f, 0f));
            tm.gameObject.name = "Title";
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0f, 0.01f);
            bc.size = new Vector3(0.34f, 0.42f, 0.06f);
            Add<ClueNote>(go).clue = clue;
        }

        static void BuildLabel(Transform props, int symbol, Vector3 pos, Vector3 n)
        {
            var go = Group("Label_" + symbol, props, pos + n * 0.02f, Quaternion.LookRotation(n));
            var plate = Prim(PrimitiveType.Cube, "Plate", go.transform, go.transform.position, new Vector3(0.34f, 0.2f, 0.02f), M("PlateWhite"), go.transform.rotation);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Symbol";
            quad.transform.SetParent(go.transform, false);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.localPosition = new Vector3(-0.08f, 0f, 0.012f);
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            quad.transform.localScale = new Vector3(0.15f, 0.15f, 1f);
            SetMaterial(quad, M("Symbol_" + symbol));
            var d = PuzzleKit.MakeText(go.transform, "?", new Vector3(0.09f, 0f, 0.013f), 0.13f, new Color(0.05f, 0.05f, 0.08f), Quaternion.Euler(0f, 180f, 0f));
            d.gameObject.name = "Digit";
            var ll = go.AddComponent<LegendLabel>();
            ll.symbol = symbol;
            var bc = go.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0f, 0.01f);
            bc.size = new Vector3(0.34f, 0.2f, 0.06f);
            Object.DestroyImmediate(plate.GetComponent<Collider>());
        }

        static void BuildLever(string name, Transform props, Vector3 wallPoint, Vector3 n, Role role, bool crowbarMount)
        {
            var go = Group(name, props, wallPoint, Quaternion.LookRotation(n));
            Prim(PrimitiveType.Cube, "Base", go.transform, wallPoint + n * 0.03f, new Vector3(0.3f, 0.5f, 0.06f), M("MetalDark"), go.transform.rotation);
            var slot = Prim(PrimitiveType.Cube, "Slot", go.transform, wallPoint + n * 0.065f, new Vector3(0.05f, 0.34f, 0.02f), M("Black"), go.transform.rotation);
            Object.DestroyImmediate(slot.GetComponent<Collider>());

            var handle = Group("Handle", crowbarMount ? Group("Lever_Crowbar", go.transform, wallPoint + n * 0.09f).transform : go.transform, wallPoint + n * 0.09f - Vector3.up * 0.12f, go.transform.rotation);
            if (crowbarMount)
            {
                var cb = CrowbarProp("Crowbar_Mounted", handle.transform, wallPoint + n * 0.12f, false);
                cb.transform.rotation = Quaternion.LookRotation(n, Vector3.up) * Quaternion.Euler(0f, 0f, 90f);
                cb.transform.position = wallPoint + n * 0.14f + Vector3.up * 0.1f;
                foreach (var c in cb.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            }
            else
            {
                var bar = Prim(PrimitiveType.Cylinder, "Bar", handle.transform, wallPoint + n * 0.12f + Vector3.up * 0.02f, new Vector3(0.04f, 0.22f, 0.04f), M("MetalLight"));
                var knob = Prim(PrimitiveType.Sphere, "Knob", handle.transform, wallPoint + n * 0.12f + Vector3.up * 0.26f, Vector3.one * 0.09f, M("ButtonRed"));
                foreach (var c in new[] { bar, knob }) Object.DestroyImmediate(c.GetComponent<Collider>());
            }

            var lc = go.transform.Find("Lever_Crowbar");
            if (lc != null) lc.gameObject.SetActive(false);   // la palanca aparece al montarla
            var bc = go.AddComponent<BoxCollider>();
            bc.center = go.transform.InverseTransformPoint(wallPoint + n * 0.1f);
            bc.size = new Vector3(0.34f, 0.6f, 0.22f);
            Add<SyncLever>(go).role = role;
        }

        static void BuildTube(Transform props, Transform arenaRoot, string sinkName, float F, Vector3 outwardGuess)
        {
            var sinkT = _probe.Find(sinkName);
            if (sinkT == null || !_probe.BoundsOf(sinkT, out var sb)) { Debug.LogWarning($"[Puzzles] No encontré {sinkName}: no se coloca la boca del tubo."); return; }

            Vector3 outward = PuzzleKit.SnapToAxis(outwardGuess);
            Vector3 lateral = Vector3.Cross(Vector3.up, outward);
            Vector3 probeFrom = new Vector3(sb.center.x, F + 0.6f, sb.center.z) + outward * 0.5f;
            lateral = FreeSide(probeFrom, lateral, 1.2f);
            Vector3 pos = new Vector3(sb.center.x, F, sb.center.z) + outward * 0.45f + lateral * 0.95f;
            const float MouthHeight = 0.55f;

            var tube = Model("tube", $"{ModelDir}/Tube/tubo.obj", "Prop_Tube", props, M("Tube"), 0.55f, pos + Vector3.up * MouthHeight, Vector3.zero, Place.Floor);
            if (tube == null)
            {
                tube = Group("Prop_Tube", props, pos);
                var pipe = Prim(PrimitiveType.Cylinder, "Pipe", tube.transform, pos + Vector3.up * 0.45f, new Vector3(0.22f, 0.45f, 0.22f), M("MetalLight"));
                var ring = Prim(PrimitiveType.Cylinder, "Ring", tube.transform, pos + Vector3.up * 0.92f, new Vector3(0.3f, 0.03f, 0.3f), M("MetalDark"));
                foreach (var c in new[] { ring }) Object.DestroyImmediate(c.GetComponent<Collider>());
            }
            // Si la boca es solo un codo pequeño, se le añade un soporte vertical para que se vea el tubo subir desde el suelo.
            var tb = RenderBounds(tube);
            if (tb.size.y < 0.7f)
            {
                float h = Mathf.Max(0.1f, tb.min.y - F);
                var stand = Prim(PrimitiveType.Cylinder, "Stand", tube.transform, new Vector3(tb.center.x, F + h * 0.5f, tb.center.z), new Vector3(0.14f, h * 0.5f, 0.14f), M("MetalDark"));
                Object.DestroyImmediate(stand.GetComponent<Collider>());
            }
            EnsureCollider(tube);
            var cap = Prim(PrimitiveType.Capsule, "TubeCapsule", tube.transform, RenderBounds(tube).center + Vector3.up * 0.1f + outward * 0.05f, new Vector3(0.12f, 0.12f, 0.12f), M("Gold"));
            Object.DestroyImmediate(cap.GetComponent<Collider>());
            cap.SetActive(false);
            Add<TubeMouth>(tube);
        }

        static void BuildTankConsole(Transform props, Vector3 wallPoint, Vector3 n, float F)
        {
            var go = Group("Prop_TankConsole", props, wallPoint, Quaternion.LookRotation(n));
            Vector3 lat = Vector3.Cross(Vector3.up, n);
            Vector3 c = new Vector3(wallPoint.x, F + 1.35f, wallPoint.z);
            Prim(PrimitiveType.Cube, "Panel", go.transform, c + n * 0.04f, new Vector3(1.0f, 0.62f, 0.08f), M("MetalDark"), go.transform.rotation);
            for (int i = 0; i < 6; i++)
            {
                var b = Prim(PrimitiveType.Cube, "Tank_Btn_" + i, props, c + n * 0.095f + lat * ((i % 3 - 1) * 0.3f) + Vector3.up * (i < 3 ? -0.12f : -0.27f),
                    new Vector3(0.22f, 0.12f, 0.05f), M("TankButton"), go.transform.rotation);
            }
            Text(props, "Tank_Display", "A 0/5 L\nB 0/3 L", c + n * 0.1f + Vector3.up * 0.15f, n, 0.07f, new Color(0.6f, 0.9f, 1f));
            Object.DestroyImmediate(go.transform.Find("Panel").GetComponent<Collider>());
        }
    }
}
