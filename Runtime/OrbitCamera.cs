using UnityEngine;

namespace PixelArtSplats
{
    /// <summary>Mouse orbit, scroll zoom, and the viewer's drift when left alone.</summary>
    [AddComponentMenu("Pixel Art Splats/Orbit Camera")]
    [ExecuteAlways]
    public class OrbitCamera : MonoBehaviour
    {
        /// <summary>How the idle camera moves, as the viewer moves it.</summary>
        public enum DriftMode
        {
            /// <summary>A steady turn at <see cref="autoOrbitSpeed"/>, nothing else.</summary>
            Off,
            /// <summary>The viewer's scene camera: it drifts in for a close look and back out.</summary>
            Scene,
            /// <summary>The viewer's model camera: it breathes by twelve percent.</summary>
            Model,
        }

        public Vector3 target = new Vector3(0f, 0.25f, 0f);
        public float distance = 3.4f;
        [Range(-89f, 89f)] public float pitch = 16f;
        public float yaw = 24f;

        public bool autoOrbit = true;
        public float autoOrbitSpeed = 7f;
        public float idleDelay = 4f;

        [Header("Drift")]
        public DriftMode drift = DriftMode.Scene;

        [Tooltip("How far the camera sits above its target, as a fraction of the distance (the viewer's elev).")]
        public float elev = 0.22f;

        [Tooltip("The slow rise and fall of that height.")]
        public float bob = 0.06f;

        [Tooltip("Pull back on a window narrower than the picture, the way the viewer's portraitFit does.")]
        public bool portraitFit = true;

        public float dragSpeed = 4f;
        public float zoomSpeed = 0.6f;
        public Vector2 distanceRange = new Vector2(1.2f, 9f);

        float _idle;
        float _drift;
        float _lastZoom = 1f;

        void OnEnable() => Snap();

        void OnValidate() => Snap();

        void Update()
        {
            if (!Application.isPlaying)
            {
                Snap();
                return;
            }

            bool touched = false;
            if (Input.GetMouseButton(0))
            {
                yaw += Input.GetAxis("Mouse X") * dragSpeed;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * dragSpeed, -89f, 89f);
                touched = true;
            }
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                distance = Mathf.Clamp(distance * (1f - scroll * zoomSpeed * 0.1f),
                    distanceRange.x, distanceRange.y);
                touched = true;
            }

            _idle = touched ? 0f : _idle + Time.deltaTime;

            bool drifting = autoOrbit && _idle > idleDelay;
            if (drifting && _idle - Time.deltaTime <= idleDelay)
            {
                // just started: take the zoom curve's value as the baseline, so it does not jump
                _drift = 0f;
                _lastZoom = ZoomAt(0f);
            }

            if (drifting)
            {
                _drift += Time.deltaTime;
                yaw += autoOrbitSpeed * Time.deltaTime;
                if (drift != DriftMode.Off)
                {
                    float z = ZoomAt(_drift);
                    distance = Mathf.Clamp(distance * z / _lastZoom, distanceRange.x, distanceRange.y);
                    _lastZoom = z;
                    pitch = Mathf.Atan(elev + bob * Mathf.Sin(_drift * 0.2f)) * Mathf.Rad2Deg;
                }
            }
            Snap();
        }

        float ZoomAt(float t)
        {
            switch (drift)
            {
                case DriftMode.Scene: return 0.7f + 0.3f * (0.5f + 0.5f * Mathf.Cos(t * 0.11f));
                case DriftMode.Model: return 1f - 0.12f * Mathf.Sin(t * 0.15f);
                default: return 1f;
            }
        }

        float Fit()
        {
            if (!portraitFit) return 1f;
            var cam = GetComponent<Camera>();
            float aspect = cam != null && cam.aspect > 0.01f ? cam.aspect : 1.6f;
            return Mathf.Max(1f, 0.9f / aspect);
        }

        /// <summary>Put the camera where the orbit settings say it is. Call after changing them.</summary>
        public void Snap()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = target - rot * Vector3.forward * (distance * Fit());
            transform.rotation = rot;
        }
    }
}
