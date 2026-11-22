using UnityEngine;

namespace PixelArtSplats
{
    /// <summary>
    /// The panel the browser viewer puts over the picture, drawn over the game view while you play.
    /// It edits the look on the renderer feature.
    /// <para>H hides it. Q / X / S / E / O flip the switches, the same keys the site uses.</para>
    /// <para>
    /// In edit mode IMGUI only redraws when the game view receives an event — that is Unity's rule for
    /// <c>ExecuteInEditMode</c>, not a setting of ours. For authoring use
    /// <c>Pixel Art Splats → Hud</c>, which shows the same panel in a dock and repaints the views.
    /// </para>
    /// </summary>
    [AddComponentMenu("Pixel Art Splats/Hud")]
    [ExecuteAlways]
    public class PixelArtHud : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.H;
        public bool visible = true;
        public float width = 292f;

        OrbitCamera _orbit;

        void OnEnable()
        {
            _orbit = GetComponent<OrbitCamera>();
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
            PixelArtSettings settings = PixelArtSplatFeature.CurrentSettings;
            if (!visible || settings == null) return;

            if (Input.GetKeyDown(KeyCode.Q)) settings.quantise = !settings.quantise;
            if (Input.GetKeyDown(KeyCode.X)) settings.crisp = !settings.crisp;
            if (Input.GetKeyDown(KeyCode.S)) settings.supersample = !settings.supersample;
            if (Input.GetKeyDown(KeyCode.E)) settings.outline = !settings.outline;
            if (Input.GetKeyDown(KeyCode.O) && _orbit != null) _orbit.autoOrbit = !_orbit.autoOrbit;
        }

        void OnGUI()
        {
            PixelArtSettings settings = PixelArtSplatFeature.CurrentSettings;
            if (!visible || settings == null) return;
            GUILayout.BeginArea(new Rect(16f, 16f, width, Mathf.Max(120f, Screen.height - 32f)));
            PixelArtPanel.Draw(settings, _orbit, width);
            GUILayout.EndArea();
        }
    }
}
