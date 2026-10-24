using System.Collections.Generic;
using PocketOrbits.Core.Gameplay;
using PocketOrbits.Core.Levels;
using PocketOrbits.Core.Simulation;
using UnityEngine;

namespace PocketOrbits.Rendering
{
    /// <summary>Maps the 2D gameplay plane (x, y) onto the Unity XZ plane viewed from above.</summary>
    public static class Plane2D
    {
        public static Vector3 ToWorld(Vec2 v, float height = 0f) => new Vector3((float)v.X, height, (float)v.Y);
        public static Vec2 ToSim(Vector3 w) => new Vec2(w.x, w.z);

        // Render layers (world Y). The camera looks straight down, so higher Y draws on top.
        public const float BackgroundY = -30f;
        public const float RegionY = -2f;
        public const float TargetY = -1.5f;
        public const float HaloY = -1f;
        public const float TrajectoryY = -0.6f;
        public const float BodyY = 0f;
        public const float OverlayY = 1.5f;
    }

    /// <summary>
    /// Visual representation of a <see cref="PuzzleSession"/>. Reads simulation state every frame and never
    /// writes to it: everything here (spin, pulses, particles, trails) is cosmetic.
    /// </summary>
    public sealed class LevelView : MonoBehaviour
    {
        ArtLibrary _art;
        PuzzleSession _session;
        bool _highContrast;
        float _fx = 1f;

        readonly List<Transform> _planets = new List<Transform>();
        readonly List<TrailRenderer> _trails = new List<TrailRenderer>();
        readonly List<Transform> _sources = new List<Transform>();
        readonly List<Renderer> _sourceCores = new List<Renderer>();
        readonly List<Transform> _sourceRings = new List<Transform>();
        readonly List<Transform> _targets = new List<Transform>();
        readonly List<Renderer> _targetRings = new List<Renderer>();
        readonly List<Renderer> _targetFills = new List<Renderer>();
        readonly List<GameObject> _regions = new List<GameObject>();
        readonly List<Vector3> _planetVisualPos = new List<Vector3>();
        Transform _highlight;
        Renderer _highlightRenderer;
        readonly List<Transform> _ghosts = new List<Transform>();
        MaterialPropertyBlock _block;
        float _time;

        public TrajectoryView Trajectory { get; private set; }

        public void Build(ArtLibrary art, PuzzleSession session, bool highContrast, float effectsIntensity)
        {
            _art = art;
            _session = session;
            _highContrast = highContrast;
            _fx = effectsIntensity;
            _block = new MaterialPropertyBlock();
            Clear();
            var level = session.Level;
            var s = session.State;

            // Placement regions (drawn under everything).
            for (int i = 0; i < level.Sources.Count; i++)
            {
                var def = level.Sources[i];
                if (!def.IsMovable) continue;
                foreach (var region in def.Regions) _regions.Add(BuildRegion(region, i));
            }

            for (int i = 0; i < s.Targets.Length; i++) BuildTarget(level, s, i);
            for (int i = 0; i < s.Hazards.Length; i++) BuildHazard(s.Hazards[i]);
            for (int i = 0; i < s.Sources.Length; i++) BuildSource(level.Sources[i], s.Sources[i]);
            for (int i = 0; i < s.Planets.Length; i++) BuildPlanet(level.Planets[i], s.Planets[i]);

            var hl = Child("Highlight", Plane2D.OverlayY);
            hl.gameObject.AddComponent<MeshFilter>().sharedMesh = _art.Ring(0.86f, 1f);
            _highlightRenderer = hl.gameObject.AddComponent<MeshRenderer>();
            _highlightRenderer.sharedMaterial = _art.Additive;
            Tint(_highlightRenderer, Palette.HintGlow);
            hl.gameObject.SetActive(false);
            _highlight = hl;

            var trajGo = new GameObject("Trajectory");
            trajGo.transform.SetParent(transform, false);
            Trajectory = trajGo.AddComponent<TrajectoryView>();
            Trajectory.Init(_art, s.Planets.Length, highContrast);
            SyncImmediate();
        }

        public void SetAccessibility(bool highContrast, float effectsIntensity)
        {
            if (_session == null) return;
            if (highContrast != _highContrast) Build(_art, _session, highContrast, effectsIntensity);
            _fx = effectsIntensity;
        }

