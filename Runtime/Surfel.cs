using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PixelArtSplats
{
    /// <summary>What a surfel is made of. Lives in the low bits of <see cref="Surfel.flags"/>.</summary>
    [Flags]
    public enum SurfelFlags : uint
    {
        None = 0,
        /// <summary>Colour is its own light: no shading, no fog.</summary>
        Emissive = 1u << 0,
        /// <summary>Visible from behind (leaves, flowers, strokes).</summary>
        TwoSided = 1u << 1,
        /// <summary>Translucent light: halo, beam, smoke. Drawn in the second pass, blended.</summary>
        Glow = 1u << 2,
        /// <summary>Colour already holds its light (baked scene). Only fog is added.</summary>
        Baked = 1u << 3,
        /// <summary>Lit as if it faced up (grass blades, leaflets).</summary>
        UpNormal = 1u << 4,
        /// <summary>Swayed by the wind on the GPU (grass blades).</summary>
        Sway = 1u << 5,
    }

    /// <summary>
    /// A surfel: a flat disc lying on a surface. 64 bytes, uploaded as-is.
    /// <para>axis1 and axis2 are the two wide axes of the disc, scaled so their length is the radius
    /// (so the disc spans 2 * radius across). normal is the thin axis: the surface normal.</para>
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Surfel
    {
        public Vector3 pos;
        public Vector3 axis1;
        public Vector3 axis2;
        public Vector3 normal;
        public Vector3 color;   // display space (what the palette holds), not linear
        public uint flags;      // SurfelFlags in bits 0..7, alpha in bits 8..15

        /// <summary>Bytes per surfel in the GPU buffer. Keep in sync with the shader struct.</summary>
        public const int Stride = 64;

        /// <summary>Splat radius as a fraction of the surfel spacing, so neighbours overlap.</summary>
        public const float RadiusPerSpacing = 0.66f;

        public float Alpha
        {
            get => ((flags >> 8) & 0xFFu) / 255f;
            set => flags = (flags & ~0xFF00u) | ((uint)Mathf.Clamp(Mathf.RoundToInt(value * 255f), 0, 255) << 8);
        }

        public SurfelFlags Kind
        {
            get => (SurfelFlags)(flags & 0xFFu);
            set => flags = (flags & ~0xFFu) | ((uint)value & 0xFFu);
        }

        public bool Has(SurfelFlags f) => (flags & (uint)f) != 0;
    }

    /// <summary>Builds surfels: a disc from a normal, a disc with explicit axes, a flat quad lying on the ground.</summary>
    public static class SurfelFactory
    {
        /// <summary>Any pair of unit vectors perpendicular to n, so the disc's orientation is free.</summary>
        public static void Basis(Vector3 n, out Vector3 t, out Vector3 b)
        {
            n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            Vector3 up = Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up;
            t = Vector3.Cross(up, n).normalized;
            b = Vector3.Cross(n, t).normalized;
        }

        /// <summary>A disc of the given radius lying on the surface whose normal is n.</summary>
        public static Surfel Disc(Vector3 pos, Vector3 n, float radius, Vector3 color, SurfelFlags kind = SurfelFlags.None)
        {
            Basis(n, out Vector3 t, out Vector3 b);
            Surfel s = default;
            s.pos = pos;
            s.axis1 = t * radius;
            s.axis2 = b * radius;
            s.normal = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            s.color = color;
            s.Kind = kind;
            s.Alpha = 1f;
            return s;
        }

        /// <summary>A disc with explicit axes (a blade, a leaf, a stretched streak).</summary>
        public static Surfel FromAxes(Vector3 pos, Vector3 axis1, Vector3 axis2, Vector3 normal, Vector3 color,
            SurfelFlags kind = SurfelFlags.None, float alpha = 1f)
        {
            Surfel s = default;
            s.pos = pos;
            s.axis1 = axis1;
            s.axis2 = axis2;
            s.normal = normal.sqrMagnitude > 1e-12f ? normal.normalized : Vector3.Cross(axis1, axis2).normalized;
            s.color = color;
            s.Kind = kind;
            s.Alpha = alpha;
            return s;
        }

        /// <summary>
        /// A disc from a scale triple and a rotation, the way the browser's scene kit writes one:
        /// local x and y are the wide axes, local z is the thin one.
        /// </summary>
        public static Surfel Scaled(Vector3 pos, Vector3 scale, Quaternion rot, Vector3 color,
            SurfelFlags kind = SurfelFlags.None, float alpha = 1f)
        {
            return FromAxes(pos, rot * new Vector3(scale.x, 0f, 0f), rot * new Vector3(0f, scale.y, 0f),
                rot * Vector3.forward, color, kind, alpha);
        }

        /// <summary>Shortest-arc rotation taking <paramref name="from"/> onto <paramref name="to"/>.</summary>
        public static Quaternion FromTo(Vector3 from, Vector3 to)
        {
            from = from.normalized;
            to = to.normalized;
            float d = Vector3.Dot(from, to);
            if (d < -0.9999f) return Quaternion.identity;
            Vector3 c = Vector3.Cross(from, to);
            return new Quaternion(c.x, c.y, c.z, 1f + d).normalized;
        }

        public static readonly Quaternion Flat = FromTo(Vector3.forward, Vector3.up);
    }
}
