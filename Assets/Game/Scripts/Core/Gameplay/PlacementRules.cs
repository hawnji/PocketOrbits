using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Simulation;

namespace PocketOrbits.Core.Gameplay
{
    public enum PlacementIssue { None = 0, OutsideRegion = 1, OverlapsBody = 2, NotMovable = 3 }

    public struct PlacementResult
    {
        /// <summary>Closest legal position to the requested point (clamped into a placement region).</summary>
        public Vec2 Position;
        public bool Valid;
        public PlacementIssue Issue;
        /// <summary>True when the requested point had to be clamped into a region.</summary>
        public bool Clamped;
    }

    /// <summary>Validates beacon placement: inside an authored region and not overlapping other bodies.</summary>
    public static class PlacementRules
    {
        /// <summary>Extra clearance between a beacon and other bodies.</summary>
        public const double Clearance = 0.15;

        public static PlacementResult Evaluate(LevelDefinition level, SimState s, int sourceIndex, Vec2 desired)
        {
            var def = level.Sources[sourceIndex];
            if (!def.IsMovable) return new PlacementResult { Position = s.Sources[sourceIndex].Position, Valid = false, Issue = PlacementIssue.NotMovable };

            Vec2 pos = desired;
            bool clamped = false;
            if (def.Regions.Count > 0)
            {
                bool inside = false;
                foreach (var r in def.Regions) if (r.Contains(desired)) { inside = true; break; }
                if (!inside)
                {
                    double best = double.MaxValue;
                    foreach (var r in def.Regions)
                    {
                        Vec2 c = r.ClosestPoint(desired);
                        double d = Vec2.DistanceSquared(c, desired);
                        if (d < best) { best = d; pos = c; }
                    }
                    clamped = true;
                }
            }
            else
            {
                pos = level.Bounds.ClosestPoint(desired);
                clamped = pos != desired;
            }

            double r0 = s.Sources[sourceIndex].Radius;
            for (int i = 0; i < s.Planets.Length; i++)
            {
                if (!s.Planets[i].IsAlive) continue;
                double rr = r0 + s.Planets[i].Radius + Clearance;
                if (Vec2.DistanceSquared(pos, s.Planets[i].Position) < rr * rr) return Overlap(pos, clamped);
            }
            for (int i = 0; i < s.Sources.Length; i++)
            {
                if (i == sourceIndex) continue;
                double rr = r0 + s.Sources[i].Radius + Clearance;
                if (Vec2.DistanceSquared(pos, s.Sources[i].Position) < rr * rr) return Overlap(pos, clamped);
            }
            for (int i = 0; i < s.Hazards.Length; i++)
            {
                double rr = r0 + s.Hazards[i].Radius + Clearance;
                if (Vec2.DistanceSquared(pos, s.Hazards[i].Position) < rr * rr) return Overlap(pos, clamped);
            }
            return new PlacementResult { Position = pos, Valid = true, Issue = PlacementIssue.None, Clamped = clamped };
        }

        static PlacementResult Overlap(Vec2 pos, bool clamped) => new PlacementResult { Position = pos, Valid = false, Issue = PlacementIssue.OverlapsBody, Clamped = clamped };
    }
}
