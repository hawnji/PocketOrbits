using System.Collections.Generic;
using PocketOrbits.Core.Simulation;
using UnityEngine;

namespace PocketOrbits.Rendering
{
    /// <summary>
    /// Renders a <see cref="TrajectoryPrediction"/> as a dotted line that fades towards the end, with an end marker
    /// (green ring for a capture, red cross for a crash/escape). Purely visual; buffers are reused.
    /// </summary>
    public sealed class TrajectoryView : MonoBehaviour
    {
        readonly List<LineRenderer> _lines = new List<LineRenderer>();
        readonly List<Vector3[]> _buffers = new List<Vector3[]>();
        Transform _endMarker;
        Renderer _endRenderer;
        Transform _crossA, _crossB;
        Renderer _crossRendererA, _crossRendererB;
        MaterialPropertyBlock _block;
        bool _highContrast;
        float _scroll;

        public void Init(ArtLibrary art, int planets, bool highContrast)
        {
            _highContrast = highContrast;
            _block = new MaterialPropertyBlock();
            var dotted = new Material(art.Additive) { name = "PO_Trajectory", mainTexture = art.Dots };
            if (dotted.HasProperty("_BaseMap")) dotted.SetTexture("_BaseMap", art.Dots);
            // One dot per ~2 line widths.
            dotted.mainTextureScale = new Vector2(0.5f, 1f);
            if (dotted.HasProperty("_BaseMap")) dotted.SetTextureScale("_BaseMap", new Vector2(0.5f, 1f));
            for (int i = 0; i < planets; i++)
            {
                var go = new GameObject("Path_" + i);
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = dotted;
                lr.textureMode = LineTextureMode.Tile;
                lr.alignment = LineAlignment.View;
                lr.useWorldSpace = true;
                lr.widthMultiplier = highContrast ? 0.2f : 0.15f;
                lr.numCapVertices = 0;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.positionCount = 0;
                _lines.Add(lr);
                _buffers.Add(new Vector3[512]);
            }

            _endMarker = MakeMarker(art, art.Ring(0.28f, 0.4f, 32), art.Transparent, out _endRenderer);
            _crossA = MakeMarker(art, art.Quad, art.Transparent, out _crossRendererA);
            _crossB = MakeMarker(art, art.Quad, art.Transparent, out _crossRendererB);
            _crossA.localScale = _crossB.localScale = new Vector3(0.7f, 1, 0.12f);
            Hide();
        }

        Transform MakeMarker(ArtLibrary art, Mesh mesh, Material mat, out Renderer r)
        {
            var go = new GameObject("Marker");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r = mr;
            return go.transform;
        }

        public void Hide()
        {
            foreach (var l in _lines) l.positionCount = 0;
            if (_endMarker != null) _endMarker.gameObject.SetActive(false);
            if (_crossA != null) { _crossA.gameObject.SetActive(false); _crossB.gameObject.SetActive(false); }
        }

        public void Show(TrajectoryPrediction p)
        {
            Color baseColor = Palette.Preview(_highContrast);
            int deathPlanet = -1;
            Vec2 deathPos = default;
            foreach (var e in p.Events)
            {
                if (e.Type == SimEventType.PlanetCrashedIntoSource || e.Type == SimEventType.PlanetHitHazard || e.Type == SimEventType.PlanetsCollided || e.Type == SimEventType.PlanetEscaped)
                {
                    deathPlanet = e.Planet;
                    deathPos = e.Position;
                    break;
                }
            }

            for (int i = 0; i < _lines.Count && i < p.Paths.Count; i++)
            {
                var path = p.Paths[i];
                var buffer = _buffers[i];
                if (buffer.Length < path.Count)
                {
                    buffer = new Vector3[Mathf.NextPowerOfTwo(path.Count)];
                    _buffers[i] = buffer;
                }
                for (int k = 0; k < path.Count; k++) buffer[k] = Plane2D.ToWorld(path[k], Plane2D.TrajectoryY);
                var lr = _lines[i];
                lr.positionCount = path.Count;
                lr.SetPositions(buffer);
                Color end = i == deathPlanet ? Palette.PreviewCrash : p.End == PredictionEnd.AllTargetsCaptured ? Palette.PreviewSuccess : baseColor;
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(baseColor, 0f), new GradientColorKey(Color.Lerp(baseColor, end, 0.5f), 0.7f), new GradientColorKey(end, 1f) },
                    new[] { new GradientAlphaKey(baseColor.a, 0f), new GradientAlphaKey(baseColor.a * 0.8f, 0.6f), new GradientAlphaKey(p.End == PredictionEnd.Duration ? 0.05f : 0.9f, 1f) });
                lr.colorGradient = g;
            }

            bool success = p.End == PredictionEnd.AllTargetsCaptured;
            _endMarker.gameObject.SetActive(success);
            bool crash = deathPlanet >= 0;
            _crossA.gameObject.SetActive(crash);
            _crossB.gameObject.SetActive(crash);
            if (success)
            {
                var last = p.Paths[0][p.Paths[0].Count - 1];
                _endMarker.position = Plane2D.ToWorld(last, Plane2D.OverlayY);
                Tint(_endRenderer, Palette.PreviewSuccess);
            }
            if (crash)
            {
                Vector3 pos = Plane2D.ToWorld(deathPos, Plane2D.OverlayY);
                _crossA.position = _crossB.position = pos;
                _crossA.rotation = Quaternion.Euler(0, 45, 0);
                _crossB.rotation = Quaternion.Euler(0, -45, 0);
                Tint(_crossRendererA, Palette.PreviewCrash);
                Tint(_crossRendererB, Palette.PreviewCrash);
            }
        }

        void Tint(Renderer r, Color c)
        {
            r.GetPropertyBlock(_block);
            MaterialFactory.SetColor(_block, c);
            r.SetPropertyBlock(_block);
        }

        void Update()
        {
            // Dots drift along the path to show direction of travel.
            _scroll -= Time.deltaTime * 1.5f;
            foreach (var l in _lines)
            {
                if (l.positionCount == 0) continue;
                l.sharedMaterial.mainTextureOffset = new Vector2(_scroll, 0);
                if (l.sharedMaterial.HasProperty("_BaseMap")) l.sharedMaterial.SetTextureOffset("_BaseMap", new Vector2(_scroll, 0));
                break; // shared material: once is enough
            }
        }
    }
}
