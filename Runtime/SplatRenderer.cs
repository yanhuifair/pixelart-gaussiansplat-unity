using System.Collections.Generic;
using UnityEngine;

namespace PixelArtSplats
{
    /// <summary>
    /// Draws one baked splat cloud. The pass in <see cref="PixelArtSplatFeature"/> does the drawing,
    /// because the whole cloud has to land in one shared low-resolution buffer.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SplatRenderer : MonoBehaviour
    {
        public static readonly List<SplatRenderer> Active = new List<SplatRenderer>();

        public SplatCloudAsset cloud;

        [Header("Live light (baked clouds ignore this)")]
        [Range(0.5f, 2.5f)] public float exposure = 1f;

        [Tooltip("Flat light steps for cel shading on live-lit splats. 0 = smooth.")]
        [Range(0, 8)] public int bands = 5;

        [Tooltip("Distance over which splats fade into the background.")]
        public Vector2 fog = new Vector2(3f, 6f);

        public bool cullBackfaces = true;

        ComputeBuffer _buffer;

        public ComputeBuffer Buffer => _buffer;
        public int Count { get; private set; }
        public int OpaqueCount { get; private set; }

        void OnEnable()
        {
            Rebuild();
            if (!Active.Contains(this)) Active.Add(this);
        }

        void OnDisable()
        {
            Active.Remove(this);
            _buffer?.Release();
            _buffer = null;
        }

        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            Rebuild();
        }

        void Rebuild()
        {
            _buffer?.Release();
            _buffer = null;
            Count = 0;
            OpaqueCount = 0;
            if (cloud == null || !cloud.HasData) return;
            _buffer = cloud.CreateBuffer();
            Count = cloud.count;
            OpaqueCount = Mathf.Clamp(cloud.opaqueCount, 0, cloud.count);
        }

        void OnDrawGizmosSelected()
        {
            if (cloud == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.4f, 0.9f, 0.7f, 0.5f);
            Gizmos.DrawWireCube(cloud.bounds.center, cloud.bounds.size);
        }
    }
}
