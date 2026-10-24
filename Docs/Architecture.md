# Architecture

## Layers

```
            +--------------------------- Unity layer (PocketOrbits.Runtime) ---------------------------+
 touch ---> | InputController --> LevelManager --> PuzzleSession (core)      UIController (IMGUI)       |
            |                         |   ^ events                              | calls LevelManager only |
            |                         v   |                                     v                         |
            |                LevelView / TrajectoryView / EffectsView / AudioManager (read-only views)    |
            +------------------------------------------------------------------------------------------+
            +---------------------------- Core (PocketOrbits.Core, no UnityEngine) --------------------+
            | PuzzleSession -> GravitySimulation.Step, PuzzleRules, MoveHistory, PlacementRules        |
            | TrajectoryPredictor (same Step on a scratch copy)                                         |
            | LevelDefinition/LevelJson/LevelValidator/SolutionRunner, StarEvaluator, ProgressionManager |
            | SaveData/SaveSerializer/SaveService (ISaveStorage)                                        |
            +------------------------------------------------------------------------------------------+
```

The core assembly has `noEngineReferences: true`, so it cannot accidentally depend on Unity. That is what lets
the same EditMode tests run in Unity and in `dotnet test`.

## Simulation

- 2D plane, double precision. Bodies: planets (moving), sources (star, beacon, black hole), hazards (asteroids),
  targets (dock, orbit ring, waypoint).
- Acceleration: `a = sum_i G m_i (s_i - p) / (|s_i - p|^2 + eps^2)^(3/2)` (`GravitySimulation.Acceleration`).
- Fixed timestep (default 1/120 s), semi-implicit Euler. Tested: circular orbit radius stays within 3% over five
  orbits; determinism (bit-identical repeated runs).
- Fixed update order per step: integrate free planets (gravity, target-field damping, speed clamp), advance
  planets on orbit rails, collisions (sources, hazards, planets, out of bounds), target capture, time.
- Speed clamp (`MaxSpeed`) plus a validator rule `MaxSpeed * dt < smallest planet radius` prevents tunnelling.
- Planets do not attract each other.
- Frame rate independence: `PuzzleSession.Advance` accumulates real time and runs whole fixed steps (max 240 per
  frame). Outcome depends only on step count; tested with irregular frame times.

### Capture rules

| Target | Field while accepting | Capture condition | After capture |
|---|---|---|---|
| Dock | velocity damping (`drag`) inside radius | inside radius and speed <= `captureSpeed` continuously for `dwell` s | planet locked at dock centre |
| Orbit ring | damps radial velocity inside band | inside `radius ± tolerance`, radial speed <= `captureSpeed` for `dwell` s | planet moves on rails along the ring |
| Waypoint | none | touches radius | planet keeps flying |

A fast planet crossing a dock does not win: it must slow below the threshold and stay for the dwell window
(`FastPlanetCrossingDockIsNotCaptured`, `BriefSlowGrazeDoesNotCaptureWithoutDwell`).
Targets with a higher `order` ignore planets until all lower orders are captured. A target can be restricted to a
planet id.

### Outcome

`PuzzleRules.Evaluate`: any destroyed/escaped planet = failure (reason from the death cause); all targets captured =
success; no free planet left = stuck; `MaxDuration` exceeded = timed out.

## Session state machine (`PuzzleSession`)

States: Initializing, ReadyForInput, SelectingObject, PreviewingMove, Simulating, LevelCompleted, LevelFailed,
Paused. Valid inputs per state are listed on the class. Beacon edits are only accepted in ReadyForInput, so nothing
can move while a launch resolves.

- Moves: confirming a placement costs a move if the beacon's `costsMove` is true. Limited-use beacons count down.
  `maxMoves` blocks further paid placements.
- Undo: `MoveHistory` stores complete `SimState` snapshots (positions, velocities, beacon positions and uses, target
  capture and dwell progress, hazards, time) plus move count, before every confirm and every launch. Undo during a
  launch aborts it.
- Reset: rebuilds the authored state with `LevelFactory`, clears history and moves.
- Turn-based levels (`turnDuration > 0`) return to ReadyForInput after each leg with velocities kept.
- Hints: tiers revealed in order; any reveal marks the attempt as hinted (no third star when the level requires
  a hint-free solve). Kept through Reset within one visit.

## Trajectory preview

`TrajectoryPredictor` copies the live state into a reusable scratch state, applies the pending beacon position,
and runs the identical `GravitySimulation.Step` until capture, crash, escape or the preview duration. It records a
sample every N steps into reused buffers. The live state is never touched (tested). Because the code path is
identical, preview and live positions match to 1e-9 (regression tests over every shipped level). The Unity layer
refreshes it at most every 50 ms and only when something changed; it can be switched off in settings.

## Data

Levels are JSON (`Assets/Game/Data/Resources/Levels`, format in `LevelFormat.md`) with separate definition types for
level, planet, gravity source, placement region, hazard, target, objective, star criteria, hint and solution.
JSON was chosen over ScriptableObjects so the same files are read by Unity, the dotnet tests and the LevelLab tool,
and diffs stay reviewable. The loader validates every level at startup and skips (and logs) invalid ones.

## Progression and saving

- Linear unlocks; any completion unlocks the next level; stars never decrease (`ProgressionManager`).
- `SaveData` is versioned JSON. Loading never throws: missing file = fresh save, corrupt or newer-version data =
  fresh save plus a `.corrupt-<time>` backup. Values are clamped on load.
- `FileSaveStorage` writes `pocket_orbits_save.json` in `Application.persistentDataPath` via temp file + rename
  (previous save kept as `.bak`). Saves happen on completion, failure, settings changes, app pause and quit.

## Rendering

Fixed orthographic camera looking straight down at the XZ plane; `CameraRig` fits the level bounds between the HUD
bars and the device safe area. All art is procedural and uses four shared materials (lit, unlit, transparent,
additive) with per-renderer `MaterialPropertyBlock` colours. Planets get generated equirectangular textures per
style; stars have unlit cores and additive halos; beacons have a field ring and a segmented grab handle; targets
use distinct shapes (dock = ring + inner hexagon, waypoint = diamond, orbit = double band) so they never rely on
colour alone. High-contrast mode thickens outlines and switches to yellow/white. Particles and trails are cosmetic
and scaled by the effects-intensity setting. Bloom/vignette come from a URP Volume when URP is present.

## Performance notes

- No allocations in the simulation step; snapshots and prediction buffers are pooled/reused.
- Prediction: ~720 steps for a 6 s preview, a few microseconds each; throttled to 20 Hz while dragging.
- No real-time shadows; one directional light; background is 4 draw calls; starfield is one mesh.
- Battery saver quality: 30 FPS cap, sparser preview sampling, no bloom. Outcomes are identical at any frame rate.
- Not yet profiled on hardware.
