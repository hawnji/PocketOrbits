using System;
using System.Collections.Generic;

namespace PocketOrbits.Core.Simulation
{
    public enum SimEventType
    {
        TargetCaptured = 0,
        PlanetCrashedIntoSource = 1,
        PlanetHitHazard = 2,
        PlanetsCollided = 3,
        PlanetEscaped = 4,
        WaypointReached = 5,
    }

    public struct SimEvent
    {
        public SimEventType Type;
        public int Planet;
        /// <summary>Target, source, hazard or other planet index depending on <see cref="Type"/>.</summary>
        public int Other;
        public double Time;
        public Vec2 Position;

        public override string ToString() => Type + " planet=" + Planet + " other=" + Other + " t=" + Time.ToString("0.###");
    }

    /// <summary>
    /// Deterministic fixed-step gravity simulation on the 2D gameplay plane.
    ///
    /// Update order per step (always identical, for live play and prediction):
    ///   1. Free planets: softened gravity -> semi-implicit Euler velocity update -> target field damping
    ///      -> speed clamp -> position update.
    ///   2. Orbiting planets advance along their captured ring (rail motion).
    ///   3. Collisions: sources, hazards, other planets, then out-of-bounds.
    ///   4. Target capture rules (sequence order respected).
    ///   5. Time advances.
    /// No UnityEngine physics is involved, so frame rate can never change outcomes.
    /// </summary>
    public static class GravitySimulation
    {
        /// <summary>
        /// a = sum_i G * m_i * (s_i - p) / (|s_i - p|^2 + eps^2)^(3/2)
        /// </summary>
        public static Vec2 Acceleration(Vec2 p, SourceState[] sources, double gravity, double softening)
        {
            double ax = 0, ay = 0;
            double eps2 = softening * softening;
            for (int i = 0; i < sources.Length; i++)
            {
                double dx = sources[i].Position.X - p.X;
                double dy = sources[i].Position.Y - p.Y;
                double d2 = dx * dx + dy * dy + eps2;
                double inv = gravity * sources[i].Strength / (d2 * Math.Sqrt(d2));
                ax += dx * inv;
                ay += dy * inv;
            }
            return new Vec2(ax, ay);
        }

        public static bool TargetAccepting(SimState s, int targetIndex)
        {
            var t = s.Targets[targetIndex];
            if (t.Captured) return false;
            for (int i = 0; i < s.Targets.Length; i++)
            {
                if (i == targetIndex) continue;
                if (s.Targets[i].Order < t.Order && !s.Targets[i].Captured) return false;
            }
            return true;
        }

        static bool PlanetAllowed(in TargetState t, int planet) => t.RequiredPlanet < 0 || t.RequiredPlanet == planet;

        /// <summary>Advances the state by exactly one fixed timestep. Events are appended to <paramref name="events"/> if provided.</summary>
        public static void Step(SimState s, SimConstants c, List<SimEvent> events)
        {
            double dt = c.TimeStep;
            var planets = s.Planets;

            // 1. Integrate free planets.
            for (int i = 0; i < planets.Length; i++)
            {
                if (planets[i].Status != PlanetStatus.Free) continue;
                Vec2 p = planets[i].Position;
                Vec2 v = planets[i].Velocity + Acceleration(p, s.Sources, c.Gravity, c.Softening) * dt;
                v = ApplyTargetFields(s, i, p, v, dt);
                v = v.ClampLength(c.MaxSpeed);
                planets[i].Velocity = v;
                planets[i].Position = p + v * dt;
            }

            // 2. Rail motion for planets captured by orbit rings.
            for (int i = 0; i < planets.Length; i++)
            {
                if (planets[i].Status != PlanetStatus.Orbiting) continue;
                int ti = planets[i].CapturedTarget;
                Vec2 center = s.TargetCenter(ti);
                double r = s.Targets[ti].Radius;
                double angle = planets[i].OrbitAngle + planets[i].OrbitAngularSpeed * dt;
                if (angle > Math.PI) angle -= 2 * Math.PI;
                else if (angle < -Math.PI) angle += 2 * Math.PI;
                planets[i].OrbitAngle = angle;
                var radial = new Vec2(Math.Cos(angle), Math.Sin(angle));
                planets[i].Position = center + radial * r;
                planets[i].Velocity = radial.Perpendicular * (r * planets[i].OrbitAngularSpeed);
            }

            // 3. Collisions.
            Rect2 outer = s.Bounds.Expanded(c.OutOfBoundsMargin);
            for (int i = 0; i < planets.Length; i++)
            {
                if (planets[i].Status != PlanetStatus.Free) continue;
                Vec2 p = planets[i].Position;
                double r = planets[i].Radius;
                bool dead = false;

                for (int k = 0; k < s.Sources.Length && !dead; k++)
                {
                    double rr = r + s.Sources[k].Radius;
                    if (Vec2.DistanceSquared(p, s.Sources[k].Position) < rr * rr)
                    {
                        Kill(s, i, PlanetStatus.Destroyed, SimEventType.PlanetCrashedIntoSource, k, events);
                        dead = true;
                    }
                }
                for (int k = 0; k < s.Hazards.Length && !dead; k++)
                {
                    double rr = r + s.Hazards[k].Radius;
                    if (Vec2.DistanceSquared(p, s.Hazards[k].Position) < rr * rr)
                    {
                        Kill(s, i, PlanetStatus.Destroyed, SimEventType.PlanetHitHazard, k, events);
                        dead = true;
                    }
                }
                for (int k = 0; k < planets.Length && !dead; k++)
                {
                    if (k == i || !planets[k].IsAlive) continue;
                    double rr = r + planets[k].Radius;
                    if (Vec2.DistanceSquared(p, planets[k].Position) < rr * rr)
                    {
                        Kill(s, i, PlanetStatus.Destroyed, SimEventType.PlanetsCollided, k, events);
                        Kill(s, k, PlanetStatus.Destroyed, SimEventType.PlanetsCollided, i, events);
                        dead = true;
                    }
                }
                if (!dead && !outer.Contains(p))
                {
                    Kill(s, i, PlanetStatus.Escaped, SimEventType.PlanetEscaped, -1, events);
                }
            }

            // 4. Targets.
            for (int t = 0; t < s.Targets.Length; t++)
            {
                if (!TargetAccepting(s, t)) continue;
                EvaluateTarget(s, t, dt, events);
            }

            // 5. Time.
            s.StepCount++;
            s.Time = s.StepCount * dt;
        }

