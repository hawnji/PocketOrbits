using System;
using System.Collections.Generic;
using PocketOrbits.Core.Simulation;
using PocketOrbits.Core.Utilities;
using J = PocketOrbits.Core.Utilities.MiniJson;

namespace PocketOrbits.Core.Levels
{
    /// <summary>Reads level definitions from the authored JSON format (see Docs/LevelFormat.md).</summary>
    public static class LevelJson
    {
        public static LevelDefinition Parse(string json, string sourceName = "level")
        {
            try
            {
                return Read(J.Obj(J.Parse(json), sourceName));
            }
            catch (MiniJson.JsonException e)
            {
                throw new FormatException(sourceName + ": " + e.Message, e);
            }
            catch (InvalidCastException e)
            {
                throw new FormatException(sourceName + ": " + e.Message, e);
            }
        }

        static LevelDefinition Read(Dictionary<string, object> d)
        {
            var level = new LevelDefinition
            {
                Id = J.Str(d, "id", null),
                World = J.Int(d, "world", 1),
                Number = J.Int(d, "number", 1),
                Name = J.Str(d, "name", ""),
                Difficulty = J.Int(d, "difficulty", 1),
                Teaches = J.Str(d, "teaches", ""),
                TurnDuration = J.Num(d, "turnDuration", 0),
                PreviewDuration = J.Num(d, "previewDuration", 8),
                MaxMoves = J.Int(d, "maxMoves", 0),
                PreviewEnabled = J.Bool(d, "previewEnabled", true),
            };
            level.Objective.Text = J.Str(d, "objective", "Guide every planet to its target.");

            var b = Numbers(d, "bounds", 4);
            if (b != null) level.Bounds = new Rect2(b[0], b[1], b[2], b[3]);

            if (d.TryGetValue("constants", out object co) && co != null)
            {
                var c = J.Obj(co, "constants");
                var k = level.Constants;
                k.Gravity = J.Num(c, "gravity", k.Gravity);
                k.Softening = J.Num(c, "softening", k.Softening);
                k.TimeStep = J.Num(c, "timeStep", k.TimeStep);
                k.MaxSpeed = J.Num(c, "maxSpeed", k.MaxSpeed);
                k.MaxDuration = J.Num(c, "maxDuration", k.MaxDuration);
                k.OutOfBoundsMargin = J.Num(c, "outOfBoundsMargin", k.OutOfBoundsMargin);
            }

            foreach (var o in J.Arr(d, "planets"))
            {
                var p = J.Obj(o, "planet");
                level.Planets.Add(new PlanetDefinition
                {
                    Id = J.Str(p, "id", null),
                    Position = V(p, "pos"),
                    Velocity = V(p, "vel"),
                    Radius = J.Num(p, "radius", 0.35),
                    Style = J.Str(p, "style", "terra"),
                });
            }

            foreach (var o in J.Arr(d, "sources"))
            {
                var s = J.Obj(o, "source");
                var src = new GravitySourceDefinition
                {
                    Id = J.Str(s, "id", null),
                    Kind = ParseEnum(J.Str(s, "kind", "beacon"), SourceKind.Beacon),
                    Position = V(s, "pos"),
                    Strength = J.Num(s, "strength", 4),
                    Mobility = ParseEnum(J.Str(s, "mobility", "fixed"), BeaconMobility.Fixed),
                    CostsMove = J.Bool(s, "costsMove", true),
                    Uses = J.Int(s, "uses", 1),
                    Style = J.Str(s, "style", ""),
                };
                src.Radius = J.Num(s, "radius", src.Kind == SourceKind.Star ? 0.6 : src.Kind == SourceKind.BlackHole ? 0.45 : 0.25);
                foreach (var ro in J.Arr(s, "regions")) src.Regions.Add(ReadRegion(J.Obj(ro, "region")));
                level.Sources.Add(src);
            }

            foreach (var o in J.Arr(d, "hazards"))
            {
                var h = J.Obj(o, "hazard");
                level.Hazards.Add(new HazardDefinition
                {
                    Id = J.Str(h, "id", null),
                    Kind = ParseEnum(J.Str(h, "kind", "asteroid"), HazardKind.Asteroid),
                    Position = V(h, "pos"),
                    Radius = J.Num(h, "radius", 0.4),
                });
            }

            foreach (var o in J.Arr(d, "targets"))
            {
                var t = J.Obj(o, "target");
                var kind = ParseEnum(J.Str(t, "kind", "dock"), TargetKind.Dock);
                var def = new TargetDefinition
                {
                    Id = J.Str(t, "id", null),
                    Kind = kind,
                    Position = V(t, "pos"),
                    CenterSource = J.Str(t, "center", null),
                    Radius = J.Num(t, "radius", kind == TargetKind.OrbitRing ? 2.0 : kind == TargetKind.Waypoint ? 0.5 : 0.8),
                    Tolerance = J.Num(t, "tolerance", 0.3),
                    CaptureSpeed = J.Num(t, "captureSpeed", kind == TargetKind.OrbitRing ? 0.6 : 1.6),
                    DwellTime = J.Num(t, "dwell", kind == TargetKind.OrbitRing ? 0.6 : 0.35),
                    Drag = J.Num(t, "drag", kind == TargetKind.Waypoint ? 0 : 4.0),
                    Order = J.Int(t, "order", 0),
                    Planet = J.Str(t, "planet", null),
                };
                level.Targets.Add(def);
            }

            if (d.TryGetValue("stars", out object so) && so != null)
            {
                var s = J.Obj(so, "stars");
                level.Stars.TwoStarMoves = J.Int(s, "twoStarMoves", level.Stars.TwoStarMoves);
                level.Stars.ThreeStarMoves = J.Int(s, "threeStarMoves", level.Stars.ThreeStarMoves);
                level.Stars.ThreeStarRequiresNoHint = J.Bool(s, "threeStarRequiresNoHint", true);
            }

            foreach (var o in J.Arr(d, "hints"))
            {
                var h = J.Obj(o, "hint");
                var hint = new HintDefinition
                {
                    Text = J.Str(h, "text", ""),
                    Focus = J.Str(h, "focus", null),
                    RevealsSolution = J.Bool(h, "revealsSolution", false),
                };
                var area = Numbers(h, "area", 3);
                if (area != null)
                {
                    hint.HasArea = true;
                    hint.AreaCenter = new Vec2(area[0], area[1]);
                    hint.AreaRadius = area[2];
                }
                level.Hints.Add(hint);
            }

            if (d.TryGetValue("solution", out object solo) && solo != null)
            {
                var s = J.Obj(solo, "solution");
                level.Solution.ExpectedStars = J.Int(s, "expectedStars", 3);
                level.Solution.Notes = J.Str(s, "notes", "");
                foreach (var ao in J.Arr(s, "actions"))
                {
                    var a = J.Obj(ao, "action");
                    string type = J.Str(a, "type", "place");
                    if (string.Equals(type, "launch", StringComparison.OrdinalIgnoreCase))
                        level.Solution.Actions.Add(new SolutionAction { Type = SolutionActionType.Launch });
                    else
                        level.Solution.Actions.Add(new SolutionAction { Type = SolutionActionType.Place, Beacon = J.Str(a, "beacon", null), Position = V(a, "pos") });
                }
            }
            return level;
        }

