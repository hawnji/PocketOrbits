using UnityEngine;

namespace PocketOrbits.Rendering
{
    /// <summary>
    /// Deep navy/violet space backdrop: vertical gradient, a single batched starfield mesh and two soft nebulae.
    /// Sits far below the gameplay plane and never moves, so it costs a handful of draw calls.
    /// </summary>
    public sealed class BackdropView : MonoBehaviour
    {
        Transform _stars;

        public void Build(ArtLibrary art, float width, float height, float effectsIntensity)
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);

            var gradient = new GameObject("Gradient");
            gradient.transform.SetParent(transform, false);
            gradient.transform.localPosition = new Vector3(0, Plane2D.BackgroundY, 0);
            var gm = new Mesh { name = "PO_Gradient" };
            float hw = width / 2f, hh = height / 2f;
            gm.vertices = new[] { new Vector3(-hw, 0, -hh), new Vector3(hw, 0, -hh), new Vector3(-hw, 0, hh), new Vector3(hw, 0, hh) };
            gm.colors = new[] { Palette.SpaceBottom, Palette.SpaceBottom, Palette.SpaceTop, Palette.SpaceTop };
            gm.uv = new[] { new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f) };
            gm.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            gm.RecalculateBounds();
            AddRenderer(gradient, gm, art.Transparent, art.White);

            var nebula1 = new GameObject("Nebula");
            nebula1.transform.SetParent(transform, false);
            nebula1.transform.localPosition = new Vector3(-hw * 0.35f, Plane2D.BackgroundY + 1, hh * 0.3f);
            nebula1.transform.localScale = new Vector3(width * 0.9f, 1, width * 0.7f);
            Tint(AddRenderer(nebula1, art.Quad, art.Transparent, art.Glow), Palette.Nebula * new Color(1, 1, 1, Mathf.Lerp(0.4f, 1f, effectsIntensity)));
            var nebula2 = new GameObject("Nebula2");
            nebula2.transform.SetParent(transform, false);
            nebula2.transform.localPosition = new Vector3(hw * 0.4f, Plane2D.BackgroundY + 1.1f, -hh * 0.35f);
            nebula2.transform.localScale = new Vector3(width * 0.8f, 1, width * 0.8f);
            Tint(AddRenderer(nebula2, art.Quad, art.Transparent, art.Glow), Palette.Nebula2 * new Color(1, 1, 1, Mathf.Lerp(0.4f, 1f, effectsIntensity)));

            // Sparse starfield in one mesh (quads with per-vertex colour).
            int count = Mathf.RoundToInt(Mathf.Lerp(60, 160, effectsIntensity));
            var rng = new System.Random(7);
            var verts = new Vector3[count * 4];
            var cols = new Color[count * 4];
            var uvs = new Vector2[count * 4];
            var tris = new int[count * 6];
            for (int i = 0; i < count; i++)
            {
                float x = (float)(rng.NextDouble() - 0.5) * width, z = (float)(rng.NextDouble() - 0.5) * height;
                float s = (float)(0.03 + rng.NextDouble() * rng.NextDouble() * 0.12);
                float b = (float)(0.35 + rng.NextDouble() * 0.65);
                var c = new Color(b, b, Mathf.Min(1, b + 0.15f), b);
                int v = i * 4;
                verts[v] = new Vector3(x - s, 0, z - s); verts[v + 1] = new Vector3(x + s, 0, z - s);
                verts[v + 2] = new Vector3(x - s, 0, z + s); verts[v + 3] = new Vector3(x + s, 0, z + s);
                uvs[v] = new Vector2(0, 0); uvs[v + 1] = new Vector2(1, 0); uvs[v + 2] = new Vector2(0, 1); uvs[v + 3] = new Vector2(1, 1);
                cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = c;
                int t = i * 6;
                tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1; tris[t + 3] = v + 2; tris[t + 4] = v + 3; tris[t + 5] = v + 1;
            }
            var sm = new Mesh { name = "PO_Starfield" };
            sm.vertices = verts; sm.colors = cols; sm.uv = uvs; sm.triangles = tris;
            sm.RecalculateBounds();
            var stars = new GameObject("Stars");
            stars.transform.SetParent(transform, false);
            stars.transform.localPosition = new Vector3(0, Plane2D.BackgroundY + 2, 0);
            AddRenderer(stars, sm, art.Additive, art.Glow);
            _stars = stars.transform;
        }

        static Renderer AddRenderer(GameObject go, Mesh mesh, Material mat, Texture tex)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            MaterialFactory.SetColor(block, Color.white);
            MaterialFactory.SetTexture(block, tex);
            r.SetPropertyBlock(block);
            return r;
        }

        static void Tint(Renderer r, Color c)
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            MaterialFactory.SetColor(block, c);
            r.SetPropertyBlock(block);
        }

        void Update()
        {
            // Very slow parallax-free drift so the menu never looks frozen.
            if (_stars != null) _stars.Rotate(0, 0.4f * Time.deltaTime, 0);
        }
    }
}
