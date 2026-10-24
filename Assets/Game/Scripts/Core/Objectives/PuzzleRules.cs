using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Simulation;

namespace PocketOrbits.Core.Objectives
{
    public enum OutcomeKind { InProgress = 0, Success = 1, Failure = 2 }

    public enum FailureReason
    {
        None = 0,
        CrashedIntoStar = 1,
        HitAsteroid = 2,
        PlanetsCollided = 3,
        Escaped = 4,
        TimedOut = 5,
        /// <summary>No planet is free to move but targets remain.</summary>
        Stuck = 6,
        FellIntoBlackHole = 7,
    }

    public struct Outcome
    {
        public OutcomeKind Kind;
        public FailureReason Reason;
        /// <summary>Planet involved in the failure, if any.</summary>
        public int Planet;

        public static readonly Outcome InProgress = new Outcome { Kind = OutcomeKind.InProgress, Planet = -1 };
        public static Outcome Success => new Outcome { Kind = OutcomeKind.Success, Planet = -1 };
        public static Outcome Fail(FailureReason r, int planet) => new Outcome { Kind = OutcomeKind.Failure, Reason = r, Planet = planet };
    }

    /// <summary>
    /// Evaluates objectives, captures, collisions and win/lose conditions from the simulation state.
    /// Pure function of state, so live play, prediction and the solution harness agree.
    /// </summary>
    public static class PuzzleRules
    {
        public static Outcome Evaluate(LevelDefinition level, SimState s)
        {
            // Losing a planet is checked first: a planet destroyed on the same step another docks still fails.
            for (int i = 0; i < s.Planets.Length; i++)
            {
                var p = s.Planets[i];
                if (!p.IsAlive) return Outcome.Fail(ReasonFor(p.DeathCause, p.DeathOther, s), i);
            }
            if (s.AllTargetsCaptured) return Outcome.Success;
            if (!s.AnyPlanetFree) return Outcome.Fail(FailureReason.Stuck, -1);
            if (s.StepCount >= level.Constants.MaxSteps) return Outcome.Fail(FailureReason.TimedOut, -1);
            return Outcome.InProgress;
        }

        /// <summary>Maps a destruction event to the failure the player is shown.</summary>
        public static FailureReason ReasonFor(SimEventType type, int other, SimState s)
        {
            switch (type)
            {
                case SimEventType.PlanetCrashedIntoSource:
                    return other >= 0 && other < s.Sources.Length && s.Sources[other].Kind == SourceKind.BlackHole
                        ? FailureReason.FellIntoBlackHole
                        : FailureReason.CrashedIntoStar;
                case SimEventType.PlanetHitHazard: return FailureReason.HitAsteroid;
                case SimEventType.PlanetsCollided: return FailureReason.PlanetsCollided;
                case SimEventType.PlanetEscaped: return FailureReason.Escaped;
                default: return FailureReason.None;
            }
        }

        public static string Describe(FailureReason r)
        {
            switch (r)
            {
                case FailureReason.CrashedIntoStar: return "The planet crashed into a gravity source.";
                case FailureReason.HitAsteroid: return "The planet hit an asteroid.";
                case FailureReason.PlanetsCollided: return "Two planets collided.";
                case FailureReason.Escaped: return "The planet drifted out of the system.";
                case FailureReason.TimedOut: return "The planet never settled into a target.";
                case FailureReason.Stuck: return "No planet is left to reach the remaining targets.";
                case FailureReason.FellIntoBlackHole: return "The planet fell into a black hole.";
                default: return "";
            }
        }

        /// <summary>Short objective progress text, e.g. "Targets 1/2".</summary>
        public static string Progress(SimState s) => "Targets " + s.CapturedTargetCount + "/" + s.Targets.Length;
    }
}
