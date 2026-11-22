using System.Collections.Generic;
using UnityEngine;

namespace PixelArtSplats
{
    /// <summary>
    /// The look/view panel, drawn with IMGUI so the runtime hud and the editor window share it.
    /// Call it from inside an OnGUI; it writes straight into the settings, the clouds and the camera.
    /// </summary>
    public static class PixelArtPanel
    {
        static readonly Color Background = new Color(0.07f, 0.07f, 0.09f, 0.86f);
        static readonly Color Ink = new Color(0.90f, 0.90f, 0.94f);
        static readonly Color Dim = new Color(0.60f, 0.60f, 0.66f);
        static readonly Color On = new Color(0.85f, 0.62f, 0.16f);
        static readonly Color Off = new Color(1f, 1f, 1f, 0.12f);

        static GUIStyle _label, _value, _section, _panel;
        static Texture2D _fill, _panelTex;

        /// <summary>Draw the panel. Returns true when something was touched.</summary>
        public static bool Draw(PixelArtSettings settings, OrbitCamera orbit, float width = 292f)
        {
            if (settings == null) return false;
            EnsureStyles();

            GUILayout.BeginVertical(_panel, GUILayout.Width(width));

            Section("look");
            int pixel = Mathf.RoundToInt(Range("pixel size", settings.pixelSize, 1f, 8f, 1f,
                v => $"{Mathf.RoundToInt(v)}px"));
            if (pixel != settings.pixelSize) settings.pixelSize = pixel;

            settings.dither = Range("dither", settings.dither, 0f, 1f, 0.05f, v => v.ToString("0.00"));

            float exposure = Splats.Count > 0 ? Splats[0].exposure : 1f;
            float lit = Range("light", exposure, 0.5f, 2.5f, 0.05f, v => v.ToString("0.00"));
            if (!Mathf.Approximately(lit, exposure))
                foreach (SplatRenderer r in Splats) r.exposure = lit;

            float bands = Splats.Count > 0 ? Splats[0].bands : 5f;
            float steps = Range("light bands", bands, 0f, 8f, 1f,
                v => v < 0.5f ? "smooth" : Mathf.RoundToInt(v).ToString());
            if (!Mathf.Approximately(steps, bands))
                foreach (SplatRenderer r in Splats) r.bands = Mathf.RoundToInt(steps);

            Section("view");
            Toggle("palette", "Q", ref settings.quantise);
            Toggle("hard pixels", "X", ref settings.crisp);
            Toggle("2x2 supersample", "S", ref settings.supersample);
            Toggle("outline", "E", ref settings.outline);
            if (orbit != null) Toggle("auto orbit", "O", ref orbit.autoOrbit);

            GUILayout.Space(4f);
            GUILayout.Label("H  hide", _section);
            GUILayout.EndVertical();
            return GUI.changed;
        }

        static List<SplatRenderer> Splats => SplatRenderer.Active;

        // ---- widgets ---------------------------------------------------------

        static float Range(string label, float value, float min, float max, float step,
            System.Func<float, string> format)
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

        static void Toggle(string label, string key, ref bool value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _label);
            GUILayout.Label(key, _section);
            GUILayout.FlexibleSpace();
            Rect swatch = GUILayoutUtility.GetRect(13f, 13f, GUILayout.Width(13f), GUILayout.Height(13f),
                GUILayout.ExpandWidth(false));
            swatch.y += 2f;
            Color was = GUI.color;
            GUI.color = value ? On : Off;
            GUI.DrawTexture(swatch, _fill);
            GUI.color = was;
            if (Event.current.type == EventType.MouseDown && swatch.Contains(Event.current.mousePosition))
            {
                value = !value;
                GUI.changed = true;
                Event.current.Use();
            }
            GUILayout.EndHorizontal();
        }

        static void Section(string title)
        {
            GUILayout.Space(6f);
            GUILayout.Label(title, _section);
        }

        static void EnsureStyles()
        {
            if (_panel != null) return;
            _fill = Tex(Color.white);
            _panelTex = Tex(Background);

            _panel = new GUIStyle { padding = new RectOffset(14, 14, 10, 12) };
            _panel.normal.background = _panelTex;
            _label = new GUIStyle { fontSize = 13 };
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
