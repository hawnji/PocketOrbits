using System.Collections.Generic;
using UnityEngine;

namespace PocketOrbits.Rendering
{
    /// <summary>Colour palette. High contrast mode swaps in brighter, more saturated target and hazard colours.</summary>
    public static class Palette
    {
        public static readonly Color SpaceTop = new Color(0.10f, 0.07f, 0.22f);
        public static readonly Color SpaceBottom = new Color(0.02f, 0.03f, 0.10f);
        public static readonly Color Nebula = new Color(0.45f, 0.20f, 0.65f, 0.16f);
        public static readonly Color Nebula2 = new Color(0.15f, 0.35f, 0.70f, 0.12f);

        public static readonly Color StarCore = new Color(1f, 0.86f, 0.55f);
        public static readonly Color StarHalo = new Color(1f, 0.62f, 0.25f, 0.55f);
        public static readonly Color Beacon = new Color(0.45f, 0.80f, 1f);
        public static readonly Color BeaconSelected = new Color(0.75f, 0.95f, 1f);
        public static readonly Color BeaconLocked = new Color(0.45f, 0.50f, 0.62f);
        public static readonly Color BeaconInvalid = new Color(1f, 0.35f, 0.35f);
        public static readonly Color BlackHoleRing = new Color(0.75f, 0.35f, 1f, 0.8f);

        public static Color Target(bool highContrast) => highContrast ? new Color(1f, 1f, 0.3f) : new Color(0.35f, 1f, 0.72f);
        public static Color TargetLocked(bool highContrast) => highContrast ? new Color(0.85f, 0.85f, 0.85f) : new Color(0.35f, 0.55f, 0.55f);
        public static Color Region(bool highContrast) => highContrast ? new Color(0.55f, 0.75f, 1f, 0.22f) : new Color(0.35f, 0.55f, 1f, 0.10f);
        public static Color RegionEdge(bool highContrast) => highContrast ? new Color(0.8f, 0.9f, 1f, 0.9f) : new Color(0.45f, 0.65f, 1f, 0.45f);
        public static Color Preview(bool highContrast) => highContrast ? new Color(1f, 1f, 1f, 1f) : new Color(0.75f, 0.92f, 1f, 0.9f);
        public static readonly Color PreviewCrash = new Color(1f, 0.4f, 0.35f, 0.95f);
        public static readonly Color PreviewSuccess = new Color(0.45f, 1f, 0.7f, 0.95f);
        public static readonly Color Asteroid = new Color(0.55f, 0.48f, 0.42f);
        public static readonly Color HintGlow = new Color(1f, 0.9f, 0.35f, 0.9f);

        /// <summary>Distinct planet base colours by style key.</summary>
        public static Color Planet(string style)
        {
            switch (style)
            {
                case "ice": return new Color(0.62f, 0.85f, 1f);
                case "lava": return new Color(1f, 0.45f, 0.25f);
                case "gas": return new Color(0.95f, 0.75f, 0.45f);
                case "moon": return new Color(0.78f, 0.78f, 0.82f);
                case "ringed": return new Color(0.80f, 0.60f, 1f);
                default: return new Color(0.35f, 0.80f, 0.50f);
            }
        }
    }

    /// <summary>
    /// Owns every shared material, procedural texture and mesh. Built once at startup; nothing here is
    /// created per frame. All art is procedural so the project has no binary art dependencies.
    /// </summary>
    public sealed class ArtLibrary
    {
        public readonly Material Lit;
        public readonly Material Unlit;
        public readonly Material Transparent;
        public readonly Material Additive;

        public readonly Mesh Sphere;
        public readonly Mesh Quad;
        public readonly Mesh Disc;
        public readonly Mesh AsteroidMesh;

        public readonly Texture2D Glow;
        public readonly Texture2D SoftDisc;
        public readonly Texture2D Dots;
        public readonly Texture2D White;
        public readonly Texture2D StarIcon;

        readonly Dictionary<string, Texture2D> _planetTextures = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Mesh> _rings = new Dictionary<string, Mesh>();

        public ArtLibrary()
        {
            Lit = MaterialFactory.Get(MaterialKind.Lit);
            Unlit = MaterialFactory.Get(MaterialKind.Unlit);
            Transparent = MaterialFactory.Get(MaterialKind.Transparent);
            Additive = MaterialFactory.Get(MaterialKind.Additive);

            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Sphere = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(tmp);

            Quad = BuildQuad();
            Disc = BuildRing(0f, 1f, 64);
            AsteroidMesh = BuildAsteroid(1234);

            White = Texture2D.whiteTexture;
            Glow = RadialTexture(64, t => Mathf.Pow(Mathf.Clamp01(1f - t), 2.2f));
            SoftDisc = RadialTexture(64, t => Mathf.Clamp01((1f - t) * 12f));
            Dots = DotTexture();
            StarIcon = StarTexture(64);
        }

        // ---- Textures -------------------------------------------------------------------------

        delegate float Falloff(float t);

