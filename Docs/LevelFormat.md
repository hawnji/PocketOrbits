# Level format

One JSON file per level in `Assets/Game/Data/Resources/Levels/`. `//` line comments are allowed. Coordinates are
world units on the gameplay plane; the default board is 9 x 16 (`bounds` = [-4.5, -8, 4.5, 8]), +y is up.
Run `Tools/format_levels.py` to normalise formatting and `Tools/check-all.sh` (or Pocket Orbits > Validate Levels)
to validate.

```jsonc
{
  "id": "w1-01",              // unique
  "world": 1, "number": 1,    // campaign position (numbers contiguous within a world)
  "name": "First Pull",
  "difficulty": 1,            // 1..5
  "teaches": "…",             // design note
  "objective": "…",           // shown in the HUD
  "bounds": [minX, minY, maxX, maxY],
  "maxMoves": 0,              // 0 = unlimited
  "turnDuration": 0,          // >0: flight pauses every N simulated seconds
  "previewDuration": 8,       // simulated seconds shown by the trajectory preview
  "previewEnabled": true,
  "constants": { "gravity": 1, "softening": 0.35, "timeStep": 0.008333, "maxSpeed": 9, "maxDuration": 40, "outOfBoundsMargin": 0.75 },
  "planets": [ { "id": "p1", "pos": [x, y], "vel": [vx, vy], "radius": 0.35, "style": "terra|ice|lava|gas|moon|ringed" } ],
  "sources": [
    { "id": "sun", "kind": "star", "pos": [0, 1], "strength": 3, "radius": 0.7 },
    { "id": "b1", "kind": "beacon", "pos": [3, -4], "strength": 4, "radius": 0.25,
      "mobility": "movable|fixed|limited-use", "uses": 1, "costsMove": true,
      "regions": [ { "rect": [minX, minY, maxX, maxY] }, { "circle": [x, y, r] } ] },
    { "id": "bh", "kind": "black-hole", "pos": [0, 0], "strength": 8, "radius": 0.45 }
  ],
  "hazards": [ { "id": "a1", "kind": "asteroid", "pos": [x, y], "radius": 0.4 } ],
  "targets": [
    { "id": "t1", "kind": "dock", "pos": [x, y], "radius": 0.8, "captureSpeed": 1.6, "dwell": 0.35, "drag": 4, "order": 0, "planet": "p1" },
    { "id": "ring", "kind": "orbit-ring", "center": "sun", "radius": 2.2, "tolerance": 0.35, "captureSpeed": 0.5, "dwell": 0.6, "drag": 4 },
    { "id": "w1", "kind": "waypoint", "pos": [x, y], "radius": 0.6, "order": 0 }
  ],
  "stars": { "twoStarMoves": 2, "threeStarMoves": 1, "threeStarRequiresNoHint": true },
  "hints": [
    { "text": "Which object matters", "focus": "b1" },
    { "text": "Where to look", "focus": "b1", "area": [x, y, r] },
    { "text": "Full solution", "revealsSolution": true }
  ],
  "solution": { "actions": [ { "type": "place", "beacon": "b1", "pos": [x, y] }, { "type": "launch" } ], "expectedStars": 3 }
}
```

## Validation rules (LevelValidator)

Unique level ids and world/number pairs; required name, objective and difficulty; unique object ids; planets and
fixed bodies inside bounds; known planet styles (asset references); positive radii; movable beacons need regions
and must start inside one; only beacons may move; references (`center`, `planet`, hint `focus`, solution
`beacon`) must exist; orbit tolerance in (0, radius); `0 <= threeStarMoves <= twoStarMoves <= maxMoves`; at least
one hint; a solution that launches; `maxSpeed * timeStep` below the smallest planet radius; no more capturing
targets than planets. Dynamic: the stored solution must complete the level with `expectedStars`, and launching
without any move must not complete it.

## Authoring workflow

```
cd DotNet
dotnet run --project LevelLab -- solve ../Assets/Game/Data/Resources/Levels/w1-07.json 0.1   # grid search
dotnet run --project LevelLab -- trace ../Assets/Game/Data/Resources/Levels/w1-07.json /tmp/t.json
python3 ../Tools/render_trace.py /tmp/t.json /tmp/t.png                                       # picture
```

`solve` reports how many beacon positions solve the level in one move (and pairs for two beacons, or two legs
for turn-based levels) and the most robust position; use it to tune difficulty and pick the stored solution.
