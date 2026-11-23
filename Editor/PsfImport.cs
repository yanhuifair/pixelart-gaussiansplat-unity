using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PixelArtSplats.EditorTools
{
    /// <summary>
    /// Reads a <c>.psf</c> — a converted model from the browser project — into a splat cloud, and
    /// builds a palette for it out of the model's own colours.
    /// <para>Unlike a <c>.dio</c> scene, a model is not baked: its colour is albedo and the renderer
    /// lights it. So the cloud must not be marked baked, and it wants a palette that covers the
    /// brightness the lighting will produce — hence <see cref="PixelArtPalette.Extract"/>.</para>
    /// </summary>
    public static class PsfImport
    {
        const uint Magic = 0x31465350;   // "PSF1"
        const int HeaderBytes = 40;

        public struct Model
        {
            public Surfel[] surfels;
            public int count;
            public float spacing;
            public Vector3 size;         // longest side is 2 units, as the viewer scales it
            public bool skinned;
        }

        /// <summary>A material flag on a surfel: 1 emissive, 2 two-sided.</summary>
        const int Emissive = 1;
        const int TwoSided = 2;

        public static Model Parse(byte[] blob)
        {
            var model = new Model();
            if (blob == null || blob.Length < HeaderBytes) return model;
            if (BitConverter.ToUInt32(blob, 0) != Magic)
            {
                Debug.LogError("Pixel art splats: not a .psf file (bad magic).");
                return model;
            }

            int count = (int)BitConverter.ToUInt32(blob, 4);
            float spacing = BitConverter.ToSingle(blob, 8);
            var box = new Vector3[2];
            for (int i = 0; i < 6; i++)
            {
                float v = BitConverter.ToSingle(blob, 12 + i * 4);
                box[i / 3][i % 3] = v;
            }
            uint flags = BitConverter.ToUInt32(blob, 36);
            long needed = HeaderBytes + (long)count * 20 + ((flags & 1) != 0 ? (long)count * 8 : 0);
            if (blob.Length < needed)
            {
                Debug.LogError($"Pixel art splats: .psf truncated ({blob.Length} bytes, {needed} expected).");
                return model;
            }

            // the viewer recentres the model and scales its longest side to two units
            Vector3 centre = (box[0] + box[1]) * 0.5f;
            Vector3 extent = box[1] - box[0];
            float unit = 2f / Mathf.Max(Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z)), 1e-9f);
            float h = spacing * unit;
            float radius = 0.66f * h;

            int posAt = HeaderBytes;
            int norAt = HeaderBytes + count * 12;
            int colAt = HeaderBytes + count * 16;

            var surfels = new Surfel[count];
            for (int i = 0; i < count; i++)
            {
                var p = new Vector3(
                    BitConverter.ToSingle(blob, posAt + i * 12),
                    BitConverter.ToSingle(blob, posAt + i * 12 + 4),
                    BitConverter.ToSingle(blob, posAt + i * 12 + 8));
                p = Vector3.Scale(p - centre, Vector3.one * unit);

                var n = new Vector3(
                    (sbyte)blob[norAt + i * 4], (sbyte)blob[norAt + i * 4 + 1],
                    (sbyte)blob[norAt + i * 4 + 2]) / 127f;
                n = n.sqrMagnitude > 1e-9f ? n.normalized : Vector3.up;
                byte material = blob[norAt + i * 4 + 3];

                int c = colAt + i * 4;
                var colour = new Vector3(blob[c] / 255f, blob[c + 1] / 255f, blob[c + 2] / 255f);

                // a disc lying on the surface, the way model.js builds one
                Quaternion q = Quaternion.FromToRotation(Vector3.forward, n);
                var kind = SurfelFlags.None;
                if ((material & Emissive) != 0) kind |= SurfelFlags.Emissive;
                if ((material & TwoSided) != 0) kind |= SurfelFlags.TwoSided;

                Surfel s = default;
                s.pos = p;
                s.axis1 = q * new Vector3(radius, 0f, 0f);
                s.axis2 = q * new Vector3(0f, radius, 0f);
                s.normal = n;
                s.color = colour;
                s.Kind = kind;
                s.Alpha = 1f;   // the browser dims the alpha to smuggle the flags through; we have bits
                surfels[i] = s;
            }

            // the browser and Godot are right handed, Unity is left handed
            for (int i = 0; i < surfels.Length; i++) surfels[i] = DioImport.Mirrored(surfels[i]);

            model.surfels = surfels;
            model.count = count;
            model.spacing = h;
            model.size = extent * unit;
            model.skinned = (flags & 1) != 0;
            return model;
        }

        public static Model Import(string psfPath, string folder, string name, int colours = 28,
            float exposure = 1f, Color? lit = null, string islandDio = null)
        {
            byte[] blob = File.ReadAllBytes(psfPath);
            Model model = Parse(blob);
            if (model.surfels == null || model.surfels.Length == 0) return model;

            Directory.CreateDirectory(folder);
            string note = $"model, imported from {Path.GetFileName(psfPath)}: {model.count} surfels, " +
                          $"lit live (not baked)";
            SplatCloudAsset cloud = SurfelBaker.CreateCloud(name, model.surfels, model.spacing,
                DioImport.BoundsOf(model.surfels), note, folder);

            // the palette comes out of the model's own colours
            var rgb = new List<Vector3>(model.count);
            foreach (Surfel s in model.surfels) rgb.Add(s.color);
            // the viewer lifts a model's palette with the first lamp of the island it stands on
            Color? lampColor = lit;
            if (lampColor == null && !string.IsNullOrEmpty(islandDio))
                lampColor = FirstLight(islandDio);
            Vector3 lamp = lampColor.HasValue
                ? new Vector3(lampColor.Value.r, lampColor.Value.g, lampColor.Value.b)
                : Vector3.zero;
            string[] hexes = PixelArtPalette.Extract(rgb, colours, exposure, lamp);

            var surface = new List<string> { "#07080c" };
            surface.AddRange(hexes);
            PixelArtPalette palette = SurfelBaker.CreatePalette(name + "Palette", surface.ToArray(),
                new string[0], "#07080c", folder);
            palette.name = name + "Palette";

            Debug.Log($"Pixel art splats: {psfPath} -> {model.count} surfels, " +
                      $"{hexes.Length} colours from the model's own palette");
            return model;
        }

        /// <summary>The colour of the first lamp a .dio scene declares, or null.</summary>
        static Color? FirstLight(string dioPath)
        {
            if (!File.Exists(dioPath)) return null;
            DioImport.ParseDio(File.ReadAllBytes(dioPath), out DioImport.Meta meta);
            if (meta?.lights == null || meta.lights.Length == 0) return null;
            float[] c = meta.lights[0].color;
            return new Color(c[0], c[1], c[2]);
        }

        [MenuItem("Pixel Art Splats/Import .psf Model As Splat Cloud…", priority = 31)]
        static void ImportFromDialog()
        {
            string path = EditorUtility.OpenFilePanel("model.psf from the browser project", "", "psf");
            if (string.IsNullOrEmpty(path)) return;
            string name = Path.GetFileNameWithoutExtension(path);
            Import(path, SurfelBaker.Root + "/Imported", name);
        }
    }
}