        void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            _planets.Clear(); _trails.Clear(); _sources.Clear(); _sourceCores.Clear(); _sourceRings.Clear();
            _targets.Clear(); _targetRings.Clear(); _targetFills.Clear(); _regions.Clear(); _ghosts.Clear();
            _planetVisualPos.Clear();
        }

        Transform Child(string name, float y)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, y, 0);
            return go.transform;
        }

        MeshRenderer AddMesh(Transform t, Mesh mesh, Material mat)
        {
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = t.gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        void Tint(Renderer r, Color c, Texture tex = null)
        {
            r.GetPropertyBlock(_block);
            MaterialFactory.SetColor(_block, c);
            if (tex != null) MaterialFactory.SetTexture(_block, tex);
            r.SetPropertyBlock(_block);
        }

        GameObject BuildRegion(PlacementRegion region, int sourceIndex)
        {
            var t = Child("Region_" + sourceIndex, Plane2D.RegionY);
            var fill = Child("Fill", 0); fill.SetParent(t, false);
            var edge = Child("Edge", 0.01f); edge.SetParent(t, false);
            if (region.Shape == RegionShape.Circle)
            {
                t.localPosition = Plane2D.ToWorld(region.Center, Plane2D.RegionY);
                float r = (float)region.Radius;
                Tint(AddMesh(fill, _art.Disc, _art.Transparent), Palette.Region(_highContrast));
                fill.localScale = new Vector3(r, 1, r);
                Tint(AddMesh(edge, _art.Ring(r - 0.04f, r), _art.Transparent), Palette.RegionEdge(_highContrast));
            }
            else
            {
                var rc = region.Rect;
                t.localPosition = Plane2D.ToWorld(rc.Center, Plane2D.RegionY);
                Tint(AddMesh(fill, _art.Quad, _art.Transparent), Palette.Region(_highContrast));
                fill.localScale = new Vector3((float)rc.Width, 1, (float)rc.Height);
                var lr = edge.gameObject.AddComponent<LineRenderer>();
                lr.sharedMaterial = _art.Transparent;
                lr.loop = true;
                lr.useWorldSpace = false;
                lr.positionCount = 4;
                float hw = (float)rc.Width / 2, hh = (float)rc.Height / 2;
                lr.SetPositions(new[] { new Vector3(-hw, 0, -hh), new Vector3(hw, 0, -hh), new Vector3(hw, 0, hh), new Vector3(-hw, 0, hh) });
                lr.widthMultiplier = _highContrast ? 0.07f : 0.04f;
                lr.startColor = lr.endColor = Palette.RegionEdge(_highContrast);
                lr.alignment = LineAlignment.View;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return t.gameObject;
        }

        void BuildTarget(LevelDefinition level, SimState s, int i)
        {
            var target = s.Targets[i];
            var t = Child("Target_" + target.Id, Plane2D.TargetY);
            t.localPosition = Plane2D.ToWorld(s.TargetCenter(i), Plane2D.TargetY);
            Color c = Palette.Target(_highContrast);
            float width = _highContrast ? 0.12f : 0.07f;
            MeshRenderer ring, fill;
            var fillT = Child("Fill", 0); fillT.SetParent(t, false);
            var ringT = Child("Ring", 0.01f); ringT.SetParent(t, false);
            switch (target.Kind)
            {
                case TargetKind.OrbitRing:
                {
                    float r = (float)target.Radius, tol = (float)target.Tolerance;
                    fill = AddMesh(fillT, _art.Ring(r - tol, r + tol), _art.Transparent);
                    Tint(fill, new Color(c.r, c.g, c.b, 0.12f));
                    // Two edge lines plus the ideal orbit line: a shape cue that does not rely on colour.
                    ring = AddMesh(ringT, _art.Ring(r - 0.025f, r + 0.025f), _art.Additive);
                    var e1 = Child("EdgeIn", 0.01f); e1.SetParent(t, false);
                    Tint(AddMesh(e1, _art.Ring(r - tol, r - tol + width), _art.Transparent), c);
                    var e2 = Child("EdgeOut", 0.01f); e2.SetParent(t, false);
                    Tint(AddMesh(e2, _art.Ring(r + tol - width, r + tol), _art.Transparent), c);
                    break;
                }
                case TargetKind.Waypoint:
                {
                    float r = (float)target.Radius;
                    fill = AddMesh(fillT, _art.Disc, _art.Transparent);
                    fillT.localScale = new Vector3(r, 1, r);
                    Tint(fill, new Color(c.r, c.g, c.b, 0.15f));
                    ring = AddMesh(ringT, _art.Ring(r - width, r, 4), _art.Transparent); // diamond outline
                    ringT.localRotation = Quaternion.Euler(0, 45, 0);
                    break;
                }
                default:
                {
                    float r = (float)target.Radius;
                    fill = AddMesh(fillT, _art.Disc, _art.Transparent);
                    fillT.localScale = new Vector3(r, 1, r);
                    Tint(fill, new Color(c.r, c.g, c.b, 0.15f));
                    ring = AddMesh(ringT, _art.Ring(r - width, r, 64), _art.Transparent);
                    // Inner dashed ring: docks read as "landing pads".
                    var inner = Child("Inner", 0.01f); inner.SetParent(t, false);
                    Tint(AddMesh(inner, _art.Ring(r * 0.35f, r * 0.35f + width * 0.7f, 6), _art.Transparent), c);
                    break;
                }
            }
            Tint(ring, c);
            _targets.Add(t);
            _targetRings.Add(ring);
            _targetFills.Add(fill);
        }

        void BuildHazard(HazardState h)
        {
            var t = Child("Hazard_" + h.Id, Plane2D.BodyY);
            t.localPosition = Plane2D.ToWorld(h.Position);
            float d = (float)h.Radius * 2f;
            t.localScale = new Vector3(d, d, d);
            t.localRotation = Quaternion.Euler(Random.Range(0, 360f), Random.Range(0, 360f), 0);
            Tint(AddMesh(t, _art.AsteroidMesh, _art.Lit), _highContrast ? new Color(0.9f, 0.55f, 0.3f) : Palette.Asteroid);
        }

        void BuildSource(GravitySourceDefinition def, SourceState src)
        {
            var t = Child("Source_" + src.Id, Plane2D.BodyY);
            t.localPosition = Plane2D.ToWorld(src.Position);
            var core = Child("Core", 0); core.SetParent(t, false);
            float d = (float)src.Radius * 2f;
            core.localScale = new Vector3(d, d, d);
            Transform ringT = null;
            MeshRenderer coreR;
            switch (src.Kind)
            {
                case SourceKind.Star:
                {
                    coreR = AddMesh(core, _art.Sphere, _art.Unlit);
                    Tint(coreR, Palette.StarCore);
                    var halo = Child("Halo", Plane2D.HaloY); halo.SetParent(t, false);
                    float hs = d * 3.2f;
                    halo.localScale = new Vector3(hs, 1, hs);
                    Tint(AddMesh(halo, _art.Quad, _art.Additive), Palette.StarHalo * new Color(1, 1, 1, Mathf.Lerp(0.5f, 1f, _fx)), _art.Glow);
                    break;
                }
                case SourceKind.BlackHole:
                {
                    coreR = AddMesh(core, _art.Sphere, _art.Unlit);
                    Tint(coreR, Color.black);
                    ringT = Child("Accretion", Plane2D.HaloY); ringT.SetParent(t, false);
                    Tint(AddMesh(ringT, _art.Ring(d * 0.55f, d * 1.1f), _art.Additive), Palette.BlackHoleRing, _art.Glow);
                    break;
                }
                default:
                {
                    coreR = AddMesh(core, _art.Sphere, _art.Lit);
                    Tint(coreR, def.IsMovable ? Palette.Beacon : Palette.BeaconLocked);
                    var glow = Child("Glow", Plane2D.HaloY); glow.SetParent(t, false);
                    float gs = d * 3f;
                    glow.localScale = new Vector3(gs, 1, gs);
                    Tint(AddMesh(glow, _art.Quad, _art.Additive), new Color(0.4f, 0.75f, 1f, 0.6f), _art.Glow);
                    // Influence rings: subtle field indicator whose spacing hints at strength.
                    ringT = Child("Field", Plane2D.HaloY); ringT.SetParent(t, false);
                    float fr = Mathf.Sqrt((float)src.Strength) * 0.9f;
                    Tint(AddMesh(ringT, _art.Ring(fr - 0.02f, fr), _art.Additive), new Color(0.45f, 0.75f, 1f, 0.35f));
                    if (def.IsMovable)
                    {
                        // Grab handle ring: movable beacons are recognisable without colour.
                        var handle = Child("Handle", 0.6f); handle.SetParent(t, false);
                        Tint(AddMesh(handle, _art.Ring(d * 0.75f, d * 0.95f, 8), _art.Transparent), new Color(1, 1, 1, 0.85f));
                    }
                    break;
                }
            }
            _sources.Add(t);
            _sourceCores.Add(coreR);
            _sourceRings.Add(ringT);
        }

        void BuildPlanet(PlanetDefinition def, PlanetState p)
        {
            var t = Child("Planet_" + p.Id, Plane2D.BodyY);
            t.localPosition = Plane2D.ToWorld(p.Position);
            float d = (float)p.Radius * 2f;
            var body = Child("Body", 0); body.SetParent(t, false);
            body.localScale = new Vector3(d, d, d);
            body.localRotation = Quaternion.Euler(20, 0, 10);
            Tint(AddMesh(body, _art.Sphere, _art.Lit), Color.white, _art.PlanetTexture(def.Style));
            if (def.Style == "ringed")
            {
                var ring = Child("Rings", 0); ring.SetParent(t, false);
                ring.localRotation = Quaternion.Euler(25, 0, 15);
                Tint(AddMesh(ring, _art.Ring(d * 0.75f, d * 1.15f), _art.Transparent), new Color(0.95f, 0.85f, 1f, 0.65f));
            }
            if (_highContrast)
            {
                var outline = Child("Outline", Plane2D.HaloY); outline.SetParent(t, false);
                Tint(AddMesh(outline, _art.Ring(d * 0.55f, d * 0.68f), _art.Transparent), Color.white);
            }
            var trail = t.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = _art.Additive;
            trail.time = Mathf.Lerp(0.4f, 1.6f, _fx);
            trail.minVertexDistance = 0.05f;
            trail.widthMultiplier = d * 0.45f;
            trail.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
            Color pc = Palette.Planet(def.Style);
            trail.startColor = new Color(pc.r, pc.g, pc.b, 0.7f);
            trail.endColor = new Color(pc.r, pc.g, pc.b, 0f);
            trail.alignment = LineAlignment.View;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.emitting = false;
            _planets.Add(t);
            _trails.Add(trail);
            _planetVisualPos.Add(t.localPosition);
        }

        /// <summary>Snaps every visual to the simulation state (after reset / undo).</summary>
        public void SyncImmediate()
        {
            if (_session == null) return;
            var s = _session.State;
            for (int i = 0; i < _planets.Count; i++)
            {
                _planets[i].localPosition = Plane2D.ToWorld(s.Planets[i].Position);
                _planetVisualPos[i] = _planets[i].localPosition;
                _planets[i].gameObject.SetActive(s.Planets[i].IsAlive);
                _trails[i].Clear();
            }
            Sync(0f);
        }

        void LateUpdate()
        {
            if (_session == null) return;
            Sync(Time.deltaTime);
        }

        void Sync(float dt)
        {
            _time += dt;
            var s = _session.State;
            bool simulating = _session.Current == GameState.Simulating;

            for (int i = 0; i < _planets.Count; i++)
            {
                var p = s.Planets[i];
                Vector3 target = Plane2D.ToWorld(p.Position);
                // Docked planets glide into the dock centre; everything else follows the sim exactly.
                if (p.Status == PlanetStatus.Docked) _planetVisualPos[i] = Vector3.Lerp(_planetVisualPos[i], target, 1f - Mathf.Exp(-10f * dt));
                else _planetVisualPos[i] = target;
                _planets[i].localPosition = _planetVisualPos[i];
                _planets[i].GetChild(0).Rotate(0, 40f * dt, 0, Space.Self);
                _trails[i].emitting = simulating && p.Status != PlanetStatus.Docked && _fx > 0.05f;
                bool alive = p.IsAlive;
                if (_planets[i].gameObject.activeSelf != alive) _planets[i].gameObject.SetActive(alive);
            }

            for (int i = 0; i < _sources.Count; i++)
            {
                Vec2 pos = s.Sources[i].Position;
                if (_session.IsDragging && i == _session.SelectedSource) pos = _session.PendingPosition;
                _sources[i].localPosition = Plane2D.ToWorld(pos);
                var def = _session.Level.Sources[i];
                if (def.Kind == SourceKind.Beacon)
                {
                    Color c = Palette.Beacon;
                    if (!def.IsMovable || !_session.CanEdit(i, out _)) c = Palette.BeaconLocked;
                    if (_session.IsDragging && i == _session.SelectedSource) c = _session.PendingPlacement.Valid ? Palette.BeaconSelected : Palette.BeaconInvalid;
                    Tint(_sourceCores[i], c);
                    if (_sourceRings[i] != null)
                    {
                        float pulse = 1f + 0.06f * Mathf.Sin(_time * 3f) * _fx;
                        _sourceRings[i].localScale = new Vector3(pulse, 1, pulse);
                    }
                }
                else if (def.Kind == SourceKind.BlackHole && _sourceRings[i] != null)
                {
                    _sourceRings[i].Rotate(0, 30f * dt, 0);
                }
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                _targets[i].localPosition = Plane2D.ToWorld(s.TargetCenter(i), Plane2D.TargetY);
                bool accepting = GravitySimulation.TargetAccepting(s, i);
                var t = s.Targets[i];
                Color c = t.Captured ? Palette.PreviewSuccess : accepting ? Palette.Target(_highContrast) : Palette.TargetLocked(_highContrast);
                float pulse = accepting ? 0.75f + 0.25f * Mathf.Sin(_time * 2.5f) * _fx : 1f;
                Tint(_targetRings[i], new Color(c.r, c.g, c.b, pulse));
                float fillAlpha = t.Captured ? 0.3f : 0.12f + 0.25f * (float)(t.DwellTime > 0 ? t.Dwell / t.DwellTime : 0);
                Tint(_targetFills[i], new Color(c.r, c.g, c.b, fillAlpha));
            }

            // Regions are emphasised while a beacon is held.
            for (int i = 0; i < _regions.Count; i++) _regions[i].SetActive(_session.Current != GameState.Simulating);

            if (_highlight != null && _highlight.gameObject.activeSelf)
            {
                float k = 1f + 0.08f * Mathf.Sin(_time * 5f);
                _highlight.localScale = _highlightBase * k;
            }
        }

        Vector3 _highlightBase = Vector3.one;

        /// <summary>Shows the hint highlight around an object id and/or an area; null hides it.</summary>
        public void ShowHint(HintDefinition hint)
        {
            foreach (var g in _ghosts) Destroy(g.gameObject);
            _ghosts.Clear();
            if (_highlight == null) return;
            if (hint == null || _session == null) { _highlight.gameObject.SetActive(false); return; }

            var s = _session.State;
            Vec2? center = null;
            double radius = 0.8;
            if (hint.HasArea)
            {
                center = hint.AreaCenter;
                radius = hint.AreaRadius;
            }
            else if (!string.IsNullOrEmpty(hint.Focus))
            {
                int k;
                if ((k = s.FindSource(hint.Focus)) >= 0) { center = s.Sources[k].Position; radius = s.Sources[k].Radius + 0.5; }
                else if ((k = s.FindPlanet(hint.Focus)) >= 0) { center = s.Planets[k].Position; radius = s.Planets[k].Radius + 0.5; }
                else if ((k = s.FindTarget(hint.Focus)) >= 0) { center = s.TargetCenter(k); radius = s.Targets[k].Radius + 0.3; }
                else
                {
                    for (int h = 0; h < s.Hazards.Length; h++)
                        if (s.Hazards[h].Id == hint.Focus) { center = s.Hazards[h].Position; radius = s.Hazards[h].Radius + 0.5; }
                }
            }
            if (center.HasValue)
            {
                _highlight.gameObject.SetActive(true);
                _highlight.localPosition = Plane2D.ToWorld(center.Value, Plane2D.OverlayY);
                _highlightBase = new Vector3((float)radius, 1, (float)radius);
                _highlight.localScale = _highlightBase;
            }
            else _highlight.gameObject.SetActive(false);

            if (hint.RevealsSolution)
            {
                foreach (var a in _session.Level.Solution.Actions)
                {
                    if (a.Type != SolutionActionType.Place) continue;
                    var g = Child("Ghost_" + a.Beacon, Plane2D.OverlayY);
                    g.localPosition = Plane2D.ToWorld(a.Position, Plane2D.OverlayY);
                    g.localScale = new Vector3(0.45f, 1, 0.45f);
                    Tint(AddMesh(g, _art.Ring(0.7f, 1f, 24), _art.Additive), Palette.HintGlow);
                    _ghosts.Add(g);
                }
            }
        }

        public Vector3 PlanetWorldPosition(int i) => i >= 0 && i < _planets.Count ? _planets[i].position : Vector3.zero;
    }
}
