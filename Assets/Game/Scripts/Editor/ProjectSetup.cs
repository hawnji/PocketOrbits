using System.IO;
using PocketOrbits.Bootstrap;
using PocketOrbits.Rendering;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if PO_URP
using UnityEngine.Rendering.Universal;
#endif

namespace PocketOrbits.Editor
{
    /// <summary>
    /// One-click (and batch-mode) project configuration, so the repository only needs scripts and data:
    ///   1. installs URP if missing (domain reload, then step 2 runs automatically),
    ///   2. creates and assigns a mobile-tuned URP asset,
    ///   3. generates the shared materials into Resources so their shaders ship in builds,
    ///   4. creates the Main scene and registers it in Build Settings,
    ///   5. applies Android player settings (portrait, IL2CPP, ARM64, min API 24, legacy+new input).
    /// Menu: Pocket Orbits > Set Up Project. Batch: -executeMethod PocketOrbits.Editor.ProjectSetup.RunBatch
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/Game/Scenes/Main.unity";
        public const string SettingsFolder = "Assets/Game/Settings";
        public const string MaterialsFolder = "Assets/Game/Resources/Materials";
        const string UrpPackage = "com.unity.render-pipelines.universal";
        const string PendingKey = "PocketOrbits.SetupPending";

        static AddRequest _install;

        static ProjectSetup()
        {
            // First open (no scene yet) or a pending second stage after URP installed: continue automatically.
            if (!File.Exists(ScenePath) || SessionState.GetBool(PendingKey, false))
                EditorApplication.delayCall += () => Run(interactive: !Application.isBatchMode);
        }

        [MenuItem("Pocket Orbits/Set Up Project", priority = 0)]
        public static void RunFromMenu() => Run(interactive: true);

        /// <summary>
        /// Batch entry point. Run it twice on a fresh checkout: the first run installs URP (scripts must reload
        /// before URP types exist), the second creates the URP asset and regenerates materials. Exit code 0 = done,
        /// 2 = URP was just installed and a second run is needed.
        /// </summary>
        public static void RunBatch()
        {
            if (!UrpInstalled)
            {
                var req = Client.Add(UrpPackage);
                while (!req.IsCompleted) System.Threading.Thread.Sleep(100);
                ConfigureCommon();
                if (req.Status != StatusCode.Success)
                {
                    Debug.LogError("[PocketOrbits] URP install failed: " + req.Error?.message + " (built-in pipeline will be used).");
                    EditorApplication.Exit(0);
                    return;
                }
                Debug.Log("[PocketOrbits] URP installed. Run RunBatch again to finish URP configuration.");
                EditorApplication.Exit(2);
                return;
            }
            Run(interactive: false);
            EditorApplication.Exit(0);
        }

        public static bool UrpInstalled
        {
            get
            {
#if PO_URP
                return true;
#else
                return false;
#endif
            }
        }

        public static void Run(bool interactive)
        {
            if (!UrpInstalled)
            {
                if (_install != null) return;
                Debug.Log("[PocketOrbits] Installing " + UrpPackage + " ...");
                SessionState.SetBool(PendingKey, true);
                _install = Client.Add(UrpPackage);
                EditorApplication.update += WaitForInstall;
                // Configure everything that does not need URP right away (the game also runs on the built-in pipeline).
                ConfigureCommon();
                return;
            }
            SessionState.SetBool(PendingKey, false);
            ConfigureUrp();
            ConfigureCommon();
            Debug.Log("[PocketOrbits] Project setup complete.");
            if (interactive) EditorUtility.DisplayDialog("Pocket Orbits", "Project setup complete. Open Assets/Game/Scenes/Main.unity and press Play.", "OK");
        }

        static void WaitForInstall()
        {
            if (_install == null || !_install.IsCompleted) return;
            EditorApplication.update -= WaitForInstall;
            if (_install.Status == StatusCode.Success) Debug.Log("[PocketOrbits] URP installed; setup continues after the script reload.");
            else
            {
                Debug.LogError("[PocketOrbits] Could not install URP: " + _install.Error?.message + ". The game still runs on the built-in pipeline.");
                SessionState.SetBool(PendingKey, false);
            }
            _install = null;
        }

        static void ConfigureCommon()
        {
            EnsureFolder(SettingsFolder);
            EnsureFolder(MaterialsFolder);
            CreateMaterials();
            CreateScene();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void ConfigureUrp()
        {
#if PO_URP
            string assetPath = SettingsFolder + "/PocketOrbits_URP.asset";
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
            if (asset == null)
            {
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, SettingsFolder + "/PocketOrbits_Renderer.asset");
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            // Mobile tuning: no HDR, light MSAA, no real-time shadows (the camera looks straight down anyway).
            asset.supportsHDR = false;
            asset.msaaSampleCount = 2;
            asset.renderScale = 1f;
            asset.shadowDistance = 0f;
            EditorUtility.SetDirty(asset);
            GraphicsSettings.renderPipelineAsset = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);
#endif
        }

        static void CreateMaterials()
        {
            foreach (MaterialKind kind in System.Enum.GetValues(typeof(MaterialKind)))
            {
                string path = MaterialsFolder + "/" + MaterialFactory.AssetName(kind) + ".mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                var fresh = MaterialFactory.Create(kind);
                if (existing != null)
                {
                    // Regenerate in place so the pipeline switch (built-in -> URP) is picked up.
                    existing.shader = fresh.shader;
                    existing.CopyPropertiesFromMaterial(fresh);
                    existing.shaderKeywords = fresh.shaderKeywords;
                    existing.renderQueue = fresh.renderQueue;
                    MaterialFactory.Configure(existing, kind);
                    EditorUtility.SetDirty(existing);
                    Object.DestroyImmediate(fresh);
                }
                else
                {
                    AssetDatabase.CreateAsset(fresh, path);
                }
            }
        }

        static void CreateScene()
        {
            if (!File.Exists(ScenePath))
            {
                EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.02f, 0.03f, 0.10f);
                camGo.transform.SetPositionAndRotation(new Vector3(0, 30, 0), Quaternion.Euler(90, 0, 0));
                new GameObject("PocketOrbits").AddComponent<GameBootstrap>();
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            var scenes = EditorBuildSettings.scenes;
            bool listed = false;
            foreach (var s in scenes) if (s.path == ScenePath) listed = true;
            if (!listed) EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "Pocket Orbits";
            PlayerSettings.productName = "Pocket Orbits";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.pocketorbits.game");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetMobileMTRendering(BuildTargetGroup.Android, true);

            // The prototype uses the legacy Input Manager (touch + mouse); "Both" keeps the new Input System usable too.
            var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (playerSettings != null && playerSettings.Length > 0)
            {
                var so = new SerializedObject(playerSettings[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue == 1)
                {
                    prop.intValue = 2;
                    so.ApplyModifiedProperties();
                    Debug.LogWarning("[PocketOrbits] Active Input Handling set to 'Both'. Restart the editor for it to take effect.");
                }
            }
        }
    }
}
