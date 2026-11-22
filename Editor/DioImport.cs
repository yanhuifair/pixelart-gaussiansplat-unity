using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace PixelArtSplats.EditorTools
{
    /// <summary>
    /// Reads a world from the browser project (<c>.dio</c>) or a raw surfel blob (<c>.bytes</c>) and
    /// turns it into a splat cloud asset. The <c>.dio</c> half is the same expansion the Godot addon
    /// does in <c>tools/dio_to_bytes.py</c>, so a scene converts the same way on both.
    /// <para>
    /// The <c>.dio</c> layout: <c>"PSD1"</c>, a uint32 json length, the json, then
    /// <c>count × 3 float32</c> positions, <c>count × 4 int8</c> normals (×127, the 4th byte an
    /// emissive marker the bake used), <c>count × 4 uint8</c> colours, and finally the free splats:
    /// <c>free × 10 float32</c> (position, scale xyz, quaternion xyzw) and <c>free × 4 uint8</c>
    /// (rgb, then kind in 2 bits and opacity in 6).
    /// </para>
    /// </summary>
    public static class DioImport
    {
        const uint Magic = 0x31445350; // "PSD1"

        [Serializable]
        public class Meta
        {
            public int count;
            public int free;
            public float h = 0.02f;
            public float R;
            public float top;
            public float bottom;
            public bool baked;
            public string[] palette;
            public string[] glowPalette;
            public string background;
        }

        // the kinds the browser's scene kit writes on a free splat
        enum FreeKind { Lit = 0, Emissive = 1, Sway = 2, Glow = 3 }

        [MenuItem("Pixel Art Splats/Bake/Import .dio Or .bytes As Splat Cloud…", priority = 41)]
        public static void ImportSelection()
        {
            string path = Selection.assetGUIDs.Length > 0
                ? AssetDatabase.GUIDToAssetPath(Selection.assetGUIDs[0])
                : null;

            if (path == null || !(path.EndsWith(".dio", StringComparison.OrdinalIgnoreCase) ||
                                  path.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase)))
            {
                string picked = EditorUtility.OpenFilePanel("A .dio world or a .bytes blob", "", "dio,bytes");
                if (string.IsNullOrEmpty(picked)) return;
                path = picked;
            }

            string folder = SurfelBaker.Root + "/Baked";
            SplatCloudAsset cloud = Import(path, folder);
            if (cloud != null) EditorGUIUtility.PingObject(cloud);
        }

        /// <summary>Import a world file into <paramref name="folder"/>; returns the cloud it wrote.</summary>
        public static SplatCloudAsset Import(string path, string folder)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"Pixel art splats: no file at {path}");
                return null;
            }

            string name = Path.GetFileNameWithoutExtension(path).Replace(" ", "");
            byte[] blob = File.ReadAllBytes(path);

            Surfel[] surfels;
            float spacing;
            string note;

            if (path.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
            {
                surfels = ParseBytes(blob);
                spacing = 0.02f;
                note = $"imported from {Path.GetFileName(path)}";
            }
            else
            {
                surfels = ParseDio(blob, out Meta meta);
                spacing = meta.h;
                note = $"imported from {Path.GetFileName(path)}" +
                       (meta.baked ? ", baked" : ", live lighting") +
                       (meta.palette != null ? $", {meta.palette.Length} palette colours" : "");
            }

            if (surfels.Length == 0)
            {
                Debug.LogError($"Pixel art splats: {path} holds no surfels.");
                return null;
            }


            Bounds bounds = BoundsOf(surfels);
            return SurfelBaker.CreateCloud(name, surfels, spacing, bounds, note, folder);
        }

        // ---- readers --------------------------------------------------------------

        public static Surfel[] ParseDio(byte[] blob, out Meta meta)
        {
            meta = null;
            if (blob.Length < 16 || BitConverter.ToUInt32(blob, 0) != Magic)
            {
                Debug.LogError("Pixel art splats: that file is not a .dio world (bad magic).");
                return Array.Empty<Surfel>();
            }

            int jsonLength = BitConverter.ToInt32(blob, 4);
            string json = System.Text.Encoding.UTF8.GetString(blob, 8, jsonLength);
            meta = JsonUtility.FromJson<Meta>(json);
            if (meta == null)
            {
                Debug.LogError("Pixel art splats: could not read the .dio metadata.");
                return Array.Empty<Surfel>();
            }

            int n = meta.count;
            int nFree = Mathf.Max(0, meta.free);
            int off = 8 + jsonLength;

            // free splats come last and are laid out after the surfels' three arrays
            // 20 bytes per surfel: 12 of position, 4 of normal, 4 of colour
            int posOff = off;
            int norOff = posOff + n * 12;
            int colOff = norOff + n * 4;
            int freeOff = colOff + n * 4;
            int freeColOff = freeOff + nFree * 40;

            var surfels = new Surfel[n + nFree];
            float radius = Surfel.RadiusPerSpacing * meta.h;
            SurfelFlags baked = meta.baked ? SurfelFlags.Baked : SurfelFlags.None;

            for (int i = 0; i < n; i++)
            {
                int p = posOff + i * 12;
                Vector3 pos = new Vector3(BitConverter.ToSingle(blob, p), BitConverter.ToSingle(blob, p + 4),
                    BitConverter.ToSingle(blob, p + 8));

                int q = norOff + i * 4;
                Vector3 normal = new Vector3((sbyte)blob[q] / 127f, (sbyte)blob[q + 1] / 127f,
                    (sbyte)blob[q + 2] / 127f);

                int c = colOff + i * 4;
                Vector3 colour = new Vector3(blob[c] / 255f, blob[c + 1] / 255f, blob[c + 2] / 255f);

                surfels[i] = SurfelFactory.Disc(pos, normal, radius, colour, baked);
            }

            for (int i = 0; i < nFree; i++)
            {
                int p = freeOff + i * 40;
                Vector3 pos = new Vector3(BitConverter.ToSingle(blob, p), BitConverter.ToSingle(blob, p + 4),
                    BitConverter.ToSingle(blob, p + 8));
                Vector3 scale = new Vector3(BitConverter.ToSingle(blob, p + 12),
                    BitConverter.ToSingle(blob, p + 16), BitConverter.ToSingle(blob, p + 20));
                var rot = new Quaternion(BitConverter.ToSingle(blob, p + 24), BitConverter.ToSingle(blob, p + 28),
                    BitConverter.ToSingle(blob, p + 32), BitConverter.ToSingle(blob, p + 36));

                int c = freeColOff + i * 4;
                Vector3 colour = new Vector3(blob[c] / 255f, blob[c + 1] / 255f, blob[c + 2] / 255f);
                int packed = blob[c + 3];
                var kind = (FreeKind)(packed & 3);
                float alpha = (packed >> 2) / 63f;

                SurfelFlags flags = kind == FreeKind.Glow ? SurfelFlags.Glow : baked;
                if (kind == FreeKind.Emissive) flags |= SurfelFlags.Emissive;
                // a blade is lit as if it faced up, and leans in the wind
                if (kind == FreeKind.Sway) flags |= SurfelFlags.Sway | SurfelFlags.UpNormal;

                surfels[n + i] = SurfelFactory.Scaled(pos, scale, rot, colour, flags, alpha);
            }

            // the browser and Godot are right handed, Unity is left handed: mirror x so a scene comes
            // out the same way round here as it does there
            for (int i = 0; i < surfels.Length; i++) surfels[i] = Mirrored(surfels[i]);
            return surfels;
        }

        public static Surfel[] ParseBytes(byte[] blob)
        {
            if (Marshal.SizeOf<Surfel>() != Surfel.Stride)
            {
                Debug.LogError($"Pixel art splats: Surfel stride is {Marshal.SizeOf<Surfel>()}, " +
                               $"expected {Surfel.Stride}.");
                return Array.Empty<Surfel>();
            }

            int n = blob.Length / Surfel.Stride;
            var surfels = new Surfel[n];
            MemoryMarshal.Cast<byte, Surfel>(blob.AsSpan().Slice(0, n * Surfel.Stride)).CopyTo(surfels);
            return surfels;
        }

        /// <summary>Reflect a surfel across the yz plane, turning a right-handed cloud left handed.</summary>
        static Surfel Mirrored(Surfel s)
        {
            s.pos.x = -s.pos.x;
            s.axis1.x = -s.axis1.x;
            s.axis2.x = -s.axis2.x;
            s.normal.x = -s.normal.x;
            return s;
        }

        public static Bounds BoundsOf(Surfel[] surfels)
        {
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (Surfel s in surfels)
            {
                min = Vector3.Min(min, s.pos);
                max = Vector3.Max(max, s.pos);
            }
            var bounds = new Bounds((min + max) * 0.5f, max - min);
            return bounds;
        }
    }
}
