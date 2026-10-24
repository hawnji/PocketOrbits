using System;
using System.Collections.Generic;
using PocketOrbits.Core.Simulation;

namespace PocketOrbits.Core.Levels
{
    /// <summary>Builds the authored initial <see cref="SimState"/> for a level. Every call returns a fresh, complete state.</summary>
    public static class LevelFactory
    {
        public static SimState CreateInitialState(LevelDefinition level)
        {
            var s = new SimState(level.Planets.Count, level.Sources.Count, level.Hazards.Count, level.Targets.Count)
            {
                Bounds = level.Bounds,
                Time = 0,
                StepCount = 0,
            };

            for (int i = 0; i < level.Planets.Count; i++)
            {
                var p = level.Planets[i];
                s.Planets[i] = new PlanetState
                {
                    Id = p.Id,
                    Position = p.Position,
                    Velocity = p.Velocity,
                    Radius = p.Radius,
                    Status = PlanetStatus.Free,
                    CapturedTarget = -1,
                };
            }

            for (int i = 0; i < level.Sources.Count; i++)
            {
                var src = level.Sources[i];
                s.Sources[i] = new SourceState
                {
                    Id = src.Id,
                    Kind = src.Kind,
                    Position = src.Position,
                    Strength = src.Strength,
                    Radius = src.Radius,
                    Mobility = src.Kind == SourceKind.Beacon ? src.Mobility : BeaconMobility.Fixed,
                    CostsMove = src.CostsMove,
                    UsesRemaining = src.Mobility == BeaconMobility.LimitedUse ? src.Uses : int.MaxValue,
                };
            }

            for (int i = 0; i < level.Hazards.Count; i++)
            {
                var h = level.Hazards[i];
                s.Hazards[i] = new HazardState { Id = h.Id, Kind = h.Kind, Position = h.Position, Radius = h.Radius };
            }

            for (int i = 0; i < level.Targets.Count; i++)
            {
                var t = level.Targets[i];
                s.Targets[i] = new TargetState
                {
                    Id = t.Id,
                    Kind = t.Kind,
                    Center = t.Position,
                    CenterSource = string.IsNullOrEmpty(t.CenterSource) ? -1 : IndexOf(level.Sources, t.CenterSource),
                    Radius = t.Radius,
                    Tolerance = t.Tolerance,
                    CaptureSpeed = t.CaptureSpeed,
                    DwellTime = t.DwellTime,
                    Drag = t.Drag,
                    Order = t.Order,
                    RequiredPlanet = string.IsNullOrEmpty(t.Planet) ? -1 : IndexOf(level.Planets, t.Planet),
                    Captured = false,
                    CapturedBy = -1,
                    DwellPlanet = -1,
                    Dwell = 0,
                };
            }
            return s;
        }

        static int IndexOf(List<GravitySourceDefinition> list, string id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return i;
            return -1;
        }

        static int IndexOf(List<PlanetDefinition> list, string id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return i;
            return -1;
        }
    }

    /// <summary>Ordered campaign of levels grouped into worlds.</summary>
    public sealed class LevelCatalog
    {
        public static readonly string[] WorldNames = { "First Light", "Twin Suns", "Shattered Belt", "Gravity Well" };

        readonly List<LevelDefinition> _levels = new List<LevelDefinition>();
        public IReadOnlyList<LevelDefinition> Levels => _levels;

        public LevelCatalog(IEnumerable<LevelDefinition> levels)
        {
            _levels.AddRange(levels);
            _levels.Sort((a, b) => a.World != b.World ? a.World.CompareTo(b.World) : a.Number.CompareTo(b.Number));
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < _levels.Count; i++) if (_levels[i].Id == id) return i;
            return -1;
        }

        public LevelDefinition Get(string id)
        {
            int i = IndexOf(id);
            return i < 0 ? null : _levels[i];
        }

        public LevelDefinition Next(string id)
        {
            int i = IndexOf(id);
            return i >= 0 && i + 1 < _levels.Count ? _levels[i + 1] : null;
        }

        public List<LevelDefinition> InWorld(int world)
        {
            var list = new List<LevelDefinition>();
            foreach (var l in _levels) if (l.World == world) list.Add(l);
            return list;
        }

        public List<int> Worlds()
        {
            var list = new List<int>();
            foreach (var l in _levels) if (!list.Contains(l.World)) list.Add(l.World);
            return list;
        }

        public static string WorldName(int world) => world >= 1 && world <= WorldNames.Length ? WorldNames[world - 1] : "World " + world;
    }
}
