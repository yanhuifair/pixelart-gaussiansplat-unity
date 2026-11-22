using UnityEngine;

namespace PixelArtSplats
{
    /// <summary>
    /// The look, per camera. Put this on the camera that should draw splats as pixel art and the
    /// renderer feature picks it up.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public class PixelArtSettings : MonoBehaviour
    {
        public PixelArtPalette palette;

        [Header("Pixel grid")]
        [Tooltip("Screen pixels per art pixel. Fixed to the screen, so zooming never changes a pixel's size.")]
        [Range(1, 8)] public int pixelSize = 5;

        [Tooltip("Draw at twice the art resolution: four samples per art pixel, which the pass votes over.")]
        public bool supersample = true;

        [Header("Look")]
        [Tooltip("Hard-edged splats and nearest-sampled palette colours. Off draws soft Gaussians, blended.")]
        public bool crisp = true;

        [Tooltip("Silhouettes take a darker ink of their own colour.")]
        public bool outline = true;

        [Tooltip("Snap every pixel to the palette. Off shows the continuous colours.")]
        public bool quantise = true;

        [Tooltip("Ordered (Bayer) dithering between the two nearest palette colours. 0 = off.")]
        [Range(0f, 1f)] public float dither = 0f;

        [Header("Splats")]
        [Tooltip("Grow every splat to at least this many source texels so far surfaces do not fall apart.")]
        [Range(0f, 3f)] public float minTexels = 0.9f;

        [Tooltip("Show the small splat render on its own, without the pixel pass. For checking.")]
        public bool debugRawSplat = false;

    }
}
