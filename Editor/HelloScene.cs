using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixelArtSplats.EditorTools
{
    /// <summary>What a material paints at a point: its colour, and whether it is a light or foliage.</summary>
    public struct PaintResult
    {
        public Vector3 color;
        public bool emissive;
        public bool leaf;
    }

    /// <summary>A scene being built: the shape, the island it stands on, the random stream.</summary>
    public class SceneContext
    {
        public readonly Shape s = new Shape();
        public float R, rFlat;
        public Rng rnd;
        public float top = 0.3f;
        public Func<float, float, float> height = (x, z) => 0f;
        public Func<float, bool, float> clear = (a, tall) => 0f;

        public SceneContext(float r, float flat, Func<float, bool, float> clear, Rng rnd)
        {
            R = r;
            rFlat = flat;
            this.clear = clear;
            this.rnd = rnd;
        }

        /// <summary>A spot on the island, kept clear of whatever the clear() function protects.</summary>
        public Vector2 Place(float a, float extra = 0.2f, bool tall = true)
        {
            float r = Mathf.Min(R - 0.16f, clear(a, tall) + extra);
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        public void Grow(float y) => top = Mathf.Max(top, y);
    }

    /// <summary>
    /// The island, its props, free splats, and the bake step. Port of src/authoring/island.js
    /// and src/authoring/scene-kit.js.
    /// </summary>
    public static class IslandKit
    {
        /// <summary>
        /// A slab whose top is flat where something stands and gets bumpier towards the rim, on a
        /// rough rock underside with hanging spikes and a few loose rocks drifting around it.
        /// </summary>
        public static void Island(SceneContext c, int mTop, int mUnder, float bump = 0.02f,
            float rise = 0f, float ripple = 0f, float rough = 0.09f, float topY = 0f)
        {
            Shape s = c.s;
            Rng rnd = c.rnd;
            float R = c.R, rFlat = c.rFlat;
            float seed = rnd.Next() * 50f;

            float Outline(float x, float z)
            {
                float a = Mathf.Atan2(z, x);
                return R * (1f + 0.16f * (Noise.Value3(Mathf.Cos(a) * 1.7f + seed, Mathf.Sin(a) * 1.7f + seed, 3f) - 0.5f));
            }

            c.height = (x, z) =>
            {
                float r = Mathf.Sqrt(x * x + z * z);
                float e = Colours.Step(rFlat, rFlat + 0.3f, r);
                return topY
                     + e * (bump * (Noise.Value3(x * 5f + seed, 1f, z * 5f) - 0.5f) * 2f
                          + rise * Colours.Step(rFlat + 0.1f, R, r) * Noise.Value3(x * 1.4f + seed, 7f, z * 1.4f))
                     + ripple * (0.3f + 0.7f * e) * Mathf.Sin(x * 30f + 4f * Noise.Value3(x * 2.5f, 2f, z * 2.5f) + z * 7f);
            };

            s.Add((x, y, z) =>
            {
                const float hh = 0.16f;
                float dx = Mathf.Sqrt(x * x + z * z) - Outline(x, z) + 0.07f;
                float dy = Mathf.Abs(y - c.height(x, z) + hh) - hh + 0.07f;
                return Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f))
                     + Mathf.Min(Mathf.Max(dx, dy), 0f) - 0.07f;
            }, mTop);

            SdfFn core = Sdf.Ellipsoid(0f, topY - 0.12f, 0f, 0.92f * R, 0.78f * R, 0.92f * R);
            s.Add((x, y, z) => Mathf.Max(
                    core(x, y, z) - rough * (Noise.Value3(x * 4f + seed, y * 4f, z * 4f) - 0.5f) * 2f
                    - 0.04f * Noise.Value3(x * 11f, y * 11f, z * 11f),
                    y - topY + 0.12f), mUnder, 0.09f);

            for (int i = 0; i < 8; i++)
            {
                float a = rnd.Next() * Mathf.PI * 2f, r = Mathf.Sqrt(rnd.Next()) * 0.62f * R;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                float y0 = topY - 0.12f - 0.78f * R * Mathf.Sqrt(1f - (r / (0.92f * R)) * (r / (0.92f * R)));
                float len = (0.2f + rnd.Next() * 0.5f) * R * 0.6f;
                s.Bound(x, y0 - len / 2f, z, len / 2f + 0.35f);
                s.Add(Sdf.Cone(new Vector3(x, y0 + 0.25f, z),
                    new Vector3(x + (rnd.Next() - 0.5f) * 0.15f, y0 - len, z + (rnd.Next() - 0.5f) * 0.15f),
                    0.16f, 0.012f), mUnder, 0.08f);
            }

            for (int i = 0; i < 6; i++)
            {
                float a = rnd.Next() * Mathf.PI * 2f, r = R + 0.18f + rnd.Next() * 0.35f;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r, y = topY - 0.75f + rnd.Next() * 0.9f;
                float q = 0.05f + rnd.Next() * 0.09f;
                s.Bound(x, y, z, q * 2f);
                s.Add(Sdf.Displace(Sdf.Ellipsoid(x, y, z, q * 1.2f, q * 0.85f, q),
                    (px, py, pz) => -0.03f * Noise.Value3(px * 12f, py * 12f, pz * 12f)), mUnder);
            }
            s.Bound();
        }

        public static void Rocks(SceneContext c, int mat, int n, float size = 0.1f)
        {
            Shape s = c.s;
            Rng rnd = c.rnd;
            for (int i = 0; i < n; i++)
            {
                Vector2 spot = c.Place(rnd.Next() * Mathf.PI * 2f, 0.1f + rnd.Next() * 0.7f, false);
                float r = size * (0.5f + rnd.Next());
                float y = c.height(spot.x, spot.y);
                s.Bound(spot.x, y, spot.y, r * 1.8f);
                s.Add(Sdf.Displace(Sdf.Ellipsoid(spot.x, y + r * 0.25f, spot.y, r * 1.3f, r * 0.8f, r),
                    (px, py, pz) => -0.3f * r * Noise.Value3(px * 11f, py * 11f, pz * 11f)), mat, 0.02f);
            }
            s.Bound();
        }

        public static void Tufts(SceneContext c, int mat, int n, float hgt = 0.2f)
        {
            Shape s = c.s;
            Rng rnd = c.rnd;
            for (int i = 0; i < n; i++)
            {
                Vector2 spot = c.Place(rnd.Next() * Mathf.PI * 2f, 0.12f + rnd.Next() * 0.6f, false);
                float y = c.height(spot.x, spot.y);
                s.Bound(spot.x, y + hgt / 2f, spot.y, hgt);
                for (int k = 0; k < 6; k++)
                {
                    float a = rnd.Next() * Mathf.PI * 2f, l = 0.03f + rnd.Next() * 0.05f, h = hgt * (0.55f + rnd.Next() * 0.6f);
                    s.Add(Sdf.Cone(new Vector3(spot.x, y - 0.02f, spot.y),
                        new Vector3(spot.x + Mathf.Cos(a) * l, y + h, spot.y + Mathf.Sin(a) * l), 0.009f, 0.002f), mat);
                }
            }
            s.Bound();
        }
    }

    /// <summary>
    /// Free splats: things easier to place one at a time. A grass blade is a single stretched splat,
    /// a flower a dot, a glow a few nested translucent discs. Port of src/authoring/scene-kit.js.
    /// </summary>
    public class FreeKit
    {
        public readonly List<Surfel> list = new List<Surfel>(4096);

        readonly SceneContext _c;
        readonly Rng _rnd;

        public FreeKit(SceneContext c)
        {
            _c = c;
            _rnd = c.rnd;
        }

        public void Push(Vector3 p, Vector3 scale, Quaternion q, Vector3 col,
            SurfelFlags kind = SurfelFlags.None, float alpha = 1f)
        {
            list.Add(SurfelFactory.Scaled(p, scale, q, col, kind, alpha));
        }

        /// <summary>A blade: a dark lower half and a lighter, leaning tip.</summary>
        public void Blade(float x, float y, float z, float h, float w, Vector3 dark, Vector3 light)
        {
            float a = _rnd.Next() * Mathf.PI * 2f;
            float lean = 0.15f + _rnd.Next() * 0.3f;
            Vector3 d = new Vector3(Mathf.Cos(a) * lean, 1f, Mathf.Sin(a) * lean).normalized;
            Quaternion q = SurfelFactory.FromTo(Vector3.up, d);
            SurfelFlags kind = SurfelFlags.Sway | SurfelFlags.UpNormal | SurfelFlags.TwoSided;
            Push(new Vector3(x + d.x * h * 0.25f, y + h * 0.25f, z + d.z * h * 0.25f),
                new Vector3(w, h * 0.3f, w), q, dark, kind);
            Push(new Vector3(x + d.x * h * 0.7f, y + h * 0.68f, z + d.z * h * 0.7f),
                new Vector3(w * 0.8f, h * 0.3f, w * 0.8f), q, light, kind);
        }

        public void Grass(int n, Func<float, float, bool> ok, Vector2 h, float w,
            Vector3[][] colors, float r)
        {
            for (int i = 0, tries = 0; i < n && tries < n * 60; tries++)
            {
                float a = _rnd.Next() * Mathf.PI * 2f, rr = Mathf.Sqrt(_rnd.Next()) * r;
                float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr;
                if (!ok(x, z)) continue;
                float y = _c.height(x, z);
                if (_c.s.Dist(x, y + 0.06f, z) < 0.035f) continue;
                Vector3[] pair = colors[_rnd.Int(colors.Length)];
                Blade(x, y - 0.01f, z, h.x + _rnd.Next() * (h.y - h.x), w, pair[0], pair[1]);
                i++;
            }
        }

        public void Dots(int n, Func<float, float, bool> ok, Vector2 yRange, float size,
            Vector3[] colors, float r, SurfelFlags kind = SurfelFlags.None)
        {
            for (int i = 0, tries = 0; i < n && tries < n * 60; tries++)
            {
                float a = _rnd.Next() * Mathf.PI * 2f, rr = Mathf.Sqrt(_rnd.Next()) * r;
                float x = Mathf.Cos(a) * rr, z = Mathf.Sin(a) * rr;
                if (!ok(x, z)) continue;
                float y0 = _c.height(x, z);
                if (_c.s.Dist(x, y0 + 0.06f, z) < 0.035f) continue;
                Vector3 p = new Vector3(x, y0 + yRange.x + _rnd.Next() * (yRange.y - yRange.x), z);
                Push(p, Vector3.one * size, Quaternion.identity, colors[_rnd.Int(colors.Length)],
                    kind | SurfelFlags.TwoSided);
                i++;
            }
        }

        /// <summary>Soft halo: nested translucent discs that dither denser towards the middle.</summary>
        public void Glow(float x, float y, float z, float r, Vector3 col)
        {
            float[] k = { 1f, 0.62f, 0.32f };
            float[] alpha = { 0.14f, 0.3f, 0.55f };
            for (int i = 0; i < 3; i++)
                Push(new Vector3(x, y, z), Vector3.one * (r * k[i]), Quaternion.identity, col, SurfelFlags.Glow, alpha[i]);
        }
    }

    /// <summary>The starter scene: an island, a tree, a lantern, some grass. Port of src/authoring/scenes/hello.js.</summary>
    public static class HelloScene
    {
        public const int Grass = 0, Rock = 1, Trunk = 2, Leaves = 3, Post = 4, Lamp = 5;

        public static readonly string[] Palette =
        {
            "#0a0a18", "#17152c", "#2b2740", "#4c4a60", "#807e92", "#4a2a26",
            "#1f4a38", "#4f9a4a", "#a8d860", "#c8372d", "#ffcf5a", "#fff1c0",
        };

        public static readonly string[] GlowPalette = { "#4e3f3a", "#8a7356" };
        public const string Background = "#0a0a18";

        public class Result
        {
            public Surfel[] surfels;
            public LightBaker.Lamp[] lamps;
            public float spacing;
            public Bounds bounds;
            public float radius;
        }

        public static Result Build(uint seed = 1234u, float h = 0.0215f)
        {
            Rng rnd = new Rng(seed);
            var c = new SceneContext(1.3f, 0.3f, (a, tall) => 0.7f, rnd);
            FreeKit free = new FreeKit(c);
            Shape s = c.s;

            IslandKit.Island(c, Grass, Rock, bump: 0.03f);

            s.Add(Sdf.Cone(new Vector3(0f, -0.05f, 0f), new Vector3(0f, 0.75f, 0f), 0.09f, 0.05f), Trunk);
            float[][] leaves =
            {
                new[] { 0.05f, 1.0f, 0.0f, 0.38f },
                new[] { -0.22f, 0.85f, 0.12f, 0.28f },
                new[] { 0.28f, 0.82f, -0.1f, 0.26f },
            };
            foreach (float[] l in leaves)
                s.Add(Sdf.Displace(Sdf.Ellipsoid(l[0], l[1], l[2], l[3], l[3] * 0.8f, l[3]),
                    (px, py, pz) => -0.06f * (Noise.Value3(px * 9f, py * 9f, pz * 9f) - 0.5f)), Leaves, 0.08f);
            c.Grow(1.45f);

            // a lantern on a post, with a point light and a soft halo
            s.Add(Sdf.Cone(new Vector3(0.6f, -0.03f, 0.35f), new Vector3(0.6f, 0.5f, 0.35f), 0.022f, 0.016f), Post);
            s.Add(Sdf.Box(0.6f, 0.56f, 0.35f, 0.05f, 0.06f, 0.05f, 0.01f), Lamp);
            var lamps = new[]
            {
                new LightBaker.Lamp
                {
                    pos = new Vector3(0.6f, 0.56f, 0.35f),
                    color = new Vector3(1.0f, 0.72f, 0.32f),
                    power = 1.0f,
                },
            };
            free.Glow(0.6f, 0.56f, 0.35f, 0.28f, Colours.Hex("#ffcf5a"));

            IslandKit.Rocks(c, Rock, 5, 0.1f);

            // 1500 grass blades (dark base, light tip) and a few flowers
            free.Grass(1500, (x, z) => true, new Vector2(0.08f, 0.2f), 0.011f,
                new[]
                {
                    new[] { Colours.Hex("#1f4a38"), Colours.Hex("#4f9a4a") },
                    new[] { Colours.Hex("#4f9a4a"), Colours.Hex("#a8d860") },
                }, 1.25f);
            free.Dots(40, (x, z) => true, new Vector2(0.12f, 0.22f), 0.016f,
                new[] { Colours.Hex("#fff1c0"), Colours.Hex("#c8372d") }, 1.2f);

            // colours, once per surfel
            PaintResult Paint(int m, float x, float y, float z, Vector3 n)
            {
                switch (m)
                {
                    case Lamp:
                        return new PaintResult { color = Colours.Hex("#ffcf5a"), emissive = true };
                    case Post:
                        return new PaintResult { color = Colours.Hex("#2b2740") };
                    case Trunk:
                        return new PaintResult { color = Colours.Hex("#4a2a26") * (0.85f + 0.3f * Noise.Value3(x * 30f, y * 5f, z * 30f)) };
                    case Leaves:
                        return new PaintResult
                        {
                            color = Noise.Value3(x * 12f, y * 12f, z * 12f) > 0.55f ? Colours.Hex("#a8d860") : Colours.Hex("#4f9a4a"),
                            leaf = true,
                        };
                    case Rock:
                        return new PaintResult { color = Colours.Hex("#807e92") * (0.8f + 0.4f * Noise.Value3(x * 8f, y * 10f, z * 8f)) };
                    default:
                        return new PaintResult
                        {
                            color = n.y < 0.5f
                                ? Colours.Hex("#4a2a26")
                                : Colours.Mix(Colours.Hex("#1f4a38"), Colours.Hex("#4f9a4a"),
                                    Colours.Step(0.3f, 0.7f, Noise.Value3(x * 4f, 2f, z * 4f))),
                        };
                }
            }

            var lighting = new LightBaker(s, lamps, Palette);
            float ext = c.R + 0.75f;
            float bottom = -0.3f - c.R * 1.25f;
            float topY = c.top + 0.15f;

            List<SurfacePoint> surf = SurfaceSampler.Sample(s, new Vector3(-ext, bottom, -ext),
                new Vector3(ext, topY, ext), h);

            var outList = new List<Surfel>(surf.Count + free.list.Count);
            float radius = h * Surfel.RadiusPerSpacing;
            Bounds bounds = new Bounds();

            for (int i = 0; i < surf.Count; i++)
            {
                SurfacePoint sp = surf[i];
                PaintResult paint = Paint(sp.mat, sp.p.x, sp.p.y, sp.p.z, sp.n);
                Vector3 k = paint.color;
                if (!paint.emissive)
                {
                    // darker towards the bottom of the island
                    if (sp.p.y < -0.2f) k *= 1f - 0.45f * Colours.Step(-0.2f, -c.R, sp.p.y);
                    k = lighting.Shade(k, sp.p, sp.n, sp.ao, paint.leaf);
                }

                SurfelFlags flags = SurfelFlags.Baked;
                if (paint.emissive) flags |= SurfelFlags.Emissive;
                Surfel sf = SurfelFactory.Disc(sp.p, sp.n, radius, k, flags);
                if (i == 0) bounds = new Bounds(sf.pos, Vector3.zero);
                bounds.Encapsulate(sf.pos);
                outList.Add(sf);
            }

            for (int i = 0; i < free.list.Count; i++)
            {
                outList.Add(free.list[i]);
                bounds.Encapsulate(free.list[i].pos);
            }

            return new Result
            {
                surfels = outList.ToArray(),
                lamps = lamps,
                spacing = h,
                bounds = bounds,
                radius = c.R,
            };
        }
    }
}
