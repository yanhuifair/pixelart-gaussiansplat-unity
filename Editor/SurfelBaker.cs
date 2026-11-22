using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace PixelArtSplats.EditorTools
{
    /// <summary>
    /// Writes splat clouds and palettes to disk, and scatters surfels over ordinary meshes.
    /// Port of tools/convert-model.mjs (the mesh half of the pipeline).
    /// </summary>
    public static class SurfelBaker
    {
        /// <summary>Where baked output goes in the user's project (the package itself lives in Packages/).</summary>
        public const string Root = "Assets/Pixel Art Splats";

        /// <summary>Splat radius as a fraction of the spacing for mesh bakes (see <see cref="FromMeshes"/>).</summary>
        public const float MeshRadiusPerSpacing = 0.9f;

        // ---- assets ---------------------------------------------------------------

        public static SplatCloudAsset CreateCloud(string name, Surfel[] surfels, float spacing,
            Bounds bounds, string note, string folder = Root + "/Baked")
        {
            Directory.CreateDirectory(folder);
            int opaque = 0;
            SplatCloudAsset.Write(surfels, ref opaque);

            string binPath = $"{folder}/{name}.bytes";
            byte[] raw = new byte[surfels.Length * Surfel.Stride];
            MemoryMarshal.AsBytes(surfels.AsSpan()).CopyTo(raw);
            File.WriteAllBytes(binPath, raw);
            AssetDatabase.ImportAsset(binPath, ImportAssetOptions.ForceUpdate);
            TextAsset blob = AssetDatabase.LoadAssetAtPath<TextAsset>(binPath);
            if (blob == null) throw new Exception($"could not import {binPath}");

            string assetPath = $"{folder}/{name}.asset";
            SplatCloudAsset asset = AssetDatabase.LoadAssetAtPath<SplatCloudAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<SplatCloudAsset>();
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            asset.data = blob;
            asset.count = surfels.Length;
            asset.opaqueCount = opaque;
            asset.spacing = spacing;
            asset.bounds = bounds;
            asset.note = note;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"Pixel art splats: {assetPath} — {surfels.Length} surfels ({opaque} solid), " +
                      $"spacing {spacing:0.0000}, {(raw.Length / 1048576f):0.0} MB");
            return asset;
        }

        public static PixelArtPalette CreatePalette(string name, string[] surface, string[] glow,
            string background, string folder = Root + "/Baked")
        {
            Directory.CreateDirectory(folder);
            string path = $"{folder}/{name}.asset";
            PixelArtPalette palette = AssetDatabase.LoadAssetAtPath<PixelArtPalette>(path);
            if (palette == null)
            {
                palette = ScriptableObject.CreateInstance<PixelArtPalette>();
                AssetDatabase.CreateAsset(palette, path);
            }
            palette.surface = surface;
            palette.glow = glow;
            palette.background = background;
            EditorUtility.SetDirty(palette);
            AssetDatabase.SaveAssets();
            return palette;
        }

        // ---- meshes ---------------------------------------------------------------

        /// <summary>
        /// Scatter surfels evenly over the triangles of everything under <paramref name="root"/>,
        /// keeping the colour of the material, its texture and any vertex colours.
        /// </summary>
        public static Surfel[] FromMeshes(GameObject root, int targetCount, out float spacing, out Bounds bounds)
        {
            Matrix4x4 toRoot = root.transform.worldToLocalMatrix;
            var parts = new List<MeshPart>();
            float totalArea = 0f;

            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>();
                MeshPart part = MeshPart.Create(mf.sharedMesh, mr != null ? mr.sharedMaterial : null, toRoot * mf.transform.localToWorldMatrix);
                if (part == null) continue;
                parts.Add(part);
                totalArea += part.Area;
            }
            foreach (SkinnedMeshRenderer smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                // bind pose: skeleton animation is not ported, so the model is baked as it rests
                MeshPart part = MeshPart.Create(smr.sharedMesh, smr.sharedMaterial, toRoot * smr.transform.localToWorldMatrix);
                if (part == null) continue;
                parts.Add(part);
                totalArea += part.Area;
            }

            if (parts.Count == 0 || totalArea <= 0f)
            {
                spacing = 0.02f;
                bounds = new Bounds();
                Debug.LogWarning("Pixel art splats: nothing to bake (no MeshFilter or SkinnedMeshRenderer under the selection).");
                return Array.Empty<Surfel>();
            }

            float h = Mathf.Sqrt(totalArea / Mathf.Max(1, targetCount));
            spacing = h;
            // scattered points are less regular than a sampled lattice, so they need more overlap or
            // small clusters leave pinholes; the Godot addon bakes meshes with the same 0.9
            float radius = h * MeshRadiusPerSpacing;
            var rng = new Rng(12345);
            var outList = new List<Surfel>(targetCount + 1024);
            bounds = new Bounds();
            bool first = true;

            foreach (MeshPart part in parts)
            {
                for (int t = 0; t + 2 < part.idx.Length; t += 3)
                {
                    int a = part.idx[t], b = part.idx[t + 1], c = part.idx[t + 2];
                    float n = part.TriArea(a, b, c) / (h * h);
                    if (n > 3f)
                    {
                        // big triangle: jittered lattice so walls and floors are evenly covered
                        int k = Mathf.CeilToInt(Mathf.Sqrt(2f * n));
                        for (int i = 0; i < k; i++)
                        for (int j = 0; j < k - i; j++)
                        {
                            float u = (i + 0.2f + 0.6f * rng.Next()) / k;
                            float v = (j + 0.2f + 0.6f * rng.Next()) / k;
                            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                            Emit(part, a, b, c, 1f - u - v, u, v, radius, outList, ref bounds, ref first);
                        }
                    }
                    else
                    {
                        // small triangle: probabilistic, slightly oversampled to avoid pinholes
                        float count = n * 1.35f;
                        int whole = Mathf.FloorToInt(count);
                        if (rng.Next() < count - whole) whole++;
                        for (int s = 0; s < whole; s++)
                        {
                            float u = rng.Next(), v = rng.Next();
                            if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                            Emit(part, a, b, c, 1f - u - v, u, v, radius, outList, ref bounds, ref first);
                        }
                    }
                }
            }

            return outList.ToArray();
        }

        static void Emit(MeshPart part, int a, int b, int c, float wa, float wb, float wc, float radius,
            List<Surfel> into, ref Bounds bounds, ref bool first)
        {
            Vector3 p = part.pos[a] * wa + part.pos[b] * wb + part.pos[c] * wc;
            Vector3 n = part.nrm != null
                ? part.nrm[a] * wa + part.nrm[b] * wb + part.nrm[c] * wc
                : Vector3.zero;
            if (n.sqrMagnitude < 1e-12f)
                n = Vector3.Cross(part.pos[b] - part.pos[a], part.pos[c] - part.pos[a]);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();

            Vector3 color = part.baseColor;
            if (part.uv != null && part.texture != null)
            {
                float u = part.uv[a].x * wa + part.uv[b].x * wb + part.uv[c].x * wc;
                float v = part.uv[a].y * wa + part.uv[b].y * wb + part.uv[c].y * wc;
                u = part.uvScale.x * u + part.uvOffset.x;
                v = part.uvScale.y * v + part.uvOffset.y;
                u -= Mathf.Floor(u);
                v -= Mathf.Floor(v);
                color = Vector3.Scale(color, ToVec(part.texture.GetPixelBilinear(u, v)));
            }
            if (part.col != null)
            {
                Vector3 vc = ToVec(part.col[a]) * wa + ToVec(part.col[b]) * wb + ToVec(part.col[c]) * wc;
                color = Vector3.Scale(color, vc);
            }

            SurfelFlags flags = SurfelFlags.None;
            if (part.emissive) flags |= SurfelFlags.Emissive;
            into.Add(SurfelFactory.Disc(p, n, radius, color, flags));

            if (first)
            {
                bounds = new Bounds(p, Vector3.zero);
                first = false;
            }
            else bounds.Encapsulate(p);
        }

        static Vector3 ToVec(Color c) => new Vector3(c.r, c.g, c.b);

        class MeshPart
        {
            public Vector3[] pos;
            public Vector3[] nrm;
            public Vector2[] uv;
            public Color[] col;
            public int[] idx;
            public Vector3 baseColor = Vector3.one;
            public Texture2D texture;
            public Vector2 uvScale = Vector2.one;
            public Vector2 uvOffset = Vector2.zero;
            public bool emissive;
            public float Area;

            public float TriArea(int a, int b, int c) =>
                0.5f * Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]).magnitude;

            public static MeshPart Create(Mesh mesh, Material material, Matrix4x4 m)
            {
                if (mesh == null) return null;
                Vector3[] verts = mesh.vertices;
                if (verts.Length == 0) return null;

                Vector3[] norms = mesh.normals;
                var part = new MeshPart
                {
                    pos = new Vector3[verts.Length],
                    nrm = norms != null && norms.Length == verts.Length ? new Vector3[verts.Length] : null,
                    uv = mesh.uv != null && mesh.uv.Length == verts.Length ? mesh.uv : null,
                    col = mesh.colors != null && mesh.colors.Length == verts.Length ? mesh.colors : null,
                    idx = mesh.triangles,
                };
                if (part.idx.Length < 3) return null;

                Matrix4x4 normalMatrix = m.inverse.transpose;
                for (int i = 0; i < verts.Length; i++)
                {
                    part.pos[i] = m.MultiplyPoint3x4(verts[i]);
                    if (part.nrm != null) part.nrm[i] = normalMatrix.MultiplyVector(norms[i]).normalized;
                }
                for (int t = 0; t < part.idx.Length; t += 3)
                    part.Area += part.TriArea(part.idx[t], part.idx[t + 1], part.idx[t + 2]);

                if (material != null)
                {
                    if (material.HasProperty("_BaseColor")) part.baseColor = ToVec(material.GetColor("_BaseColor"));
                    else if (material.HasProperty("_Color")) part.baseColor = ToVec(material.GetColor("_Color"));

                    Texture tex = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : material.mainTexture;
                    if (tex is Texture2D t2)
                    {
                        if (t2.isReadable) part.texture = t2;
                        else Debug.LogWarning($"Pixel art splats: texture {t2.name} is not readable, using the material colour only. " +
                                              "Enable Read/Write in its import settings to bake it.", t2);
                    }
                    if (material.HasProperty("_BaseMap"))
                    {
                        part.uvScale = material.GetTextureScale("_BaseMap");
                        part.uvOffset = material.GetTextureOffset("_BaseMap");
                    }
                    else
                    {
                        part.uvScale = material.mainTextureScale;
                        part.uvOffset = material.mainTextureOffset;
                    }
                    if (material.IsKeywordEnabled("_EMISSION") && material.HasProperty("_EmissionColor"))
                    {
                        Color e = material.GetColor("_EmissionColor");
                        part.emissive = Mathf.Max(e.r, Mathf.Max(e.g, e.b)) > 0.2f;
                    }
                }
                return part;
            }
        }
    }
}
