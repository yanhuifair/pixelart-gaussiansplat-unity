using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixelArtSplats.EditorTools
{
    public delegate float SdfFn(float x, float y, float z);

    /// <summary>One sampled point on a surface: where, which way it faces, how occluded, what it is made of.</summary>
    public struct SurfacePoint
    {
        public Vector3 p;
        public Vector3 n;
        public float ao;
        public int mat;
    }

    /// <summary>
    /// Signed-distance shapes, a builder with a material per primitive, and the sampler that turns
    /// a surface into oriented surfels. Port of src/authoring/sdf.js.
    /// </summary>
    public static class Sdf
    {
        public static SdfFn Sphere(float cx, float cy, float cz, float r) => (x, y, z) =>
        {
            float dx = x - cx, dy = y - cy, dz = z - cz;
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz) - r;
        };

        public static SdfFn Ellipsoid(float cx, float cy, float cz, float rx, float ry, float rz) => (x, y, z) =>
        {
            float px = x - cx, py = y - cy, pz = z - cz;
            float k0 = Mathf.Sqrt((px / rx) * (px / rx) + (py / ry) * (py / ry) + (pz / rz) * (pz / rz));
            float k1 = Mathf.Sqrt(px * px / (rx * rx * rx * rx) + py * py / (ry * ry * ry * ry) + pz * pz / (rz * rz * rz * rz));
            if (k1 < 1e-9f) return -Mathf.Min(rx, Mathf.Min(ry, rz));
            return k0 * (k0 - 1f) / k1;
        };

        /// <summary>Thick line from a to b, radius r1 at a tapering to r2 at b.</summary>
        public static SdfFn Cone(Vector3 a, Vector3 b, float r1, float r2)
        {
            float bx = b.x - a.x, by = b.y - a.y, bz = b.z - a.z;
            float bb = bx * bx + by * by + bz * bz;
            float ax = a.x, ay = a.y, az = a.z;
            return (x, y, z) =>
            {
                float px = x - ax, py = y - ay, pz = z - az;
                float t = bb > 1e-12f ? (px * bx + py * by + pz * bz) / bb : 0f;
                t = t < 0f ? 0f : t > 1f ? 1f : t;
                float dx = px - bx * t, dy = py - by * t, dz = pz - bz * t;
                return Mathf.Sqrt(dx * dx + dy * dy + dz * dz) - (r1 + (r2 - r1) * t);
            };
        }

        public static SdfFn Displace(SdfFn f, SdfFn g) => (x, y, z) => f(x, y, z) + g(x, y, z);

        /// <summary>Rounded box: centre, half sizes, corner radius.</summary>
        public static SdfFn Box(float cx, float cy, float cz, float hx, float hy, float hz, float r = 0f) => (x, y, z) =>
        {
            float qx = Mathf.Abs(x - cx) - hx + r, qy = Mathf.Abs(y - cy) - hy + r, qz = Mathf.Abs(z - cz) - hz + r;
            return Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f) + Mathf.Max(qz, 0f) * Mathf.Max(qz, 0f))
                 + Mathf.Min(Mathf.Max(qx, Mathf.Max(qy, qz)), 0f) - r;
        };

        /// <summary>Upright capped cylinder.</summary>
        public static SdfFn Cyl(float cx, float cy, float cz, float radius, float halfH, float r = 0f) => (x, y, z) =>
        {
            float dx = Mathf.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) - radius + r;
            float dy = Mathf.Abs(y - cy) - halfH + r;
            return Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dy, 0f) * Mathf.Max(dy, 0f))
                 + Mathf.Min(Mathf.Max(dx, dy), 0f) - r;
        };

        public static float SMin(float a, float b, float k)
        {
            if (k <= 0f) return a < b ? a : b;
            float h = Mathf.Max(k - Mathf.Abs(a - b), 0f) / k;
            return Mathf.Min(a, b) - h * h * k * 0.25f;
        }
    }

    /// <summary>A union of shapes, each carrying a material number, plus subtractions.</summary>
    public class Shape
    {
        struct Prim
        {
            public SdfFn f;
            public int mat;
            public float k;
            public bool hasBound;
            public Vector4 bound;
        }

        readonly List<Prim> _adds = new List<Prim>();
        readonly List<Prim> _subs = new List<Prim>();
        Vector4? _pending;

        /// <summary>Bounding sphere for the primitives added next: far props are skipped unevaluated.</summary>
        public Shape Bound(float cx, float cy, float cz, float r)
        {
            _pending = new Vector4(cx, cy, cz, r);
            return this;
        }

        public Shape Bound()
        {
            _pending = null;
            return this;
        }

        public Shape Add(SdfFn f, int mat = 0, float k = 0f)
        {
            _adds.Add(new Prim { f = f, mat = mat, k = k, hasBound = _pending.HasValue, bound = _pending ?? Vector4.zero });
            return this;
        }

        public Shape Sub(SdfFn f, int mat = 0)
        {
            _subs.Add(new Prim { f = f, mat = mat, k = 0f, hasBound = _pending.HasValue, bound = _pending ?? Vector4.zero });
            return this;
        }

        static float Far(Vector4 b, float x, float y, float z)
        {
            float dx = x - b.x, dy = y - b.y, dz = z - b.z;
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz) - b.w;
        }

        public float Dist(float x, float y, float z)
        {
            float d = 1e9f;
            for (int i = 0; i < _adds.Count; i++)
            {
                Prim a = _adds[i];
                if (a.hasBound && Far(a.bound, x, y, z) > d + a.k) continue;
                d = Sdf.SMin(d, a.f(x, y, z), a.k);
            }
            for (int i = 0; i < _subs.Count; i++)
            {
                Prim u = _subs[i];
                if (u.hasBound && Far(u.bound, x, y, z) > 0.05f) continue;
                float s = -u.f(x, y, z);
                if (s > d) d = s;
            }
            return d;
        }

        public int Mat(float x, float y, float z)
        {
            float best = 1e9f;
            int m = 0;
            for (int i = 0; i < _adds.Count; i++)
            {
                Prim a = _adds[i];
                if (a.hasBound && Far(a.bound, x, y, z) > best) continue;
                float d = a.f(x, y, z);
                if (d < best) { best = d; m = a.mat; }
            }
            for (int i = 0; i < _subs.Count; i++)
            {
                Prim s = _subs[i];
                if (s.hasBound && Far(s.bound, x, y, z) > 0.05f) continue;
                if (-s.f(x, y, z) > best - 1e-3f) return s.mat;
            }
            return m;
        }
    }

    /// <summary>
    /// Sample the zero level set on a grid of spacing h inside a box. Splats sit on a regular grid,
    /// each nudged onto the surface with two Newton steps, with the normal from the gradient and an
    /// occlusion term from how much closer the surface is than it should be when stepping out.
    /// </summary>
    public static class SurfaceSampler
    {
        public static List<SurfacePoint> Sample(Shape sh, Vector3 min, Vector3 max, float h)
        {
            const int B = 4;
            float bh = B * h;
            float e = h * 0.5f;
            var points = new List<SurfacePoint>(1 << 16);

            Vector3 Grad(float x, float y, float z) => new Vector3(
                sh.Dist(x + e, y, z) - sh.Dist(x - e, y, z),
                sh.Dist(x, y + e, z) - sh.Dist(x, y - e, z),
                sh.Dist(x, y, z + e) - sh.Dist(x, y, z - e));

            int nx = Mathf.Max(1, Mathf.CeilToInt((max.x - min.x) / bh));
            int ny = Mathf.Max(1, Mathf.CeilToInt((max.y - min.y) / bh));
            int nz = Mathf.Max(1, Mathf.CeilToInt((max.z - min.z) / bh));

            for (int bz = 0; bz < nz; bz++)
            for (int by = 0; by < ny; by++)
            for (int bx = 0; bx < nx; bx++)
            {
                float ox = min.x + bx * bh, oy = min.y + by * bh, oz = min.z + bz * bh;
                // skip blocks that are clearly far from the surface
                if (Mathf.Abs(sh.Dist(ox + bh * 0.5f, oy + bh * 0.5f, oz + bh * 0.5f)) > bh * 1.6f) continue;

                for (int k = 0; k < B; k++)
                for (int j = 0; j < B; j++)
                for (int i = 0; i < B; i++)
                {
                    float x = ox + (i + 0.5f) * h, y = oy + (j + 0.5f) * h, z = oz + (k + 0.5f) * h;
                    float d = sh.Dist(x, y, z);
                    if (Mathf.Abs(d) > h * 1.2f) continue;

                    Vector3 g = Grad(x, y, z);
                    float gl = g.magnitude / (2f * e);
                    if (gl < 1e-9f) gl = 1f;
                    if (Mathf.Abs(d) / gl > h * 0.6f) continue;

                    // two Newton steps onto the surface
                    for (int it = 0; it < 2; it++)
                    {
                        float s = d / (gl * gl) / (2f * e);
                        x -= g.x * s; y -= g.y * s; z -= g.z * s;
                        d = sh.Dist(x, y, z);
                        g = Grad(x, y, z);
                        gl = g.magnitude / (2f * e);
                        if (gl < 1e-9f) gl = 1f;
                    }

                    float inv = 1f / (gl * 2f * e);
                    Vector3 n = g * inv;
                    float nl = n.magnitude;
                    if (nl < 1e-6f) continue;
                    n /= nl;

                    float occ = 0f;
                    for (int s = 0; s < 3; s++)
                    {
                        float step = s == 0 ? 2f : (s == 1 ? 5f : 10f);
                        float off = step * h;
                        float dd = sh.Dist(x + n.x * off, y + n.y * off, z + n.z * off);
                        occ += Mathf.Max(0f, 1f - dd / off);
                    }

                    points.Add(new SurfacePoint
                    {
                        p = new Vector3(x, y, z),
                        n = n,
                        ao = Mathf.Max(0f, 1f - occ / 2.2f),
                        mat = sh.Mat(x, y, z),
                    });
                }
            }

            return points;
        }
    }

    /// <summary>mulberry32: the same stream the scene files are built with.</summary>
    public class Rng
    {
        uint _a;

        public Rng(uint seed) => _a = seed;

        public float Next()
        {
            unchecked
            {
                _a += 0x6d2b79f5u;
                uint t = _a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t >> 7) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296f;
            }
        }

        public float Range(float a, float b) => a + (b - a) * Next();
        public int Int(int n) => Mathf.Min(n - 1, (int)(Next() * n));
    }

    /// <summary>Value noise, port of src/noise.js.</summary>
    public static class Noise
    {
        public static float Hash3(int x, int y, int z)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 2147483647;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                h ^= (int)((uint)h >> 16);
                return (uint)h / 4294967296f;
            }
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        public static float Value3(float x, float y, float z)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float xf = Smooth(x - xi), yf = Smooth(y - yi), zf = Smooth(z - zi);
            float c000 = Hash3(xi, yi, zi), c100 = Hash3(xi + 1, yi, zi);
            float c010 = Hash3(xi, yi + 1, zi), c110 = Hash3(xi + 1, yi + 1, zi);
            float c001 = Hash3(xi, yi, zi + 1), c101 = Hash3(xi + 1, yi, zi + 1);
            float c011 = Hash3(xi, yi + 1, zi + 1), c111 = Hash3(xi + 1, yi + 1, zi + 1);
            float a = Mathf.Lerp(Mathf.Lerp(c000, c100, xf), Mathf.Lerp(c010, c110, xf), yf);
            float b = Mathf.Lerp(Mathf.Lerp(c001, c101, xf), Mathf.Lerp(c011, c111, xf), yf);
            return Mathf.Lerp(a, b, zf);
        }
    }
}
