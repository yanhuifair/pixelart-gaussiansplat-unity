using UnityEngine;

namespace PixelArtSplats
{
    /// <summary>Mouse orbit, scroll zoom, slow drift when left alone. The viewer's feel, roughly.</summary>
    [AddComponentMenu("Pixel Art Splats/Orbit Camera")]
    [ExecuteAlways]
    public class OrbitCamera : MonoBehaviour
    {
        public Vector3 target = new Vector3(0f, 0.25f, 0f);
        public float distance = 3.4f;
        [Range(-89f, 89f)] public float pitch = 16f;
        public float yaw = 24f;

        public bool autoOrbit = true;
        public float autoOrbitSpeed = 7f;
        public float idleDelay = 4f;

        public float dragSpeed = 4f;
        public float zoomSpeed = 0.6f;
        public Vector2 distanceRange = new Vector2(1.2f, 9f);

        float _idle;

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
            if (autoOrbit && _idle > idleDelay) yaw += autoOrbitSpeed * Time.deltaTime;
            Snap();
        }

        /// <summary>Put the camera where the orbit settings say it is. Call after changing them.</summary>
        public void Snap()
        {
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = target - rot * Vector3.forward * distance;
            transform.rotation = rot;
        }
    }
}
