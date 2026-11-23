using UnityEngine;

namespace PixelArtSplats
{
    /// <summary>
    /// A lamp a scene declares. The splat pass adds <c>power * flicker</c> at this position, so this
    /// component keeps a Unity <see cref="Light"/>'s intensity in step with that number — and does the
    /// flicker, the way the browser's <c>setLights</c> does.
    /// <para>Only splats that are not baked are lit by it: a baked scene's colour already holds its
    /// light and the shader skips point lights for it. A model standing on an island therefore gets
    /// the flicker while the island does not.</para>
    /// </summary>
    [AddComponentMenu("Pixel Art Splats/Lamp")]
    [RequireComponent(typeof(Light))]
    [ExecuteAlways]
    public class PixelArtLamp : MonoBehaviour
    {
        [Tooltip("The lamp's strength as the world declares it.")]
        public float power = 1f;

        [Tooltip("Which lamp this is, so a row of them does not flicker in lockstep.")]
        public int index;

        [Tooltip("Off keeps a steady light.")]
        public bool flicker = true;

        /// <summary>The pass multiplies a light's intensity by this; keep in step with the shader.</summary>
        public const float Gain = 0.35f;

        Light _light;

        void OnEnable()
        {
            _light = GetComponent<Light>();
            _light.type = LightType.Point;
            _light.shadows = LightShadows.None;
            Apply(0f);
        }

        void Update()
        {
            Apply(Time.realtimeSinceStartup);
        }

        void Apply(float t)
        {
            if (_light == null) _light = GetComponent<Light>();
            float f = flicker
                ? 0.86f + 0.1f * Mathf.Sin(t * 11f + index * 2f) + 0.06f * Mathf.Sin(t * 23.7f + index)
                : 1f;
            _light.intensity = power * f / Gain;
        }
    }
}
