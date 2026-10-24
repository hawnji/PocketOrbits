using UnityEngine;

namespace PocketOrbits.Rendering
{
    /// <summary>
    /// Cosmetic particle bursts (capture, crash, waypoint, invalid placement) and expanding shock rings.
    /// Uses one pooled ParticleSystem driven by Emit(), scaled by the effects-intensity setting.
    /// Never reads back into gameplay.
    /// </summary>
    public sealed class EffectsView : MonoBehaviour
    {
        ParticleSystem _ps;
        ArtLibrary _art;
        float _intensity = 1f;
        const int MaxParticles = 300;

        readonly Transform[] _rings = new Transform[4];
        readonly Renderer[] _ringRenderers = new Renderer[4];
        readonly float[] _ringAge = new float[4];
        readonly Color[] _ringColor = new Color[4];
        MaterialPropertyBlock _block;

        public void Init(ArtLibrary art)
        {
            _art = art;
            _block = new MaterialPropertyBlock();
            var go = new GameObject("Particles");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, Plane2D.OverlayY, 0);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = MaxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 0.9f;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            var emission = _ps.emission;
            emission.enabled = false;
            var shape = _ps.shape;
            shape.enabled = false;
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var size = _ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0.2f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(art.Additive) { name = "PO_Particles", mainTexture = art.Glow };
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", art.Glow);
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            for (int i = 0; i < _rings.Length; i++)
            {
                var r = new GameObject("Shock");
                r.transform.SetParent(transform, false);
                r.AddComponent<MeshFilter>().sharedMesh = art.Ring(0.85f, 1f, 48);
                var mr = r.AddComponent<MeshRenderer>();
                mr.sharedMaterial = art.Additive;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _rings[i] = r.transform;
                _ringRenderers[i] = mr;
                _ringAge[i] = 99f;
                r.SetActive(false);
            }
        }

        public void SetIntensity(float intensity) => _intensity = Mathf.Clamp01(intensity);

        public void Burst(Vector3 position, Color color, int count, float speed, float size = 0.18f, float life = 0.9f)
        {
            if (_ps == null) return;
            int n = Mathf.RoundToInt(count * Mathf.Lerp(0.25f, 1f, _intensity));
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                float v = speed * Random.Range(0.35f, 1f);
                p.position = new Vector3(position.x, Plane2D.OverlayY, position.z);
                p.velocity = new Vector3(Mathf.Cos(a) * v, 0, Mathf.Sin(a) * v);
                p.startColor = Color.Lerp(color, Color.white, Random.value * 0.3f);
                p.startSize = size * Random.Range(0.6f, 1.4f);
                p.startLifetime = life * Random.Range(0.6f, 1.1f);
                _ps.Emit(p, 1);
            }
        }

        public void Shock(Vector3 position, Color color)
        {
            if (_intensity <= 0.01f) return;
            int slot = 0;
            for (int i = 1; i < _ringAge.Length; i++) if (_ringAge[i] > _ringAge[slot]) slot = i;
            _ringAge[slot] = 0;
            _ringColor[slot] = color;
            _rings[slot].position = new Vector3(position.x, Plane2D.OverlayY, position.z);
            _rings[slot].gameObject.SetActive(true);
        }

        public void Capture(Vector3 pos, Color c) { Burst(pos, c, 40, 3.2f); Shock(pos, c); }
        public void Crash(Vector3 pos) { Burst(pos, new Color(1f, 0.55f, 0.25f), 50, 4f, 0.22f); Burst(pos, Color.gray, 20, 2f, 0.12f, 1.4f); }
        public void Waypoint(Vector3 pos, Color c) { Burst(pos, c, 16, 2f, 0.12f); Shock(pos, c); }
        public void Invalid(Vector3 pos) { Burst(pos, Palette.BeaconInvalid, 8, 1.2f, 0.1f, 0.4f); }

        void Update()
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                if (_ringAge[i] > 1f) { if (_rings[i].gameObject.activeSelf) _rings[i].gameObject.SetActive(false); continue; }
                _ringAge[i] += Time.deltaTime * 1.6f;
                float t = Mathf.Clamp01(_ringAge[i]);
                float s = Mathf.Lerp(0.4f, 2.2f, 1 - (1 - t) * (1 - t));
                _rings[i].localScale = new Vector3(s, 1, s);
                _ringRenderers[i].GetPropertyBlock(_block);
                var c = _ringColor[i];
                MaterialFactory.SetColor(_block, new Color(c.r, c.g, c.b, (1 - t) * _intensity));
                _ringRenderers[i].SetPropertyBlock(_block);
            }
        }
    }
}