        static PlacementRegion ReadRegion(Dictionary<string, object> r)
        {
            var c = Numbers(r, "circle", 3);
            if (c != null) return new PlacementRegion { Shape = RegionShape.Circle, Center = new Vec2(c[0], c[1]), Radius = c[2] };
            var q = Numbers(r, "rect", 4);
            if (q != null) return new PlacementRegion { Shape = RegionShape.Rect, Rect = new Rect2(q[0], q[1], q[2], q[3]) };
            throw new MiniJson.JsonException("region needs 'circle' [x,y,r] or 'rect' [minX,minY,maxX,maxY]");
        }

        static Vec2 V(Dictionary<string, object> d, string key)
        {
            var n = Numbers(d, key, 2);
            return n == null ? Vec2.Zero : new Vec2(n[0], n[1]);
        }

        static double[] Numbers(Dictionary<string, object> d, string key, int count)
        {
            if (!d.TryGetValue(key, out object v) || v == null) return null;
            if (!(v is List<object> list) || list.Count != count) throw new MiniJson.JsonException("'" + key + "' must be an array of " + count + " numbers");
            var result = new double[count];
            for (int i = 0; i < count; i++)
            {
                if (!(list[i] is double n)) throw new MiniJson.JsonException("'" + key + "' must contain numbers");
                result[i] = n;
            }
            return result;
        }

        static T ParseEnum<T>(string value, T fallback) where T : struct
        {
            if (value == null) return fallback;
            string normalized = value.Replace("_", "").Replace("-", "");
            if (Enum.TryParse(normalized, true, out T result)) return result;
            throw new MiniJson.JsonException("Unknown " + typeof(T).Name + " '" + value + "'");
        }
    }
}
