using UnityEngine;

namespace PixelArtSplats.EditorTools
{
    /// <summary>
    /// Baked lighting for scenes. Port of src/authoring/light.js.
    /// <para>A scene does not move, so its light is worked out once per surfel at build time: soft sun
    /// shadows marched through the distance field, lamps that only light what they can see, foliage
    /// lit through, and the lit colour matched to the palette in OKLab with a penalty for leaving the
    /// surface's own hue.</para>
    /// </summary>
    public class LightBaker
    {
        public struct Lamp
        {
            public Vector3 pos;
            public Vector3 color;
            public float power;
        }

        // same direction as the viewer's key light (src/render/shade.js)
        public static readonly Vector3 Sun = new Vector3(0.45f, 0.8f, 0.4f).normalized;

        const float AmbLow = 0.48f, AmbHigh = 0.62f, SunK = 0.7f, LampK = 1f;

        readonly Shape _sh;
        readonly Lamp[] _lamps;
        readonly Vector3[] _ramp;
        readonly Vector3[] _labs;
        readonly Vector2[] _dirs;

        /// <param name="palette">Hex colours; the first entry is the background and is never picked.</param>
        public LightBaker(Shape sh, Lamp[] lamps, string[] palette)
        {
            _sh = sh;
            _lamps = lamps ?? new Lamp[0];
            int n = Mathf.Max(0, palette.Length - 1);
            _ramp = new Vector3[n];
            _labs = new Vector3[n];
            _dirs = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                _ramp[i] = Colours.Hex(palette[i + 1]);
                _labs[i] = Oklab(_ramp[i]);
                _dirs[i] = HueDir(_labs[i]);
            }
        }

