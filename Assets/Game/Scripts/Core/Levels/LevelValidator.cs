using System;
using System.Collections.Generic;
using PocketOrbits.Core.Gameplay;
using PocketOrbits.Core.Simulation;

namespace PocketOrbits.Core.Levels
{
    public sealed class ValidationReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool Ok => Errors.Count == 0;

        public void Error(string levelId, string msg) => Errors.Add("[" + (levelId ?? "?") + "] " + msg);
        public void Warn(string levelId, string msg) => Warnings.Add("[" + (levelId ?? "?") + "] " + msg);

        public override string ToString() => string.Join("\n", Errors) + (Warnings.Count > 0 ? "\nWarnings:\n" + string.Join("\n", Warnings) : "");
    }

    /// <summary>Known visual style keys; the renderer maps each to a material set. Unknown keys are "missing assets".</summary>
    public static class VisualStyles
    {
        public static readonly string[] Planet = { "terra", "ice", "lava", "gas", "moon", "ringed" };
    }

    /// <summary>
    /// Static checks for authored levels, plus a dynamic check that runs the stored solution through a real
    /// <see cref="PuzzleSession"/> and verifies the result and star count.
    /// </summary>
    public static class LevelValidator
    {
        public static ValidationReport ValidateCatalog(IEnumerable<LevelDefinition> levels, bool runSolutions = true)
        {
            var report = new ValidationReport();
            var ids = new HashSet<string>();
            var numbers = new HashSet<string>();
            foreach (var level in levels)
            {
                if (level.Id != null && !ids.Add(level.Id)) report.Error(level.Id, "duplicate level id");
                if (!numbers.Add(level.World + "-" + level.Number)) report.Error(level.Id, "duplicate world/number " + level.DisplayNumber);
                Validate(level, report);
                if (runSolutions && report.Errors.Count == 0) VerifySolution(level, report);
            }
            return report;
        }

        public static ValidationReport Validate(LevelDefinition level, ValidationReport report = null)
        {
            report = report ?? new ValidationReport();
            string id = level.Id;
            if (string.IsNullOrWhiteSpace(id)) report.Error(id, "missing id");
            if (level.World < 1 || level.Number < 1) report.Error(id, "world and number must be >= 1");
            if (level.Difficulty < 1 || level.Difficulty > 5) report.Error(id, "difficulty must be 1..5");
            if (string.IsNullOrWhiteSpace(level.Name)) report.Error(id, "missing name");
            if (string.IsNullOrWhiteSpace(level.Objective.Text)) report.Error(id, "missing objective text");
            if (level.Bounds.Width <= 0 || level.Bounds.Height <= 0) report.Error(id, "invalid bounds");

            var c = level.Constants;
            if (c.TimeStep <= 0 || c.TimeStep > 0.05) report.Error(id, "timeStep must be in (0, 0.05]");
            if (c.Softening <= 0) report.Error(id, "softening must be > 0");
            if (c.MaxSpeed <= 0) report.Error(id, "maxSpeed must be > 0");
            if (c.MaxDuration <= 0) report.Error(id, "maxDuration must be > 0");
            if (level.PreviewDuration < 0) report.Error(id, "previewDuration must be >= 0");
            if (level.TurnDuration < 0) report.Error(id, "turnDuration must be >= 0");

            var objectIds = new HashSet<string>();
            void CheckId(string objId, string what)
            {
                if (string.IsNullOrWhiteSpace(objId)) report.Error(id, what + " without id");
                else if (!objectIds.Add(objId)) report.Error(id, "duplicate object id '" + objId + "'");
            }

            if (level.Planets.Count == 0) report.Error(id, "needs at least one planet");
            double minPlanetRadius = double.MaxValue;
            foreach (var p in level.Planets)
            {
                CheckId(p.Id, "planet");
                if (p.Radius <= 0) report.Error(id, "planet " + p.Id + " radius must be > 0");
                minPlanetRadius = Math.Min(minPlanetRadius, p.Radius);
                if (!level.Bounds.Contains(p.Position)) report.Error(id, "planet " + p.Id + " starts outside bounds");
                if (Array.IndexOf(VisualStyles.Planet, p.Style) < 0) report.Error(id, "planet " + p.Id + " has unknown style '" + p.Style + "' (missing asset)");
                if (p.Velocity.Length > c.MaxSpeed) report.Warn(id, "planet " + p.Id + " starts faster than maxSpeed");
            }
            // Tunnelling guard: a planet may not move further than its radius in one step.
            if (level.Planets.Count > 0 && c.MaxSpeed * c.TimeStep >= minPlanetRadius)
                report.Error(id, "maxSpeed * timeStep must be smaller than the smallest planet radius");

            int movable = 0;
            foreach (var s in level.Sources)
            {
                CheckId(s.Id, "source");
                if (s.Radius <= 0) report.Error(id, "source " + s.Id + " radius must be > 0");
                if (s.Strength < 0) report.Error(id, "source " + s.Id + " strength must be >= 0");
                if (!level.Bounds.Contains(s.Position)) report.Error(id, "source " + s.Id + " outside bounds");
                if (s.Kind != SourceKind.Beacon && s.Mobility != BeaconMobility.Fixed) report.Error(id, "only beacons can be movable (" + s.Id + ")");
                if (s.IsMovable)
                {
                    movable++;
                    if (s.Regions.Count == 0) report.Error(id, "movable beacon " + s.Id + " needs a placement region");
                    bool inside = false;
                    foreach (var r in s.Regions)
                    {
                        if (r.Shape == RegionShape.Circle && r.Radius <= 0) report.Error(id, "beacon " + s.Id + " has an empty circle region");
                        if (r.Shape == RegionShape.Rect && (r.Rect.Width <= 0 || r.Rect.Height <= 0)) report.Error(id, "beacon " + s.Id + " has an empty rect region");
                        if (r.Contains(s.Position)) inside = true;
                    }
                    if (!inside && s.Regions.Count > 0) report.Error(id, "beacon " + s.Id + " starts outside its placement regions");
                    if (s.Mobility == BeaconMobility.LimitedUse && s.Uses < 1) report.Error(id, "limited-use beacon " + s.Id + " needs uses >= 1");
                }
            }
            if (movable == 0) report.Error(id, "needs at least one movable beacon (the player's only action)");

            foreach (var h in level.Hazards)
            {
                CheckId(h.Id, "hazard");
                if (h.Radius <= 0) report.Error(id, "hazard " + h.Id + " radius must be > 0");
            }

            if (level.Targets.Count == 0) report.Error(id, "needs at least one target");
            foreach (var t in level.Targets)
            {
                CheckId(t.Id, "target");
                if (t.Radius <= 0) report.Error(id, "target " + t.Id + " radius must be > 0");
                if (t.DwellTime < 0) report.Error(id, "target " + t.Id + " dwell must be >= 0");
                if (t.Drag < 0) report.Error(id, "target " + t.Id + " drag must be >= 0");
                if (t.Kind != TargetKind.Waypoint && t.CaptureSpeed <= 0) report.Error(id, "target " + t.Id + " captureSpeed must be > 0");
                if (t.Kind == TargetKind.OrbitRing && (t.Tolerance <= 0 || t.Tolerance >= t.Radius)) report.Error(id, "orbit ring " + t.Id + " tolerance must be in (0, radius)");
                if (!string.IsNullOrEmpty(t.CenterSource) && level.Sources.Find(s => s.Id == t.CenterSource) == null) report.Error(id, "target " + t.Id + " references unknown centre '" + t.CenterSource + "'");
                if (string.IsNullOrEmpty(t.CenterSource) && !level.Bounds.Contains(t.Position)) report.Error(id, "target " + t.Id + " outside bounds");
                if (!string.IsNullOrEmpty(t.Planet) && level.Planets.Find(p => p.Id == t.Planet) == null) report.Error(id, "target " + t.Id + " references unknown planet '" + t.Planet + "'");
            }
            int docks = 0;
            foreach (var t in level.Targets) if (t.Kind != TargetKind.Waypoint) docks++;
            if (docks > level.Planets.Count) report.Error(id, "more capturing targets than planets; level cannot be completed");

            var st = level.Stars;
            if (st.ThreeStarMoves < 0 || st.TwoStarMoves < st.ThreeStarMoves) report.Error(id, "star criteria must satisfy 0 <= threeStarMoves <= twoStarMoves");
            if (level.MaxMoves > 0 && st.TwoStarMoves > level.MaxMoves) report.Error(id, "twoStarMoves exceeds maxMoves");
            if (level.MaxMoves > 0 && level.MaxMoves < st.ThreeStarMoves) report.Error(id, "maxMoves below threeStarMoves");

            if (level.Hints.Count == 0) report.Error(id, "needs at least one hint");
            foreach (var h in level.Hints)
            {
                if (string.IsNullOrWhiteSpace(h.Text)) report.Error(id, "hint without text");
                if (!string.IsNullOrEmpty(h.Focus) && !objectIds.Contains(h.Focus)) report.Error(id, "hint focuses unknown object '" + h.Focus + "'");
            }

            if (level.Solution.Actions.Count == 0) report.Error(id, "missing solution metadata");
            bool hasLaunch = false;
            foreach (var a in level.Solution.Actions)
            {
                if (a.Type == SolutionActionType.Launch) { hasLaunch = true; continue; }
                var src = level.Sources.Find(s => s.Id == a.Beacon);
                if (src == null) report.Error(id, "solution moves unknown beacon '" + a.Beacon + "'");
                else if (!src.IsMovable) report.Error(id, "solution moves fixed source '" + a.Beacon + "'");
            }
            if (level.Solution.Actions.Count > 0 && !hasLaunch) report.Error(id, "solution never launches");
            if (level.Solution.ExpectedStars < 1 || level.Solution.ExpectedStars > 3) report.Error(id, "expectedStars must be 1..3");
            return report;
        }

        /// <summary>Runs the authored solution and a "do nothing" attempt through the real session.</summary>
        public static void VerifySolution(LevelDefinition level, ValidationReport report)
        {
            var run = SolutionRunner.Run(level, level.Solution.Actions);
            if (!run.Completed)
                report.Error(level.Id, "stored solution does not complete the level (" + run.Failure + (run.Error != null ? ": " + run.Error : "") + ")");
            else if (run.Stars != level.Solution.ExpectedStars)
                report.Error(level.Id, "stored solution earns " + run.Stars + " stars, expected " + level.Solution.ExpectedStars);

            var idle = SolutionRunner.Run(level, new List<SolutionAction> { new SolutionAction { Type = SolutionActionType.Launch } });
            if (idle.Completed) report.Error(level.Id, "level is solved without moving any beacon");
        }
    }

    public struct SolutionRunResult
    {
        public bool Completed;
        public int Stars;
        public int Moves;
        public Objectives.FailureReason Failure;
        public string Error;
        public double SimTime;
        public int TargetsCaptured;
    }

    /// <summary>Plays a list of actions through a real <see cref="PuzzleSession"/> (level test harness).</summary>
    public static class SolutionRunner
    {
        public static SolutionRunResult Run(LevelDefinition level, IList<SolutionAction> actions)
        {
            var session = new PuzzleSession(level);
            var result = new SolutionRunResult();
            foreach (var a in actions)
            {
                if (session.Current == GameState.LevelCompleted || session.Current == GameState.LevelFailed) break;
                if (a.Type == SolutionActionType.Launch)
                {
                    if (!session.Launch()) { result.Error = "launch rejected in state " + session.Current; break; }
                    session.RunToEnd();
                    continue;
                }
                int src = session.State.FindSource(a.Beacon);
                var sel = session.Select(src);
                if (sel != SelectResult.Selected) { result.Error = "cannot select " + a.Beacon + ": " + sel; break; }
                var placement = session.Drag(a.Position);
                if (!placement.Valid || placement.Clamped) { result.Error = "illegal placement for " + a.Beacon + " at " + a.Position + " (" + placement.Issue + (placement.Clamped ? ", clamped" : "") + ")"; session.Cancel(); break; }
                if (!session.Confirm()) { result.Error = "confirm rejected for " + a.Beacon; break; }
            }
            result.Completed = session.Current == GameState.LevelCompleted;
            result.Stars = session.Stars;
            result.Moves = session.Moves;
            result.Failure = session.LastOutcome.Reason;
            result.SimTime = session.State.Time;
            result.TargetsCaptured = session.State.CapturedTargetCount;
            return result;
        }
    }
}
