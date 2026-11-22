using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace PixelArtSplats.EditorTools
{
    /// <summary>
    /// The same panel the renderer feature holds, in a dock. IMGUI on a component only redraws in edit
    /// mode when the game view receives an event (Unity's rule for ExecuteInEditMode), so the overlay
    /// can look absent while authoring; this window repaints itself and the views on every change.
    /// </summary>
    public class PixelArtHudWindow : EditorWindow
    {
        Vector2 _scroll;

        [MenuItem("Pixel Art Splats/Hud", priority = 2)]
        public static void Open()
        {
            var window = GetWindow<PixelArtHudWindow>();
            window.titleContent = new GUIContent("Pixel Art");
            window.minSize = new Vector2(280f, 340f);
            window.Show();
        }

        void OnInspectorUpdate()
        {
            Repaint(); // which feature is rendering can change with the camera
        }

        void OnGUI()
        {
            PixelArtSplatFeature feature = PixelArtSplatFeature.Current;
            if (feature == null)
            {
                EditorGUILayout.HelpBox(
                    "No Pixel Art Splats feature is loaded.\n\n" +
                    "Add it under Renderer Features: Pixel Art Splats → Setup → Install Renderer Feature.",
                    MessageType.Info);
                if (GUILayout.Button("Install it in this project's URP renderers"))
                    PixelArtSetup.InstallFeature();
                return;
            }

            string path = AssetDatabase.GetAssetPath(feature);
            EditorGUILayout.LabelField("Renderer feature", string.IsNullOrEmpty(path) ? feature.name : path,
                EditorStyles.miniLabel);
            EditorGUILayout.Space(2f);

            OrbitCamera orbit = FindOrbit();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // catch the state before a drag starts, so the change can be undone
            if (Event.current.type == EventType.MouseDown) Undo.RecordObject(feature, "Pixel art look");
            bool changed = PixelArtPanel.Draw(feature.Settings, orbit, Mathf.Max(240f, position.width - 24f));

            EditorGUILayout.EndScrollView();
            if (!changed) return;

            EditorUtility.SetDirty(feature);
            InternalEditorUtility.RepaintAllViews();
        }

        static OrbitCamera FindOrbit()
        {
            if (Selection.activeGameObject != null)
            {
                var selected = Selection.activeGameObject.GetComponent<OrbitCamera>();
                if (selected != null) return selected;
            }
            foreach (OrbitCamera orbit in Resources.FindObjectsOfTypeAll<OrbitCamera>())
                if (orbit.hideFlags == HideFlags.None) return orbit;
            return null;
        }
    }
}
