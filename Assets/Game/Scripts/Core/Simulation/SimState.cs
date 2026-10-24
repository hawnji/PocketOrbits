using System;

namespace PocketOrbits.Core.Simulation
{
    /// <summary>Tunable constants shared by the live simulation and the trajectory predictor.</summary>
    [Serializable]
    public sealed class SimConstants
    {
        /// <summary>Gravity constant G.</summary>
        public double Gravity = 1.0;
        /// <summary>Softening length epsilon; prevents singular forces near sources.</summary>
        public double Softening = 0.35;
        /// <summary>Fixed timestep in simulation seconds. Never derived from frame time.</summary>
        public double TimeStep = 1.0 / 120.0;
        /// <summary>Planet speed limit (units / second). Also guarantees no tunnelling.</summary>
        public double MaxSpeed = 9.0;
        /// <summary>Hard cap on simulated time for a whole level attempt.</summary>
        public double MaxDuration = 40.0;
        /// <summary>Planets further than this outside the level bounds have escaped.</summary>
        public double OutOfBoundsMargin = 0.75;

        public SimConstants Clone() => (SimConstants)MemberwiseClone();

        public int MaxSteps => (int)Math.Ceiling(MaxDuration / TimeStep);
    }

    public enum SourceKind { Star = 0, Beacon = 1, BlackHole = 2 }

    public enum BeaconMobility { Fixed = 0, Movable = 1, LimitedUse = 2 }

    public enum PlanetStatus { Free = 0, Docked = 1, Orbiting = 2, Destroyed = 3, Escaped = 4 }

    public enum HazardKind { Asteroid = 0 }

    public enum TargetKind
    {
        /// <summary>Docking zone: planet must slow below a speed threshold inside the radius for a dwell time.</summary>
        Dock = 0,
        /// <summary>Orbital ring around a centre: planet must stay inside the band with low radial speed.</summary>
        OrbitRing = 1,
        /// <summary>Collection point / waypoint: touched once, planet keeps flying.</summary>
        Waypoint = 2,
    }

    public struct PlanetState
    {
        public string Id;
        public Vec2 Position;
        public Vec2 Velocity;
        public double Radius;
        public PlanetStatus Status;
        /// <summary>Index of the target that captured this planet (-1 if none).</summary>
        public int CapturedTarget;
        /// <summary>Rail motion parameters while Orbiting.</summary>
        public double OrbitAngle;
        public double OrbitAngularSpeed;
        /// <summary>What destroyed the planet (valid when Status is Destroyed or Escaped).</summary>
        public SimEventType DeathCause;
        /// <summary>Index of the source / hazard / planet involved in the destruction.</summary>
        public int DeathOther;

        public bool IsSimulated => Status == PlanetStatus.Free;
        public bool IsAlive => Status != PlanetStatus.Destroyed && Status != PlanetStatus.Escaped;
    }

    public struct SourceState
    {
        public string Id;
        public SourceKind Kind;
        public Vec2 Position;
        /// <summary>Gravitational strength m_i.</summary>
        public double Strength;
        /// <summary>Solid body radius; planets touching it are destroyed (event horizon for black holes).</summary>
        public double Radius;
        public BeaconMobility Mobility;
        public bool CostsMove;
        /// <summary>For LimitedUse beacons: remaining repositions.</summary>
        public int UsesRemaining;
    }

    public struct HazardState
    {
        public string Id;
        public HazardKind Kind;
        public Vec2 Position;
        public double Radius;
    }

    public struct TargetState
    {
        public string Id;
        public TargetKind Kind;
        /// <summary>Fixed centre (used when CenterSource &lt; 0).</summary>
        public Vec2 Center;
        /// <summary>Optional source index the target is anchored to (orbit rings around a star or beacon).</summary>
        public int CenterSource;
        /// <summary>Dock / waypoint radius, or orbit ring radius.</summary>
        public double Radius;
        /// <summary>Orbit ring half band width.</summary>
        public double Tolerance;
        /// <summary>Dock: max speed; orbit: max radial speed.</summary>
        public double CaptureSpeed;
        /// <summary>Required continuous time meeting the capture condition.</summary>
        public double DwellTime;
        /// <summary>Damping rate (1/s) applied while a planet is inside an accepting zone.</summary>
        public double Drag;
        /// <summary>Sequence order; a target accepts only when every lower order target is captured.</summary>
        public int Order;
        /// <summary>Optional planet index restriction (-1 = any planet).</summary>
        public int RequiredPlanet;

