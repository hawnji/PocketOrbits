using PocketOrbits.Audio;
using PocketOrbits.Core.Progression;
using PocketOrbits.Core.Saving;
using PocketOrbits.Gameplay;
using PocketOrbits.Input;
using PocketOrbits.Levels;
using PocketOrbits.Rendering;
using PocketOrbits.Saving;
using PocketOrbits.UI;
using UnityEngine;

namespace PocketOrbits.Bootstrap
{
    /// <summary>
    /// Entry point. Creates every service in dependency order (save -> levels -> progression -> art/camera ->
    /// audio -> level manager -> input -> UI) and applies settings. Lives in the Main scene; if a scene without
    /// it is played, it creates itself, so the game always starts correctly.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }

        public GameServices Services { get; private set; }
        public LevelManager Levels { get; private set; }
        public InputController InputController { get; private set; }
        public UIController UI { get; private set; }

        /// <summary>Set by tests to use an isolated save file.</summary>
        public static ISaveStorage StorageOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureExists()
        {
            if (Instance == null && FindObjectOfType<GameBootstrap>() == null)
                new GameObject("PocketOrbits").AddComponent<GameBootstrap>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            UnityEngine.Input.multiTouchEnabled = false;
            Screen.sleepTimeout = SleepTimeout.SystemSetting;

            var save = new SaveService(StorageOverride ?? new FileSaveStorage());
            save.Load();
            if (save.LastLoadStatus == LoadStatus.CorruptReset) Debug.LogWarning("[PocketOrbits] Save file was corrupt; starting fresh.");

            var catalog = LevelLoader.LoadCatalog();
            var services = new GameServices
            {
                Save = save,
                Catalog = catalog,
                Progression = new ProgressionManager(catalog, save),
            };

            SetupLighting();
            services.Art = new ArtLibrary();
            services.Camera = CameraRig.Create();

            var backdropGo = new GameObject("Backdrop");
            backdropGo.transform.SetParent(transform, false);
            services.Backdrop = backdropGo.AddComponent<BackdropView>();

            var fxGo = new GameObject("Effects");
            fxGo.transform.SetParent(transform, false);
            services.Effects = fxGo.AddComponent<EffectsView>();
            services.Effects.Init(services.Art);

            services.Audio = gameObject.AddComponent<AudioManager>();
            services.Audio.Init();
            Services = services;

            var levelsGo = new GameObject("Levels");
            levelsGo.transform.SetParent(transform, false);
            Levels = levelsGo.AddComponent<LevelManager>();
            Levels.Init(services);

            UI = gameObject.AddComponent<UIController>();
            UI.Init(services, Levels);

            InputController = gameObject.AddComponent<InputController>();
            InputController.Init(Levels, services.Camera, UI.IsPointerOverUi);

            GameBootstrapHooks.ApplySettings = ApplySettings;
            services.Camera.Frame(new Core.Simulation.Rect2(-4.5, -8, 4.5, 8), 0, 0);
            ApplySettings();
            UI.GoTo(Screen2.MainMenu);
            PostProcessing.Setup(services.Camera.Camera, services.Settings);
        }

        static void SetupLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.20f, 0.36f);
            var existing = FindObjectOfType<Light>();
            if (existing != null && existing.type == LightType.Directional) return;
            var go = new GameObject("Key Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.95f, 0.88f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            DontDestroyOnLoad(go);
        }

        /// <summary>Re-applies volume, haptics, quality and accessibility settings everywhere.</summary>
        public void ApplySettings()
        {
            var s = Services.Settings;
            Services.Audio.Apply(s.MusicVolume, s.SfxVolume, s.Haptics);
            Services.Effects.SetIntensity(s.EffectsIntensity);
            // Battery saver: 30 fps. Puzzle outcomes are identical at any frame rate (fixed-step simulation).
            Application.targetFrameRate = s.Quality == 0 ? 30 : 60;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = s.Quality == 2 ? 4 : s.Quality == 1 ? 2 : 0;
            var cam = Services.Camera.Camera;
            float viewH = cam.orthographicSize * 2f * 1.6f;
            Services.Backdrop.Build(Services.Art, viewH * Mathf.Max(1f, (float)Screen.width / Mathf.Max(1, Screen.height)) * 1.6f + 20f, viewH + 20f, s.EffectsIntensity);
            Levels.ApplySettings();
            PostProcessing.Apply(s);
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && Services != null) Services.Save.Save();
        }

        void OnApplicationQuit()
        {
            if (Services != null) Services.Save.Save();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                GameBootstrapHooks.ApplySettings = null;
            }
        }
    }
}
