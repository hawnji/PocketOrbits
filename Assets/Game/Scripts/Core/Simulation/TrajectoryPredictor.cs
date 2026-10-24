using System.Collections.Generic;

namespace PocketOrbits.Core.Simulation
{
    public enum PredictionEnd
    {
        Duration = 0,
        AllTargetsCaptured = 1,
        PlanetDestroyed = 2,
        PlanetEscaped = 3,
        NoFreePlanets = 4,
    }

    /// <summary>Result buffers are owned by the predictor and reused between calls.</summary>
    public sealed class TrajectoryPrediction
    {
        /// <summary>Sampled positions per planet, starting with the current position.</summary>
        public readonly List<List<Vec2>> Paths = new List<List<Vec2>>();
        /// <summary>Simulation time of each sample (shared across planets).</summary>
        public readonly List<double> SampleTimes = new List<double>();
        public readonly List<SimEvent> Events = new List<SimEvent>();
        public PredictionEnd End;
        public double EndTime;
        public int StepsSimulated;

        internal void Reset(int planets)
        {
            while (Paths.Count < planets) Paths.Add(new List<Vec2>(256));
            while (Paths.Count > planets) Paths.RemoveAt(Paths.Count - 1);
            for (int i = 0; i < Paths.Count; i++) Paths[i].Clear();
            SampleTimes.Clear();
            Events.Clear();
            End = PredictionEnd.Duration;
            EndTime = 0;
            StepsSimulated = 0;
        }
    }

    /// <summary>
    /// Predicts trajectories by running the exact same <see cref="GravitySimulation.Step"/> on an isolated copy
    /// of the state. Because integrator, constants, collision and capture rules are shared, the preview matches
    /// the live run step-for-step over the preview window (verified by regression tests).
    /// The live state is never modified.
    /// </summary>
    public sealed class TrajectoryPredictor
    {
        readonly SimState _scratch = new SimState(0, 0, 0, 0);
        readonly List<SimEvent> _stepEvents = new List<SimEvent>(8);
        public readonly TrajectoryPrediction Result = new TrajectoryPrediction();

        /// <summary>Record one sample every N steps.</summary>
        public int SampleEvery = 4;

        /// <param name="live">State to predict from (read only).</param>
        /// <param name="sourceOverride">Index of a source whose position is replaced (e.g. the beacon being dragged), or -1.</param>
        /// <param name="overridePosition">Proposed position for that source.</param>
        /// <param name="duration">Maximum simulated preview time in seconds.</param>
        public TrajectoryPrediction Predict(SimState live, SimConstants c, int sourceOverride, Vec2 overridePosition, double duration)
        {
            _scratch.CopyFrom(live);
            if (sourceOverride >= 0 && sourceOverride < _scratch.Sources.Length)
                _scratch.Sources[sourceOverride].Position = overridePosition;

            var r = Result;
            r.Reset(_scratch.Planets.Length);
            Sample(r);

            int steps = (int)System.Math.Ceiling(duration / c.TimeStep);
            int every = SampleEvery < 1 ? 1 : SampleEvery;
            for (int n = 1; n <= steps; n++)
            {
                _stepEvents.Clear();
                GravitySimulation.Step(_scratch, c, _stepEvents);
                r.StepsSimulated = n;
                r.Events.AddRange(_stepEvents);

                PredictionEnd? end = null;
                for (int e = 0; e < _stepEvents.Count; e++)
                {
                    var t = _stepEvents[e].Type;
                    if (t == SimEventType.PlanetEscaped) end = PredictionEnd.PlanetEscaped;
                    else if (t == SimEventType.PlanetCrashedIntoSource || t == SimEventType.PlanetHitHazard || t == SimEventType.PlanetsCollided) end = PredictionEnd.PlanetDestroyed;
                }
                if (end == null && _scratch.AllTargetsCaptured) end = PredictionEnd.AllTargetsCaptured;
                if (end == null && !_scratch.AnyPlanetFree) end = PredictionEnd.NoFreePlanets;

                if (end != null || n % every == 0 || n == steps) Sample(r);
                if (end != null)
                {
                    r.End = end.Value;
                    break;
                }
            }
            r.EndTime = _scratch.Time - live.Time;
            return r;
        }

        void Sample(TrajectoryPrediction r)
        {
            r.SampleTimes.Add(_scratch.Time);
            for (int i = 0; i < _scratch.Planets.Length; i++)
                r.Paths[i].Add(_scratch.Planets[i].Position);
        }
    }
}