        // Runtime progress.
        public bool Captured;
        public int CapturedBy;
        public int DwellPlanet;
        public double Dwell;
    }

    /// <summary>
    /// Complete mutable gameplay state. Arrays of structs keep copies cheap and allocation free,
    /// which matters for undo snapshots and trajectory prediction.
    /// </summary>
    public sealed class SimState
    {
        public PlanetState[] Planets;
        public SourceState[] Sources;
        public HazardState[] Hazards;
        public TargetState[] Targets;
        public Rect2 Bounds;
        public double Time;
        public int StepCount;

        public SimState(int planets, int sources, int hazards, int targets)
        {
            Planets = new PlanetState[planets];
            Sources = new SourceState[sources];
            Hazards = new HazardState[hazards];
            Targets = new TargetState[targets];
        }

        public SimState Clone()
        {
            var copy = new SimState(Planets.Length, Sources.Length, Hazards.Length, Targets.Length);
            copy.CopyFrom(this);
            return copy;
        }

        /// <summary>Copies every field from <paramref name="other"/>. Arrays are reused when sizes match.</summary>
        public void CopyFrom(SimState other)
        {
            if (Planets.Length != other.Planets.Length) Planets = new PlanetState[other.Planets.Length];
            if (Sources.Length != other.Sources.Length) Sources = new SourceState[other.Sources.Length];
            if (Hazards.Length != other.Hazards.Length) Hazards = new HazardState[other.Hazards.Length];
            if (Targets.Length != other.Targets.Length) Targets = new TargetState[other.Targets.Length];
            Array.Copy(other.Planets, Planets, Planets.Length);
            Array.Copy(other.Sources, Sources, Sources.Length);
            Array.Copy(other.Hazards, Hazards, Hazards.Length);
            Array.Copy(other.Targets, Targets, Targets.Length);
            Bounds = other.Bounds;
            Time = other.Time;
            StepCount = other.StepCount;
        }

        public int FindPlanet(string id)
        {
            for (int i = 0; i < Planets.Length; i++) if (Planets[i].Id == id) return i;
            return -1;
        }

        public int FindSource(string id)
        {
            for (int i = 0; i < Sources.Length; i++) if (Sources[i].Id == id) return i;
            return -1;
        }

        public int FindTarget(string id)
        {
            for (int i = 0; i < Targets.Length; i++) if (Targets[i].Id == id) return i;
            return -1;
        }

        public Vec2 TargetCenter(int targetIndex)
        {
            var t = Targets[targetIndex];
            return t.CenterSource >= 0 ? Sources[t.CenterSource].Position : t.Center;
        }

        public bool AllTargetsCaptured
        {
            get
            {
                for (int i = 0; i < Targets.Length; i++) if (!Targets[i].Captured) return false;
                return true;
            }
        }

        public int CapturedTargetCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Targets.Length; i++) if (Targets[i].Captured) n++;
                return n;
            }
        }

        public bool AnyPlanetFree
        {
            get
            {
                for (int i = 0; i < Planets.Length; i++) if (Planets[i].Status == PlanetStatus.Free) return true;
                return false;
            }
        }

        /// <summary>True when every field that influences future simulation matches (used by tests).</summary>
        public bool StateEquals(SimState o)
        {
            if (o.Planets.Length != Planets.Length || o.Sources.Length != Sources.Length || o.Targets.Length != Targets.Length || o.Hazards.Length != Hazards.Length) return false;
            if (Time != o.Time || StepCount != o.StepCount) return false;
            for (int i = 0; i < Planets.Length; i++)
            {
                var a = Planets[i]; var b = o.Planets[i];
                if (a.Position != b.Position || a.Velocity != b.Velocity || a.Status != b.Status || a.CapturedTarget != b.CapturedTarget || a.OrbitAngle != b.OrbitAngle || a.OrbitAngularSpeed != b.OrbitAngularSpeed) return false;
            }
            for (int i = 0; i < Sources.Length; i++)
            {
                if (Sources[i].Position != o.Sources[i].Position || Sources[i].UsesRemaining != o.Sources[i].UsesRemaining) return false;
            }
            for (int i = 0; i < Hazards.Length; i++)
            {
                if (Hazards[i].Position != o.Hazards[i].Position) return false;
            }
            for (int i = 0; i < Targets.Length; i++)
            {
                var a = Targets[i]; var b = o.Targets[i];
                if (a.Captured != b.Captured || a.CapturedBy != b.CapturedBy || a.Dwell != b.Dwell || a.DwellPlanet != b.DwellPlanet) return false;
            }
            return true;
        }
    }
}