        static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);

        public static Vector3 Oklab(Vector3 c)
        {
            float r = ToLinear(Mathf.Max(c.x, 0f)), g = ToLinear(Mathf.Max(c.y, 0f)), b = ToLinear(Mathf.Max(c.z, 0f));
            float l = Mathf.Pow(0.4122214708f * r + 0.5363325363f * g + 0.0514459929f * b, 1f / 3f);
            float m = Mathf.Pow(0.2119034982f * r + 0.6806995451f * g + 0.1073969566f * b, 1f / 3f);
            float s = Mathf.Pow(0.0883024619f * r + 0.2817188376f * g + 0.6299787005f * b, 1f / 3f);
            return new Vector3(
                0.2104542553f * l + 0.793617785f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.428592205f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.808675766f * s);
        }

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // hue as a direction in the a/b plane, shrunk towards 0 for greys so that greys and
        // slightly tinted darks count as the same family
        static Vector2 HueDir(Vector3 lab)
        {
            float c = Mathf.Sqrt(lab.y * lab.y + lab.z * lab.z);
            float k = 1f / (c + 0.035f);
            return new Vector2(lab.y * k, lab.z * k);
        }

        /// <summary>Nearest palette colour to the lit colour, staying near the surface's own hue.</summary>
        public Vector3 Pick(Vector3 target, Vector3 baseCol, float pull = 0f)
        {
            Vector3 t = Oklab(target), bl = Oklab(baseCol);
            Vector2 hb = HueDir(bl);
            float sat = Smooth(0.025f, 0.07f, Mathf.Sqrt(bl.y * bl.y + bl.z * bl.z));
            float lit = Smooth(0.06f, 0.3f, t.x);
            float pen = 0.12f * (1f - pull) * sat * lit;

            int best = 0;
            float bd = float.PositiveInfinity;
            for (int i = 0; i < _labs.Length; i++)
            {
                Vector3 L = _labs[i];
                float dl = t.x - L.x, da = t.y - L.y, db = t.z - L.z;
                float hx = _dirs[i].x - hb.x, hy = _dirs[i].y - hb.y;
                float c2 = L.y * L.y + L.z * L.z; // a grey surface stays out of strong colours
                float d = 1.4f * dl * dl + da * da + db * db + pen * (hx * hx + hy * hy) + 8f * (1f - sat) * c2;
                if (d < bd) { bd = d; best = i; }
            }
            return _ramp.Length > 0 ? _ramp[best] : baseCol;
        }

        // soft shadow: 1 = in full sun, 0 = fully shadowed
        float SunVis(Vector3 p, Vector3 n, float maxT = 4f)
        {
            float t = 0.03f, vis = 1f;
            float ox = p.x + n.x * 0.025f, oy = p.y + n.y * 0.025f, oz = p.z + n.z * 0.025f;
            for (int i = 0; i < 48 && t < maxT; i++)
            {
                float h = _sh.Dist(ox + Sun.x * t, oy + Sun.y * t, oz + Sun.z * t);
                if (h < 0.002f) return 0f;
                vis = Mathf.Min(vis, 10f * h / t);
                t += Mathf.Min(Mathf.Max(h, 0.012f), 0.35f);
            }
            return Mathf.Clamp01(vis);
        }

        // can a lamp at l see this point? (hard-ish, with a little softness)
        float LampVis(Vector3 p, Vector3 n, Vector3 l, float dist)
        {
            float dx = (l.x - p.x) / dist, dy = (l.y - p.y) / dist, dz = (l.z - p.z) / dist;
            float t = 0.03f, vis = 1f;
            float ox = p.x + n.x * 0.025f, oy = p.y + n.y * 0.025f, oz = p.z + n.z * 0.025f;
            for (int i = 0; i < 32 && t < dist - 0.12f; i++)
            {
                float h = _sh.Dist(ox + dx * t, oy + dy * t, oz + dz * t);
                if (h < 0.002f) return 0.1f;
                vis = Mathf.Min(vis, 6f * h / t);
                t += Mathf.Max(h, 0.012f);
            }
            return 0.1f + 0.9f * Mathf.Clamp01(vis);
        }

        /// <summary>The lit colour of a surface point, snapped to the palette.</summary>
        public Vector3 Shade(Vector3 baseCol, Vector3 p, Vector3 n, float ao, bool leaf)
        {
            float ndl = Vector3.Dot(n, Sun);
            if (leaf) ndl = 0.45f + 0.55f * Mathf.Abs(ndl); // light comes through
            float sun = Mathf.Max(0f, ndl);
            if (sun > 0f)
            {
                Vector3 towards = leaf ? Sun : n;
                float v = SunVis(p, towards);
                sun *= leaf ? 0.35f + 0.65f * v : v;
            }

            float sky = AmbLow + (AmbHigh - AmbLow) * (n.y * 0.5f + 0.5f);
            float k = sky * (0.55f + 0.45f * ao) + SunK * sun;

            // lamps: warm light that can recolour what is right next to them
            float wr = 0f, wg = 0f, wb = 0f;
            for (int i = 0; i < _lamps.Length; i++)
            {
                Lamp l = _lamps[i];
                Vector3 d3 = l.pos - p;
                float d = d3.magnitude;
                if (d > 1.6f || d < 1e-4f) continue;
                float facing = Vector3.Dot(n, d3) / d;
                float w = (Mathf.Max(facing, 0f) * 0.85f + 0.15f) / (1f + d * d * 9f) * l.power * LampK;
                if (w < 0.01f) continue;
                if (facing > -0.1f) w *= LampVis(p, n, l.pos, d);
                else w *= 0.3f;
                wr += l.color.x * w;
                wg += l.color.y * w;
                wb += l.color.z * w;
            }

            // cool shadows, warm light: shift the hue with how lit the surface is
            float lit = Mathf.Min(1f, sun / 0.5f);
            Vector3 tint = new Vector3(0.86f + 0.18f * lit, 0.92f + 0.08f * lit, 1.1f - 0.17f * lit);
            Vector3 target = new Vector3(
                baseCol.x * k * tint.x + wr * (baseCol.x * 0.85f + 0.1f),
                baseCol.y * k * tint.y + wg * (baseCol.y * 0.85f + 0.1f),
                baseCol.z * k * tint.z + wb * (baseCol.z * 0.85f + 0.1f));
            float lampLum = 0.3f * wr + 0.59f * wg + 0.11f * wb;
            float pull = Mathf.Clamp01((lampLum - 0.12f) / 0.5f);
            return Pick(target, baseCol, pull);
        }
    }

    /// <summary>Colour helpers, the same ones the scene files use.</summary>
    public static class Colours
    {
        public static Vector3 Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Vector3.one;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? new Vector3(c.r, c.g, c.b) : Vector3.one;
        }

        public static Vector3 Mul(Vector3 c, float k) => c * k;

        public static Vector3 Mix(Vector3 a, Vector3 b, float t) => Vector3.LerpUnclamped(a, b, t);

        public static float Step(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        public static Color ToColor(Vector3 c) => new Color(c.x, c.y, c.z, 1f);
    }
}
