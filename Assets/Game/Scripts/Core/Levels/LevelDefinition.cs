using System.Collections.Generic;
using PocketOrbits.Core.Simulation;

namespace PocketOrbits.Core.Levels
{
    // Authored, immutable-by-convention level data. Each concept has its own small definition type;
    // LevelFactory turns them into a fresh SimState, which is how reset restores the authored state.

    public sealed class LevelDefinition
    {
        public string Id;
        public int World;
        public int Number;
        public string Name;
        /// <summary>1 (gentle) .. 5 (expert).</summary>
        public int Difficulty = 1;
        /// <summary>Short note on what the level teaches (design documentation).</summary>
        public string Teaches = "";
        public Rect2 Bounds = new Rect2(-4.5, -8, 4.5, 8);
        public SimConstants Constants = new SimConstants();
        /// <summary>Simulated seconds per launch before control returns to the player; 0 = run until resolved.</summary>
        public double TurnDuration;
        /// <summary>Simulated seconds shown by the trajectory preview.</summary>
        public double PreviewDuration = 8;
        /// <summary>Whether this level offers a trajectory preview at all (the player can also disable it).</summary>
        public bool PreviewEnabled = true;
        /// <summary>Hard move limit; 0 means unlimited.</summary>
        public int MaxMoves;

        public ObjectiveDefinition Objective = new ObjectiveDefinition();
        public readonly List<PlanetDefinition> Planets = new List<PlanetDefinition>();
        public readonly List<GravitySourceDefinition> Sources = new List<GravitySourceDefinition>();
        public readonly List<HazardDefinition> Hazards = new List<HazardDefinition>();
        public readonly List<TargetDefinition> Targets = new List<TargetDefinition>();
        public StarCriteria Stars = new StarCriteria();
        public readonly List<HintDefinition> Hints = new List<HintDefinition>();
        public SolutionDefinition Solution = new SolutionDefinition();

        public string DisplayNumber => World + "-" + Number;
    }

    public enum ObjectiveType
    {
        /// <summary>Capture every target (respecting sequence order) without losing a planet.</summary>
        CaptureAllTargets = 0,
    }

    public sealed class ObjectiveDefinition
    {
        public ObjectiveType Type = ObjectiveType.CaptureAllTargets;
        public string Text = "";
    }

    public sealed class PlanetDefinition
    {
        public string Id;
        public Vec2 Position;
        public Vec2 Velocity;
        public double Radius = 0.35;
        /// <summary>Visual style key (terra, ice, lava, gas, moon).</summary>
        public string Style = "terra";
    }

    public enum RegionShape { Circle = 0, Rect = 1 }

    public sealed class PlacementRegion
    {
        public RegionShape Shape;
        public Vec2 Center;
        public double Radius;
        public Rect2 Rect;

        public bool Contains(Vec2 p) => Shape == RegionShape.Circle ? Vec2.DistanceSquared(p, Center) <= Radius * Radius : Rect.Contains(p);

        public Vec2 ClosestPoint(Vec2 p)
        {
            if (Shape == RegionShape.Rect) return Rect.ClosestPoint(p);
            Vec2 d = p - Center;
            double len = d.Length;
            if (len <= Radius) return p;
            return Center + d * (Radius / len);
        }
    }

    public sealed class GravitySourceDefinition
    {
        public string Id;
        public SourceKind Kind = SourceKind.Beacon;
        public Vec2 Position;
        public double Strength = 4;
        public double Radius = 0.3;
        public BeaconMobility Mobility = BeaconMobility.Fixed;
        public bool CostsMove = true;
        /// <summary>LimitedUse beacons: number of repositions allowed.</summary>
        public int Uses = 1;
        /// <summary>Legal placement areas. A movable beacon must have at least one.</summary>
        public readonly List<PlacementRegion> Regions = new List<PlacementRegion>();
        public string Style = "";

        public bool IsMovable => Kind == SourceKind.Beacon && Mobility != BeaconMobility.Fixed;
    }

    public sealed class HazardDefinition
    {
        public string Id;
        public HazardKind Kind = HazardKind.Asteroid;
        public Vec2 Position;
        public double Radius = 0.4;
    }

    public sealed class TargetDefinition
    {
        public string Id;
        public TargetKind Kind = TargetKind.Dock;
        public Vec2 Position;
        /// <summary>Optional: id of the source the target is centred on (orbit rings).</summary>
        public string CenterSource;
        public double Radius = 0.8;
        public double Tolerance = 0.3;
        public double CaptureSpeed = 1.6;
        public double DwellTime = 0.35;
        public double Drag = 4.0;
        public int Order;
        /// <summary>Optional: only this planet id may capture the target.</summary>
        public string Planet;
    }

    /// <summary>
    /// Star rules (deterministic): 1 star = objective complete; 2 stars = moves &lt;= TwoStarMoves;
    /// 3 stars = moves &lt;= ThreeStarMoves and (no hint used, if ThreeStarRequiresNoHint).
    /// </summary>
    public sealed class StarCriteria
    {
        public int TwoStarMoves = 2;
        public int ThreeStarMoves = 1;
        public bool ThreeStarRequiresNoHint = true;
    }

    public sealed class HintDefinition
    {
        public string Text;
        /// <summary>Object id to highlight (beacon, planet, target, hazard).</summary>
        public string Focus;
        /// <summary>Optional highlighted area.</summary>
        public bool HasArea;
        public Vec2 AreaCenter;
        public double AreaRadius;
        /// <summary>When true this tier shows the full solution (ghost beacon positions).</summary>
        public bool RevealsSolution;
    }

    public enum SolutionActionType { Place = 0, Launch = 1 }

    public struct SolutionAction
    {
        public SolutionActionType Type;
        public string Beacon;
        public Vec2 Position;
    }

    public sealed class SolutionDefinition
    {
        public readonly List<SolutionAction> Actions = new List<SolutionAction>();
        /// <summary>Stars the verified solution must earn (the level test harness checks this).</summary>
        public int ExpectedStars = 3;
        public string Notes = "";
    }
}
