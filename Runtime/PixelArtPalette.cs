using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixelArtSplats
{
    /// <summary>
    /// The colours the picture is allowed to use, plus the background it is drawn on.
    /// Hex strings, in display space, the way the browser sources list them.
    /// </summary>
    [CreateAssetMenu(menuName = "Pixel Art Splats/Palette", fileName = "Palette")]
    public class PixelArtPalette : ScriptableObject
    {
        [Tooltip("Surface colours. The background must be one of them: the pixel pass never puts the background on the object.")]
        public string[] surface =
        {
            "#0a0a18", "#17152c", "#2b2740", "#4c4a60", "#807e92", "#4a2a26",
            "#1f4a38", "#4f9a4a", "#a8d860", "#c8372d", "#ffcf5a", "#fff1c0",
        };

        [Tooltip("Extra colours only translucent light (beams, halos, smoke) may take.")]
        public string[] glow = { "#4e3f3a", "#8a7356" };

        [Tooltip("Hex, display space. Should appear in surface.")]
        public string background = "#0a0a18";

        /// <summary>Surface colours then glow colours, deduplicated, display space.</summary>
        public void Resolve(List<Color> into, out int surfaceCount, out Color background8)
        {
            into.Clear();
            foreach (string hex in surface)
                if (TryParse(hex, out Color c) && !Contains(into, c)) into.Add(c);
            surfaceCount = into.Count;
            foreach (string hex in glow)
                if (TryParse(hex, out Color c) && !Contains(into, c)) into.Add(c);
            if (!TryParse(background, out background8)) background8 = Color.black;
        }

        static bool Contains(List<Color> list, Color c)
        {
            foreach (Color x in list)
                if (Mathf.Abs(x.r - c.r) < 1e-3f && Mathf.Abs(x.g - c.g) < 1e-3f && Mathf.Abs(x.b - c.b) < 1e-3f)
                    return true;
            return false;
        }

        // brightness levels a surface can take: light is worked out per splat, so one albedo becomes
        // several palette entries. The same numbers the shader uses.
        const float AmbientLow = 0.36f;
        const float AmbientHigh = 0.58f;
        const float Diffuse = 0.62f;

        /// <summary>
        /// A palette for a cloud that did not come with one, the way the viewer does it for models:
        /// k-means over the surface colours at the brightness levels the lighting will produce.
        /// <paramref name="lit"/> is a lamp's colour, so its warm tones get entries of their own.
        /// <para>The Godot addon has the same function with the same constants: the two must agree
        /// colour for colour, or the same model comes out differently in each engine.</para>
        /// </summary>
        public static string[] Extract(IReadOnlyList<Vector3> colours, int k, float exposure = 1f,
            Vector3 lit = default)
        {
            k = Mathf.Clamp(k, 2, 64);
            if (colours == null || colours.Count == 0) return new string[0];

            int step = Mathf.Max(1, colours.Count / 9000);
            bool hasLit = lit.x + lit.y + lit.z > 0.001f;
            double[] levels = { AmbientLow, 0.55, 0.85, AmbientHigh + Diffuse };

            var s = new List<double>(9000 * 6);
            for (int i = 0; i < colours.Count; i += step)
            {
                double r = colours[i].x, g = colours[i].y, b = colours[i].z;
                foreach (double level in levels)
                {
                    s.Add(Math.Min(1.0, r * level * exposure));
                    s.Add(Math.Min(1.0, g * level * exposure));
                    s.Add(Math.Min(1.0, b * level * exposure));
                }
                if (hasLit && i % (step * 3) == 0)
                {
                    s.Add(Math.Min(1.0, r * 0.7 * exposure + (r * 0.85 + 0.1) * lit.x * 0.45));
                    s.Add(Math.Min(1.0, g * 0.7 * exposure + (g * 0.85 + 0.1) * lit.y * 0.45));
                    s.Add(Math.Min(1.0, b * 0.7 * exposure + (b * 0.85 + 0.1) * lit.z * 0.45));
                }
            }

            int n = s.Count / 3;
            if (n == 0) return new string[0];

            // initial centres: a brightness histogram, so the k spots are quantiles of the samples
            const int Bins = 2048;
            var weight = new double[Bins];
            var sumR = new double[Bins];
            var sumG = new double[Bins];
            var sumB = new double[Bins];
            for (int j = 0; j < n; j++)
            {
                double r = s[j * 3], g = s[j * 3 + 1], b = s[j * 3 + 2];
                int bin = Mathf.Clamp((int)((r + g * 2.0 + b) * 0.25 * 2047.0), 0, Bins - 1);
                weight[bin] += 1.0;
                sumR[bin] += r;
                sumG[bin] += g;
                sumB[bin] += b;
            }

            var centres = new double[k * 3];
            double seen = 0.0;
            int binAt = 0;
            for (int c = 0; c < k; c++)
            {
                double target = (c + 0.5) / k * n;
                while (binAt < Bins - 1 && seen + weight[binAt] < target)
                {
                    seen += weight[binAt];
                    binAt++;
                }
                if (weight[binAt] > 0.0)
                {
                    centres[c * 3] = sumR[binAt] / weight[binAt];
                    centres[c * 3 + 1] = sumG[binAt] / weight[binAt];
                    centres[c * 3 + 2] = sumB[binAt] / weight[binAt];
                }
                else
                {
                    centres[c * 3] = 0.5;
                    centres[c * 3 + 1] = 0.5;
                    centres[c * 3 + 2] = 0.5;
                }
            }

            // Lloyd's algorithm: each sample joins the nearest centre, each centre takes the mean
            var acc = new double[k * 3];
            var count = new int[k];
            for (int pass = 0; pass < 10; pass++)
            {
                System.Array.Clear(acc, 0, acc.Length);
                System.Array.Clear(count, 0, k);
                for (int j = 0; j < n; j++)
                {
                    double r = s[j * 3], g = s[j * 3 + 1], b = s[j * 3 + 2];
                    int best = 0;
                    double bestD = 1e30;
                    for (int c = 0; c < k; c++)
                    {
                        double dr = r - centres[c * 3];
                        double dg = g - centres[c * 3 + 1];
                        double db = b - centres[c * 3 + 2];
                        double d = dr * dr + dg * dg + db * db;
                        if (d < bestD) { bestD = d; best = c; }
                    }
                    acc[best * 3] += r;
                    acc[best * 3 + 1] += g;
                    acc[best * 3 + 2] += b;
                    count[best]++;
                }
                for (int c = 0; c < k; c++)
                {
                    if (count[c] == 0) continue;
                    centres[c * 3] = acc[c * 3] / count[c];
                    centres[c * 3 + 1] = acc[c * 3 + 1] / count[c];
                    centres[c * 3 + 2] = acc[c * 3 + 2] / count[c];
                }
            }

            // round half up, like the viewer's Math.round
            var hexes = new List<string>(k);
            for (int c = 0; c < k; c++)
            {
                string hex = "#" +
                    ((int)Math.Floor(Math.Clamp(centres[c * 3], 0.0, 1.0) * 255.0 + 0.5)).ToString("x2") +
                    ((int)Math.Floor(Math.Clamp(centres[c * 3 + 1], 0.0, 1.0) * 255.0 + 0.5)).ToString("x2") +
                    ((int)Math.Floor(Math.Clamp(centres[c * 3 + 2], 0.0, 1.0) * 255.0 + 0.5)).ToString("x2");
                if (!hexes.Contains(hex)) hexes.Add(hex);
            }
            return hexes.ToArray();
        }

        public static bool TryParse(string hex, out Color c)
        {
            c = Color.black;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out c);
        }

    }
}
