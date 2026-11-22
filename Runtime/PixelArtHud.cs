using System.Collections.Generic;
using UnityEngine;

namespace PixelArtSplats
{
    /// <summary>
    /// The panel the browser viewer puts over the picture: a few sliders and switches for the look.
    /// Draws over the game view, so nothing else in the scene has to know about it.
    /// <para>H hides it. Q / X / S / E / O flip the switches, the same keys the site uses.</para>
    /// </summary>
    [AddComponentMenu("Pixel Art Splats/Hud")]
    [RequireComponent(typeof(PixelArtSettings))]
    [ExecuteAlways]   // the panel is worth seeing while you author, not only in play mode
    public class PixelArtHud : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.H;
        public bool visible = true;
        public float width = 292f;

        static readonly Color Background = new Color(0.07f, 0.07f, 0.09f, 0.86f);
        static readonly Color Ink = new Color(0.90f, 0.90f, 0.94f);
        static readonly Color Dim = new Color(0.60f, 0.60f, 0.66f);
        static readonly Color On = new Color(0.85f, 0.62f, 0.16f);
        static readonly Color Off = new Color(1f, 1f, 1f, 0.12f);

        PixelArtSettings _settings;
        OrbitCamera _orbit;
        Texture2D _fill;
        GUIStyle _label, _value, _section, _panel;

        void OnEnable()
        {
            _settings = GetComponent<PixelArtSettings>();
            _orbit = GetComponent<OrbitCamera>();
        }

        void OnDisable()
        {
            if (_fill != null) DestroyImmediate(_fill);
            _fill = null;
        }

        void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
            if (!visible) return;

            if (Input.GetKeyDown(KeyCode.Q)) _settings.quantise = !_settings.quantise;
            if (Input.GetKeyDown(KeyCode.X)) _settings.crisp = !_settings.crisp;
            if (Input.GetKeyDown(KeyCode.S)) _settings.supersample = !_settings.supersample;
            if (Input.GetKeyDown(KeyCode.E)) _settings.outline = !_settings.outline;
            if (Input.GetKeyDown(KeyCode.O) && _orbit != null) _orbit.autoOrbit = !_orbit.autoOrbit;
        }

        void OnGUI()
        {
            if (!visible || _settings == null) return;
            EnsureStyles();

            GUILayout.BeginArea(new Rect(16f, 16f, width, Screen.height - 32f));
            GUILayout.BeginVertical(_panel, GUILayout.Width(width));

            Section("look");
            int pixel = Mathf.RoundToInt(Slider("pixel size", _settings.pixelSize, 1f, 8f, 1f, v => $"{Mathf.RoundToInt(v)}px"));
            if (pixel != _settings.pixelSize) _settings.pixelSize = pixel;

            _settings.dither = Slider("dither", _settings.dither, 0f, 1f, 0.05f, v => v.ToString("0.00"));

            float exposure = Splats.Count > 0 ? Splats[0].exposure : 1f;
            float lit = Slider("light", exposure, 0.5f, 2.5f, 0.05f, v => v.ToString("0.00"));
            if (!Mathf.Approximately(lit, exposure)) foreach (SplatRenderer r in Splats) r.exposure = lit;

            float bands = Splats.Count > 0 ? Splats[0].bands : 5f;
            float steps = Slider("light bands", bands, 0f, 8f, 1f, v => v < 0.5f ? "smooth" : Mathf.RoundToInt(v).ToString());
            if (!Mathf.Approximately(steps, bands)) foreach (SplatRenderer r in Splats) r.bands = Mathf.RoundToInt(steps);

            Section("view");
            Toggle("palette", "Q", ref _settings.quantise);
            Toggle("hard pixels", "X", ref _settings.crisp);
            Toggle("2x2 supersample", "S", ref _settings.supersample);
            Toggle("outline", "E", ref _settings.outline);
            if (_orbit != null) Toggle("auto orbit", "O", ref _orbit.autoOrbit);

            GUILayout.Space(4f);
            GUILayout.Label("H  hide", _section);
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        static List<SplatRenderer> Splats => SplatRenderer.Active;

        // ---- widgets ---------------------------------------------------------

        float Slider(string label, float value, float min, float max, float step, System.Func<float, string> format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _label);
            GUILayout.FlexibleSpace();
            GUILayout.Label(format(Mathf.Round(value / step) * step), _value);
            GUILayout.EndHorizontal();

            float next = GUILayout.HorizontalSlider(value, min, max, GUILayout.Height(14f));
            if (Mathf.Abs(next - value) < step * 0.5f) return value;
            return Mathf.Round(next / step) * step;
        }

        void Toggle(string label, string key, ref bool value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _label);
            GUILayout.Label(key, _section);
            GUILayout.FlexibleSpace();
            var swatch = GUILayoutUtility.GetRect(13f, 13f, GUILayout.Width(13f), GUILayout.Height(13f),
                GUILayout.ExpandWidth(false));
            swatch.y += 2f;
            Color was = GUI.color;
            GUI.color = value ? On : Off;
            GUI.DrawTexture(swatch, _fill);
            GUI.color = was;
            if (Event.current.type == EventType.MouseDown && swatch.Contains(Event.current.mousePosition))
            {
                value = !value;
                Event.current.Use();
            }
            GUILayout.EndHorizontal();
        }

        void Section(string title)
        {
            GUILayout.Space(6f);
            GUILayout.Label(title, _section);
        }

        void EnsureStyles()
        {
            if (_fill == null)
            {
                _fill = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _fill.SetPixel(0, 0, Color.white);
                _fill.Apply();
            }
            if (_panel != null) return;

            _panel = new GUIStyle
            {
                padding = new RectOffset(14, 14, 10, 12),
                margin = new RectOffset(0, 0, 0, 0),
            };
            _panel.normal.background = Tex(Background);

            _label = new GUIStyle { fontSize = 13, wordWrap = false };
            _label.normal.textColor = Ink;
            _value = new GUIStyle { fontSize = 12 };
            _value.normal.textColor = Dim;
            _section = new GUIStyle { fontSize = 11 };
            _section.normal.textColor = Dim;
        }

        static Texture2D Tex(Color colour)
        {
            var tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            tex.SetPixel(0, 0, colour);
            tex.Apply();
            return tex;
        }
    }
}