        static Texture2D RadialTexture(int size, Falloff f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Radial" };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float t = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                byte a = (byte)(Mathf.Clamp01(f(t)) * 255);
                px[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Dotted line texture: one soft dot per tile, used with LineTextureMode.Tile.</summary>
        static Texture2D DotTexture()
        {
            const int w = 32, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, name = "Dots" };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float dx = (x - 7.5f) / 7.5f, dy = (y - 15.5f) / 15.5f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01((1f - d) * 3f) * 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        static Texture2D StarTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "StarIcon" };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c, dy = (y - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Atan2(dy, dx) + Mathf.PI / 2f;
                // Five pointed star: radius oscillates between inner and outer.
                float k = Mathf.Abs(Mathf.Repeat(a * 5f / (2f * Mathf.PI), 1f) - 0.5f) * 2f;
                float edge = Mathf.Lerp(0.95f, 0.42f, k);
                float alpha = Mathf.Clamp01((edge - r) * size * 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Equirectangular planet surface: banding, noise and craters by style.</summary>
        public Texture2D PlanetTexture(string style)
        {
            if (_planetTextures.TryGetValue(style, out var cached)) return cached;
            const int w = 128, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "Planet_" + style };
            var px = new Color[w * h];
            var rng = new System.Random(style.GetHashCode());
            float seed = (float)rng.NextDouble() * 100f;
            Color baseC = Palette.Planet(style);
            Color dark = baseC * 0.55f; dark.a = 1;
            Color light = Color.Lerp(baseC, Color.white, 0.35f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                float n = Mathf.PerlinNoise(seed + u * 8f, seed + v * 4f) * 0.6f + Mathf.PerlinNoise(seed * 2 + u * 24f, v * 12f) * 0.4f;
                Color c;
                switch (style)
                {
                    case "gas":
                    case "ringed":
                        float band = Mathf.Sin(v * 22f + n * 3f) * 0.5f + 0.5f;
                        c = Color.Lerp(dark, light, band * 0.7f + n * 0.3f);
                        break;
                    case "lava":
                        c = n > 0.62f ? new Color(1f, 0.85f, 0.3f) : Color.Lerp(new Color(0.25f, 0.08f, 0.06f), baseC, n);
                        break;
                    case "ice":
                        c = Color.Lerp(baseC, Color.white, Mathf.SmoothStep(0.4f, 0.7f, n));
                        break;
                    case "terra":
                        c = n > 0.52f ? Color.Lerp(baseC, new Color(0.85f, 0.8f, 0.55f), (n - 0.52f) * 2f) : new Color(0.15f, 0.35f, 0.75f);
                        if (v < 0.08f || v > 0.92f) c = Color.white;
                        break;
                    default:
                        c = Color.Lerp(dark, light, n);
                        break;
                }
                px[y * w + x] = c;
            }
            // Craters for rocky styles.
            if (style == "moon" || style == "lava" || style == "terra")
            {
                int craters = style == "moon" ? 18 : 6;
                for (int i = 0; i < craters; i++)
                {
                    int cx = rng.Next(w), cy = rng.Next(8, h - 8), r = rng.Next(2, 6);
                    for (int y = -r; y <= r; y++)
                    for (int x = -r; x <= r; x++)
                    {
                        float d = Mathf.Sqrt(x * x + y * y) / r;
                        if (d > 1) continue;
                        int ix = (cx + x + w) % w, iy = Mathf.Clamp(cy + y, 0, h - 1);
                        float shade = d > 0.75f ? 1.15f : 0.75f;
                        px[iy * w + ix] = px[iy * w + ix] * shade;
                    }
                }
            }
            tex.SetPixels(px);
            tex.Apply(true, true);
            _planetTextures[style] = tex;
            return tex;
        }

        // ---- Meshes ---------------------------------------------------------------------------

        static Mesh BuildQuad()
        {
            // Flat quad in the XZ plane facing +Y (towards the top-down camera).
            var m = new Mesh { name = "PO_Quad" };
            m.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Annulus in the XZ plane (inner 0 = filled disc). Cached by radii.</summary>
        public Mesh Ring(float inner, float outer, int segments = 72)
        {
            string key = Mathf.RoundToInt(inner * 1000) + "/" + Mathf.RoundToInt(outer * 1000) + "/" + segments;
            if (_rings.TryGetValue(key, out var mesh)) return mesh;
            mesh = BuildRing(inner, outer, segments);
            _rings[key] = mesh;
            return mesh;
        }

        static Mesh BuildRing(float inner, float outer, int segments)
        {
            var verts = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts[i * 2] = dir * inner;
                verts[i * 2 + 1] = dir * outer;
                uvs[i * 2] = new Vector2(i / (float)segments, 0);
                uvs[i * 2 + 1] = new Vector2(i / (float)segments, 1);
                if (i < segments)
                {
                    int t = i * 6, v = i * 2;
                    // Clockwise as seen from +Y (the camera), i.e. front-facing in Unity.
                    tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                    tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                }
            }
            var m = new Mesh { name = "PO_Ring" };
            m.vertices = verts;
            m.uv = uvs;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Lumpy low-poly rock: a sphere with deterministic radial displacement.</summary>
        Mesh BuildAsteroid(int seed)
        {
            var src = Sphere;
            var verts = src.vertices;
            var rng = new System.Random(seed);
            var offsets = new Vector3[6];
            for (int i = 0; i < offsets.Length; i++)
                offsets[i] = new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f).normalized;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 n = verts[i].normalized;
                float d = 1f;
                for (int k = 0; k < offsets.Length; k++) d += 0.12f * Mathf.Max(0, Vector3.Dot(n, offsets[k])) * (float)(rng.NextDouble() * 0.3 + 0.85);
                d -= 0.08f * Mathf.PerlinNoise(n.x * 3 + 5, n.z * 3 + n.y * 2);
                verts[i] = n * 0.5f * d;
            }
            var m = new Mesh { name = "PO_Asteroid" };
            m.vertices = verts;
            m.triangles = src.triangles;
            m.uv = src.uv;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
