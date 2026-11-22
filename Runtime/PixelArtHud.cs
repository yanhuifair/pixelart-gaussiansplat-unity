using UnityEngine;

namespace PixelArtSplats
{
    /// <summary>
    /// The panel the browser viewer puts over the picture, drawn over the game view while you play.
    /// <para>H hides it. Q / X / S / E / O flip the switches, the same keys the site uses.</para>
    /// <para>
    /// In edit mode IMGUI only redraws when the game view receives an event — that is Unity's rule for
    /// <c>ExecuteInEditMode</c>, not a setting of ours. For authoring use
    /// <c>Pixel Art Splats → Hud</c>, which shows the same panel in a dock and repaints the views.
    /// </para>
    /// </summary>
    [AddComponentMenu("Pixel Art Splats/Hud")]
    [RequireComponent(typeof(PixelArtSettings))]
    [ExecuteAlways]
    public class PixelArtHud : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.H;
        public bool visible = true;
        public float width = 292f;

        PixelArtSettings _settings;
        OrbitCamera _orbit;

        void OnEnable()
        {
            _settings = GetComponent<PixelArtSettings>();
            _orbit = GetComponent<OrbitCamera>();
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
            if (!visible || _settings == null) return;

            if (Input.GetKeyDown(KeyCode.Q)) _settings.quantise = !_settings.quantise;
            if (Input.GetKeyDown(KeyCode.X)) _settings.crisp = !_settings.crisp;
            if (Input.GetKeyDown(KeyCode.S)) _settings.supersample = !_settings.supersample;
            if (Input.GetKeyDown(KeyCode.E)) _settings.outline = !_settings.outline;
            if (Input.GetKeyDown(KeyCode.O) && _orbit != null) _orbit.autoOrbit = !_orbit.autoOrbit;
        }

        void OnGUI()
        {
            if (!visible || _settings == null) return;
            GUILayout.BeginArea(new Rect(16f, 16f, width, Mathf.Max(120f, Screen.height - 32f)));
            PixelArtPanel.Draw(_settings, _orbit, width);
            GUILayout.EndArea();
        }
    }
}
