using System;
using System.Collections.Generic;
using System.Globalization;
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

        /// <summary>A lamp a scene declares: where it is, what colour, how strong.</summary>
        [Serializable]
        public class Lamp
        {
            public float[] pos = new float[3];
            public float[] color = { 1f, 1f, 1f };
            public float power = 1f;
        }

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
            public Lamp[] lights;
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
            meta.lights = ParseLamps(json);
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

            var list = new List<Surfel>(n + nFree + 256);
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

                list.Add(SurfelFactory.Disc(pos, normal, radius, colour, baked));
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

                list.Add(SurfelFactory.Scaled(pos, scale, rot, colour, flags, alpha));
            }

            AddParticles(list, json, meta);

            // the browser and Godot are right handed, Unity is left handed: mirror x so a scene comes
            // out the same way round here as it does there
            Surfel[] surfels = list.ToArray();
            for (int i = 0; i < surfels.Length; i++) surfels[i] = Mirrored(surfels[i]);
            return surfels;
        }


        // ---- particles ------------------------------------------------------------
        // A .dio declares its moving things under "particles": embers, smoke, fireflies, drifting sand.
        // They are generated the way the browser generates them — the same lcg, called in the same
        // order — and the renderer animates each one from its index, so nothing moves on the CPU.

        enum Motion
        {
            None = 0, Rise = 1, Float = 2, Blink = 3, Twinkle = 4, Drift = 5, Leaf = 6,
            Smoke = 7, Fog = 8, Shaft = 9, Ray = 10, Pulse = 11, Beam = 12,
        }

        static int MotionId(string mode) => mode switch
        {
            "rise" => (int)Motion.Rise,
            "float" => (int)Motion.Float,
            "blink" => (int)Motion.Blink,
            "twinkle" => (int)Motion.Twinkle,
            "drift" => (int)Motion.Drift,
            "leaf" => (int)Motion.Leaf,
            "smoke" => (int)Motion.Smoke,
            "fog" => (int)Motion.Fog,
            "shaft" => (int)Motion.Shaft,
            "ray" => (int)Motion.Ray,
            "pulse" => (int)Motion.Pulse,
            "beam" => (int)Motion.Beam,
            _ => 0,
        };

        /// <summary>The browser's lcg: same seed, same sequence, same specks.</summary>
        sealed class Rng
        {
            uint _state;
            public Rng(uint seed) { _state = seed; }
            public float Next()
            {
                _state = unchecked(_state * 1664525u + 1013904223u);
                return (float)(_state / 4294967296.0);
            }
        }

        static uint ParamByte(float value) =>
            (uint)Mathf.Clamp(Mathf.RoundToInt(value / 4f * 255f), 0, 255);

        static void AddParticles(List<Surfel> into, string json, Meta meta)
        {
            int at = json.IndexOf("\"particles\"", StringComparison.Ordinal);
            if (at < 0) return;
            int bracket = json.IndexOf('[', at);
            if (bracket < 0) return;
            if (!(MiniJson.Value(json.Substring(bracket)) is List<object> groups)) return;

            var rng = new Rng(99);
            foreach (object entry in groups)
            {
                if (!(entry is Dictionary<string, object> ps)) continue;

                string mode = Flag(ps, "still") ? "twinkle" : Text(ps, "mode");
                int motion = MotionId(mode);
                if (motion == 0) continue;

                int count = Mathf.Max(1, Mathf.RoundToInt(Number(ps, "n", 0f)));
                float[] centre = Vector(ps, "center") ?? new float[3];
                float[] spread = Vector(ps, "spread");
                List<object> origins = List(ps, "origins");
                List<object> points = List(ps, "points");
                string[] colours = Texts(ps, "colors");
                if (colours.Length == 0) continue;

                float alpha = Number(ps, "alpha", 0.95f);
                bool halo = Flag(ps, "halo");
                float height = spread != null ? spread[1] : Number(ps, "height", 0.6f);
                float param = motion == (int)Motion.Drift ? meta.R : height;

                // leaf falls through a column above its centre, beam sweeps around it: those two
                // carry the centre in the surfel's normal slot. Both are translucent, and a
                // translucent splat's normal is never read, so the 64-byte record stays as it is.
                Vector3? pivot = motion == (int)Motion.Leaf || motion == (int)Motion.Beam
                    ? new Vector3(centre[0], centre[1], centre[2])
                    : (Vector3?)null;

                // explicit shapes, relative to the centre (the lighthouse beam): 8 numbers, the last
                // four being a quaternion
                List<object> shapes = List(ps, "splats");
                if (shapes != null)
                {
                    foreach (object item in shapes)
                    {
                        if (!(item is List<object> b) || b.Count < 7) continue;
                        float At(int i) => (float)(double)b[i];
                        Quaternion rot = b.Count > 10
                            ? new Quaternion(At(7), At(8), At(9), At(10))
                            : Quaternion.identity;
                        Put(into, new[] { centre[0] + At(0), centre[1] + At(1), centre[2] + At(2) },
                            At(3), At(4), At(5), At(6), motion, param, colours, rng, pivot, rot);
                    }
                    continue;
                }

                var spots = new List<float[]>();
                if (points != null)
                    foreach (object pt in points) spots.Add(ToVector3(pt));
                else if (origins != null)
                    foreach (object origin in origins)
                        for (int i = 0; i < count; i++) spots.Add(ToVector3(origin));
                else
                    for (int i = 0; i < count; i++)
                    {
                        if (spread != null)
                            spots.Add(new[]
                            {
                                centre[0] + (rng.Next() - 0.5f) * 2f * spread[0],
                                centre[1] + rng.Next() * spread[1],
                                centre[2] + (rng.Next() - 0.5f) * 2f * spread[2],
                            });
                        else
                        {
                            float angle = rng.Next() * 6.28f;
                            float radius = Mathf.Sqrt(rng.Next()) * meta.R * (mode == "shaft" ? 0.7f : 1f);
                            float y = mode == "fog" ? 0.05f + rng.Next() * 0.22f
                                : mode == "shaft" ? meta.top * 0.55f
                                : 0.06f + rng.Next() * (meta.top + 0.3f);
                            spots.Add(new[] { Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius });
                        }
                    }

                foreach (float[] spot in spots)
                {
                    if (mode == "fog")
                    {
                        Put(into, spot, 0.3f + rng.Next() * 0.25f, 0.035f, 0.3f + rng.Next() * 0.25f,
                            alpha, motion, param, colours, rng, pivot);
                    }
                    else if (mode == "shaft")
                    {
                        Put(into, spot, 0.07f + rng.Next() * 0.06f, meta.top * 0.75f,
                            0.07f + rng.Next() * 0.06f, alpha, motion, param, colours, rng, pivot);
                    }
                    else
                    {
                        float k = meta.h * (mode == "smoke" ? 0.9f : 0.28f + rng.Next() * 0.14f);
                        // a leaf is a flat flake: half as tall as it is wide
                        Put(into, spot, k, mode == "leaf" ? k * 0.6f : k, k, alpha, motion, param,
                            colours, rng, pivot);
                        if (halo)
                            Put(into, spot, k * 5.5f, k * 5.5f, k * 5.5f, 0.18f, motion, param,
                                colours, rng, pivot);
                    }
                }
            }
        }

        static void Put(List<Surfel> into, float[] spot, float sx, float sy, float sz, float alpha,
            int motion, float param, string[] colours, Rng rng, Vector3? pivot, Quaternion? rot = null)
        {
            string hex = colours[Mathf.Min(colours.Length - 1, (int)(rng.Next() * colours.Length))];
            var colour = new Vector3(
                Convert.ToInt32(hex.Substring(1, 2), 16) / 255f,
                Convert.ToInt32(hex.Substring(3, 2), 16) / 255f,
                Convert.ToInt32(hex.Substring(5, 2), 16) / 255f);

            // translucent and unshaded, like the glow: the pass blends them over the picture
            Surfel surfel = SurfelFactory.Scaled(new Vector3(spot[0], spot[1], spot[2]),
                new Vector3(sx, sy, sz), rot ?? Quaternion.identity, colour, SurfelFlags.Glow, alpha);
            // the motion id and its parameter live above the kind byte, and SurfelFactory's Kind
            // setter keeps only the low eight bits, so they go in here
            surfel.flags |= ((uint)motion << 16) | (ParamByte(param) << 24);
            if (pivot.HasValue) surfel.normal = pivot.Value;   // leaf and beam turn around this
            into.Add(surfel);
        }

        static Lamp[] ParseLamps(string json)
        {
            int at = json.IndexOf("\"lights\"", StringComparison.Ordinal);
            if (at < 0) return new Lamp[0];
            int bracket = json.IndexOf('[', at);
            if (bracket < 0) return new Lamp[0];
            if (!(MiniJson.Value(json.Substring(bracket)) is List<object> list)) return new Lamp[0];

            var lamps = new List<Lamp>(list.Count);
            foreach (object entry in list)
            {
                if (!(entry is Dictionary<string, object> map)) continue;
                var lamp = new Lamp
                {
                    pos = Vector(map, "pos") ?? new[] { 0f, 0f, 0f },
                    color = Vector(map, "color") ?? new[] { 1f, 1f, 1f },
                    power = Number(map, "power", 1f),
                };
                lamps.Add(lamp);
            }
            return lamps.ToArray();
        }

        // ---- a way into the json, for the nested arrays JsonUtility cannot read ------------

        static class MiniJson
        {
            public static object Value(string text)
            {
                int i = 0;
                return Read(text, ref i);
            }

            static void Space(string s, ref int i)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            static object Read(string s, ref int i)
            {
                Space(s, ref i);
                if (i >= s.Length) return null;
                char c = s[i];
                if (c == '{')
                {
                    var map = new Dictionary<string, object>();
                    i++;
                    Space(s, ref i);
                    if (i < s.Length && s[i] == '}') { i++; return map; }
                    while (i < s.Length)
                    {
                        Space(s, ref i);
                        string key = Str(s, ref i);
                        Space(s, ref i);
                        if (i < s.Length && s[i] == ':') i++;
                        map[key] = Read(s, ref i);
                        Space(s, ref i);
                        if (i < s.Length && s[i] == ',') { i++; continue; }
                        if (i < s.Length && s[i] == '}') i++;
                        return map;
                    }
                    return map;
                }
                if (c == '[')
                {
                    var list = new List<object>();
                    i++;
                    Space(s, ref i);
                    if (i < s.Length && s[i] == ']') { i++; return list; }
                    while (i < s.Length)
                    {
                        list.Add(Read(s, ref i));
                        Space(s, ref i);
                        if (i < s.Length && s[i] == ',') { i++; continue; }
                        if (i < s.Length && s[i] == ']') i++;
                        return list;
                    }
                    return list;
                }
                if (c == '"') return Str(s, ref i);
                if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
                if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
                if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }

                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' ||
                                        s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
                if (i == start) { i++; return 0.0; }
                return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
            }

            static string Str(string s, ref int i)
            {
                if (i < s.Length && s[i] == '"') i++;
                int start = i;
                while (i < s.Length && s[i] != '"') i++;
                string text = s.Substring(start, Mathf.Max(0, i - start));
                if (i < s.Length) i++;
                return text;
            }
        }

        static float Number(Dictionary<string, object> map, string key, float fallback) =>
            map.TryGetValue(key, out object v) && v is double d ? (float)d : fallback;

        static string Text(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out object v) && v is string t ? t : "";

        static bool Flag(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out object v) && v is bool b && b;

        static List<object> List(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out object v) ? v as List<object> : null;

        static float[] Vector(Dictionary<string, object> map, string key)
        {
            List<object> list = List(map, key);
            if (list == null || list.Count < 2) return null;
            var out3 = new float[3];
            for (int i = 0; i < 3 && i < list.Count; i++) out3[i] = (float)(double)list[i];
            return out3;
        }

        static float[] ToVector3(object value)
        {
            if (value is List<object> list && list.Count >= 3)
                return new[] { (float)(double)list[0], (float)(double)list[1], (float)(double)list[2] };
            return new float[3];
        }

        static string[] Texts(Dictionary<string, object> map, string key)
        {
            List<object> list = List(map, key);
            if (list == null) return Array.Empty<string>();
            var outList = new List<string>(list.Count);
            foreach (object item in list)
                if (item is string text) outList.Add(text);
            return outList.ToArray();
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
        public static Surfel Mirrored(Surfel s)
        {
            s.pos.x = -s.pos.x;
            s.axis1.x = -s.axis1.x;
            s.axis2.x = -s.axis2.x;
            s.normal.x = -s.normal.x;
            return s;
        }

        /// <summary>
        /// Place a scene's own lamps as <see cref="Light"/>s, so live-lit splats get the light the
        /// browser gives them. The positions are mirrored with the cloud.
        /// </summary>
        public static GameObject BuildLamps(Meta meta, Transform parent)
        {
            if (meta?.lights == null || meta.lights.Length == 0) return null;
            var root = new GameObject("Lamps");
            if (parent != null) root.transform.SetParent(parent, false);
            for (int i = 0; i < meta.lights.Length; i++)
            {
                Lamp lamp = meta.lights[i];
                var go = new GameObject($"Lamp {i + 1}");
                go.transform.SetParent(root.transform, false);
                // same mirror the splats got: the browser and Godot are right handed
                go.transform.localPosition = new Vector3(-lamp.pos[0], lamp.pos[1], lamp.pos[2]);

                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.color = lamp.color.Length >= 3
                    ? new Color(lamp.color[0], lamp.color[1], lamp.color[2])
                    : Color.white;

                var wiggle = go.AddComponent<PixelArtLamp>();
                wiggle.power = lamp.power;
                wiggle.index = i;
            }
            return root;
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
