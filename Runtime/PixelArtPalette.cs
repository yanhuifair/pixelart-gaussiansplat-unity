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

        public static bool TryParse(string hex, out Color c)
        {
            c = Color.black;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out c);
        }

    }
}
