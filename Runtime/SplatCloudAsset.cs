using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace PixelArtSplats
{
    /// <summary>
    /// A baked cloud of surfels. The surfels live in a binary blob (<c>.bytes</c>) next to the
    /// asset, because a hundred thousand of them do not belong in a text serialised object.
    /// Surfels are stored opaque-first, so the renderer can draw them in two calls: solid, then
    /// translucent light, without sorting anything.
    /// </summary>
    [CreateAssetMenu(menuName = "Pixel Art Splats/Splat Cloud", fileName = "New Splat Cloud")]
    public class SplatCloudAsset : ScriptableObject
    {
        [Tooltip("Binary blob of surfels, 64 bytes each.")]
        public TextAsset data;

        public int count;
        [Tooltip("How many leading surfels are opaque. The rest are translucent light.")]
        public int opaqueCount;

        [Tooltip("Surfel spacing the scene was sampled with (world units).")]
        public float spacing = 0.02f;

        public Bounds bounds;
        public string note;

        [NonSerialized] Surfel[] _surfels;

        public Surfel[] Surfels
        {
            get
            {
                if (_surfels == null) Parse();
                return _surfels;
            }
        }

        public bool HasData => data != null && count > 0;

        void Parse()
        {
            if (data == null)
            {
                _surfels = Array.Empty<Surfel>();
                return;
            }

            ReadOnlySpan<byte> bytes = data.bytes;
            int n = Mathf.Min(count, bytes.Length / Surfel.Stride);
            _surfels = new Surfel[n];
            int stride = Marshal.SizeOf<Surfel>();
            if (stride != Surfel.Stride)
            {
                Debug.LogError($"Surfel stride is {stride}, expected {Surfel.Stride}. The shader struct will not match.", this);
                return;
            }
            MemoryMarshal.Cast<byte, Surfel>(bytes.Slice(0, n * Surfel.Stride)).CopyTo(_surfels);
        }

        /// <summary>Upload to a compute buffer. The caller owns it.</summary>
        public ComputeBuffer CreateBuffer()
        {
            if (!HasData) return null;
            ComputeBuffer buf = new ComputeBuffer(count, Surfel.Stride, ComputeBufferType.Structured);
            buf.SetData(Surfels);
            return buf;
        }

        void OnValidate()
        {
            _surfels = null;
        }

        public static void Write(Surfel[] surfels, ref int opaqueCount)
        {
            // opaque first, translucent light second: two draw calls, no sorting at runtime.
            // A stable partition, not Array.Sort: the particles hash on their index, so shuffling
            // equal elements would give each engine a different phase for the same speck.
            var ordered = new Surfel[surfels.Length];
            int at = 0;
            for (int i = 0; i < surfels.Length; i++)
                if (!surfels[i].Has(SurfelFlags.Glow)) ordered[at++] = surfels[i];
            opaqueCount = at;
            for (int i = 0; i < surfels.Length; i++)
                if (surfels[i].Has(SurfelFlags.Glow)) ordered[at++] = surfels[i];
            Array.Copy(ordered, surfels, surfels.Length);
        }
    }
}