        static void Kill(SimState s, int i, PlanetStatus status, SimEventType type, int other, List<SimEvent> events)
        {
            if (!s.Planets[i].IsAlive) return;
            s.Planets[i].Status = status;
            s.Planets[i].Velocity = Vec2.Zero;
            s.Planets[i].DeathCause = type;
            s.Planets[i].DeathOther = other;
            events?.Add(new SimEvent { Type = type, Planet = i, Other = other, Time = s.Time, Position = s.Planets[i].Position });
        }

        static Vec2 ApplyTargetFields(SimState s, int planet, Vec2 p, Vec2 v, double dt)
        {
            for (int t = 0; t < s.Targets.Length; t++)
            {
                ref TargetState target = ref s.Targets[t];
                if (target.Drag <= 0 || target.Kind == TargetKind.Waypoint) continue;
                if (!PlanetAllowed(target, planet) || !TargetAccepting(s, t)) continue;
                Vec2 center = s.TargetCenter(t);
                double dist = Vec2.Distance(p, center);
                double damp = Math.Exp(-target.Drag * dt);
                if (target.Kind == TargetKind.Dock)
                {
                    if (dist <= target.Radius) v = v * damp;
                }
                else if (target.Kind == TargetKind.OrbitRing)
                {
                    if (Math.Abs(dist - target.Radius) <= target.Tolerance && dist > 1e-9)
                    {
                        // Damp only the radial component: the ring "circularises" a passing planet.
                        Vec2 n = (p - center) / dist;
                        double vr = Vec2.Dot(v, n);
                        v = v - n * (vr * (1 - damp));
                    }
                }
            }
            return v;
        }

        static void EvaluateTarget(SimState s, int t, double dt, List<SimEvent> events)
        {
            ref TargetState target = ref s.Targets[t];
            Vec2 center = s.TargetCenter(t);
            int best = -1;
            for (int i = 0; i < s.Planets.Length; i++)
            {
                if (s.Planets[i].Status != PlanetStatus.Free || !PlanetAllowed(target, i)) continue;
                Vec2 p = s.Planets[i].Position;
                Vec2 v = s.Planets[i].Velocity;
                double dist = Vec2.Distance(p, center);
                bool ok;
                switch (target.Kind)
                {
                    case TargetKind.Dock:
                        ok = dist <= target.Radius && v.Length <= target.CaptureSpeed;
                        break;
                    case TargetKind.OrbitRing:
                        ok = Math.Abs(dist - target.Radius) <= target.Tolerance && dist > 1e-9 &&
                             Math.Abs(Vec2.Dot(v, (p - center) / dist)) <= target.CaptureSpeed;
                        break;
                    default:
                        ok = dist <= target.Radius + s.Planets[i].Radius;
                        break;
                }
                if (!ok) continue;
                // Prefer continuing the current dwell planet so a second planet cannot steal progress.
                if (best < 0 || i == target.DwellPlanet) best = i;
            }

            if (best < 0)
            {
                target.Dwell = 0;
                target.DwellPlanet = -1;
                return;
            }
            if (best != target.DwellPlanet)
            {
                target.DwellPlanet = best;
                target.Dwell = 0;
            }
            target.Dwell += dt;
            if (target.Kind != TargetKind.Waypoint && target.Dwell + 1e-9 < target.DwellTime) return;

            target.Captured = true;
            target.CapturedBy = best;
            ref PlanetState planet = ref s.Planets[best];
            if (target.Kind == TargetKind.Dock)
            {
                planet.Status = PlanetStatus.Docked;
                planet.CapturedTarget = t;
                planet.Position = center;
                planet.Velocity = Vec2.Zero;
                events?.Add(new SimEvent { Type = SimEventType.TargetCaptured, Planet = best, Other = t, Time = s.Time, Position = center });
            }
            else if (target.Kind == TargetKind.OrbitRing)
            {
                Vec2 rel = planet.Position - center;
                double tangential = Vec2.Cross(rel.Normalized, planet.Velocity);
                double speed = Math.Abs(tangential) < 0.5 ? 0.5 * (tangential < 0 ? -1 : 1) : tangential;
                planet.Status = PlanetStatus.Orbiting;
                planet.CapturedTarget = t;
                planet.OrbitAngle = Math.Atan2(rel.Y, rel.X);
                planet.OrbitAngularSpeed = speed / target.Radius;
                var radial = new Vec2(Math.Cos(planet.OrbitAngle), Math.Sin(planet.OrbitAngle));
                planet.Position = center + radial * target.Radius;
                planet.Velocity = radial.Perpendicular * speed;
                events?.Add(new SimEvent { Type = SimEventType.TargetCaptured, Planet = best, Other = t, Time = s.Time, Position = planet.Position });
            }
            else
            {
                events?.Add(new SimEvent { Type = SimEventType.WaypointReached, Planet = best, Other = t, Time = s.Time, Position = planet.Position });
            }
            target.Dwell = 0;
            target.DwellPlanet = -1;
        }
    }
}
