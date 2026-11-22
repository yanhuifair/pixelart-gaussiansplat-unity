using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace PixelArtSplats.EditorTools
{
    /// <summary>
    /// The same panel, in a dock, for edit mode. IMGUI on a component only redraws in edit mode when
    /// the game view receives an event (Unity's rule for ExecuteInEditMode), so authoring wants a
    /// window: this one repaints itself and the game view whenever a value moves.
    /// </summary>
    public class PixelArtHudWindow : EditorWindow
    {
        Camera _camera;

        [MenuItem("Pixel Art Splats/Hud", priority = 2)]
        public static void Open()
        {
            var window = GetWindow<PixelArtHudWindow>();
            window.titleContent = new GUIContent("Pixel Art");
            window.minSize = new Vector2(260f, 320f);
            window.Show();
        }

        void OnEnable()
        {
            if (_camera == null) _camera = Pick(out PixelArtSettings _);
        }

        void OnSelectionChange()
        {
            if (Selection.activeGameObject != null &&
                Selection.activeGameObject.GetComponent<PixelArtSettings>() != null)
            {
                _camera = Selection.activeGameObject.GetComponent<Camera>();
                Repaint();
            }
        }

        void OnGUI()
        {
            _camera = (Camera)EditorGUILayout.ObjectField("Camera", _camera, typeof(Camera), true);

            PixelArtSettings settings = _camera != null ? _camera.GetComponent<PixelArtSettings>() : null;
            if (settings == null)
            {
                EditorGUILayout.HelpBox(
                    "Pick a camera with a Pixel Art Settings component, or select one in the scene.\n\n" +
                    "Menu: Pixel Art Splats → Setup → Install Renderer Feature, then add Pixel Art Settings " +
                    "to the camera.", MessageType.Info);
                if (GUILayout.Button("Use the camera with splat settings")) _camera = Pick(out _);
                return;
            }

            var orbit = _camera.GetComponent<OrbitCamera>();
            bool changed = PixelArtPanel.Draw(settings, orbit, Mathf.Max(240f, position.width - 24f));

            if (!changed) return;
            // in edit mode nothing re-renders on its own: say the asset changed and force a redraw
            EditorUtility.SetDirty(settings);
            if (orbit != null) EditorUtility.SetDirty(orbit);
            foreach (SplatRenderer renderer in SplatRenderer.Active) EditorUtility.SetDirty(renderer);
            InternalEditorUtility.RepaintAllViews();
        }

        static Camera Pick(out PixelArtSettings settings)
        {
            settings = null;
            foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
            {
                if (camera.hideFlags != HideFlags.None) continue;
                PixelArtSettings found = camera.GetComponent<PixelArtSettings>();
                if (found == null) continue;
                settings = found;
                return camera;
            }
            return Camera.main;
        }
    }
}
