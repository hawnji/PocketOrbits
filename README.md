# Pocket Orbits

Android-first casual physics puzzle game. Players move gravity beacons and let a deterministic simulation carry
miniature planets into docks, waypoints and orbit rings. Unity + C#, stylized 3D planets under a fixed top-down
orthographic camera, URP when installed.

**Status: first playable milestone + ten-level vertical slice implemented, not yet run inside Unity.** The
environment this was built in had no Unity editor or Android SDK. Everything that can be verified without Unity
was verified; see [Verification status](#verification-status) for exactly what was and was not.

## Open and play

1. Install **Unity 2022.3 LTS or Unity 6 LTS** with **Android Build Support** (Unity Hub > Installs > Add modules).
2. Unity Hub > Add > select this folder. Pick your installed editor version when asked.
3. On first open the editor runs **Pocket Orbits > Set Up Project** automatically. It installs URP, then (after the
   automatic script reload) creates the URP asset, the shared materials, `Assets/Game/Scenes/Main.unity`, and the
   Android player settings. You can re-run it from the menu at any time.
   If Unity says Active Input Handling was changed, restart the editor once.
4. Open `Assets/Game/Scenes/Main.unity` and press Play. Use the Game view in a portrait resolution (e.g. 1080x2400).

Controls: drag a glowing beacon (mouse or touch), watch the dotted trajectory, tap **Confirm**, then **Launch**.
Editor shortcuts: Space launch, Z undo, R reset, Enter confirm, Esc cancel/pause.

## Build for Android

- Menu: **Pocket Orbits > Build Android APK** (output `Builds/Android/PocketOrbits.apk`), or
- Command line:

```
Unity -batchmode -projectPath . -executeMethod PocketOrbits.Editor.ProjectSetup.RunBatch -logFile -   # exit 2 = run again
Unity -batchmode -projectPath . -executeMethod PocketOrbits.Editor.ProjectSetup.RunBatch -logFile -
Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod PocketOrbits.Editor.BuildScript.BuildAndroid -logFile -
```

The build validates every level and replays its stored solution first, and aborts if any check fails.
Settings: portrait, IL2CPP, ARM64 + ARMv7, min API 24, `com.pocketorbits.game`, version 0.1.0 (1).

## Tests

| Suite | Where | Runs in |
|---|---|---|
| EditMode (101 tests): gravity, integration, collisions, capture, session state machine, undo/reset, trajectory-vs-live regression, stars, progression, save data, level data | `Assets/Game/Tests/EditMode` | Unity Test Runner **and** `dotnet test` (no Unity needed) |
| PlayMode (8 tests): boot, start level, drag + preview, complete + unlock + save, failed launch undo, reset, input lock while simulating, restart persistence | `Assets/Game/Tests/PlayMode` | Unity Test Runner only |
| Level validation + solution replay | `Pocket Orbits > Validate Levels`, `DotNet/LevelLab` | Unity or dotnet |

Without Unity: `Tools/check-all.sh` (needs .NET 8 and access to nuget.org) runs the EditMode tests, validates all
levels and compile-checks the Unity code against Unity 2021.3 reference assemblies. CI runs the same script
(`.github/workflows/offline-checks.yml`).

## Project layout

```
Assets/Game/
  Scripts/Core/        Engine-free C# (asmdef with noEngineReferences): the whole game logic
    Simulation/        Vec2, SimState, GravitySimulation (fixed step), TrajectoryPredictor
    Objectives/        PuzzleRules (win/lose evaluation, failure reasons)
    Gameplay/          PuzzleSession (state machine, moves, undo/reset, hints), MoveHistory, PlacementRules
    Levels/            LevelDefinition (+ body/source/target/hazard/objective/star/hint/solution defs),
                       LevelJson, LevelFactory, LevelCatalog, LevelValidator, SolutionRunner
    Progression/       StarEvaluator, ProgressionManager
    Saving/            SaveData (versioned), SaveSerializer, SaveService, ISaveStorage
    Utilities/         MiniJson
  Scripts/Runtime/     Unity layer
    Bootstrap/         GameBootstrap (service creation, settings)
    Gameplay/          LevelManager (session driver, prediction refresh, events -> audio/fx)
    Input/             InputController (touch/mouse select, drag, place)
    Rendering/         ArtLibrary (procedural materials, textures, meshes), LevelView, TrajectoryView,
                       BackdropView, EffectsView, CameraRig, PostProcessing (URP bloom)
    UI/                UIController (all screens, IMGUI), UiSkin
    Audio/             AudioManager (procedural music + SFX, haptics)
    Saving/            FileSaveStorage (atomic local JSON)
    Levels/            LevelLoader (Resources -> validated catalog)
  Scripts/Editor/      ProjectSetup, BuildScript, LevelTools
  Data/Resources/Levels/  Level JSON files (w1-01 ... w1-10)
  Tests/EditMode, Tests/PlayMode
DotNet/                dotnet test project, LevelLab tool, Unity compile check
Docs/                  Architecture, level format, level list, development checklist
Tools/                 check-all.sh, trace renderer, level JSON formatter
```

More: [Docs/Architecture.md](Docs/Architecture.md), [Docs/LevelFormat.md](Docs/LevelFormat.md),
[Docs/Levels.md](Docs/Levels.md), [Docs/DevelopmentChecklist.md](Docs/DevelopmentChecklist.md).

## Verification status

Verified here (Linux, .NET 8, no Unity):
- All 101 EditMode tests pass under `dotnet test`, including preview-vs-live trajectory regression for every
  shipped level (tolerance 1e-9 world units; both use the identical step function).
- All 10 levels pass validation; each stored solution completes the level with the expected 3 stars, and each
  level fails when launched without a move.
- Runtime, editor and PlayMode test code compiles with zero warnings against Unity 2021.3 reference assemblies
  (Android player defines and editor defines).

Not verified (needs Unity and/or a device):
- Opening the project in Unity, scene rendering, materials/shaders, URP setup code (`PO_URP` blocks are excluded
  from the offline compile check), IMGUI layout on real screens.
- PlayMode tests (written and compile-checked, never executed).
- Android build, on-device touch feel, haptics, audio, 60 FPS performance on midrange hardware.

## Known limitations / next steps

- UI is IMGUI (code-only, touch-ready, compile-verifiable). A UGUI/UI Toolkit pass would be the polish step.
- Art is procedural (no model or texture files). Planets, stars, rings, asteroids and backdrop are generated.
- Hint usage persists through Reset within a visit but not after leaving the level.
- Black holes, moving asteroids and orbital gates are data-model ready (`SourceKind.BlackHole`, hazard kinds)
  but no levels use them yet; worlds 2-4 are "coming soon".
- Planets do not attract each other (documented simplification; keeps puzzles readable).
