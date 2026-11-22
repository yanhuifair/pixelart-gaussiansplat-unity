using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PixelArtSplats.EditorTools
{
    /// <summary>Menus: install the pass, bake the demo, bake a model, render a still.</summary>
    public static class PixelArtSetup
    {
        public const string DemoScenePath = SurfelBaker.Root + "/Demo/Caravan.unity";

        /// <summary>The demo scene, wherever it is: freshly built, or imported from the package's samples.</summary>
        public static string FindDemoScene()
        {
            if (File.Exists(DemoScenePath)) return DemoScenePath;
            if (!Directory.Exists("Assets/Samples")) return null;
            foreach (string name in new[] { "Caravan.unity", "HelloIsland.unity" })
            {
                string[] found = Directory.GetFiles("Assets/Samples", name, SearchOption.AllDirectories);
                if (found.Length > 0) return found[0].Replace('\\', '/');
            }
            return null;
        }

        // ---- install ---------------------------------------------------------------

        [MenuItem("Pixel Art Splats/Setup/Install Renderer Feature", priority = 1)]
        public static void InstallFeature()
        {
            string[] guids = AssetDatabase.FindAssets("t:UniversalRendererData");
            if (guids.Length == 0)
            {
                Debug.LogError("Pixel art splats: no URP renderer found. Is this project using the URP?");
                return;
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                // only project renderers: a package's own default renderer is not ours to edit
                if (!path.StartsWith("Assets/")) continue;
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) continue;

                bool present = false;
                foreach (ScriptableRendererFeature f in data.rendererFeatures)
                    if (f is PixelArtSplatFeature) present = true;
                if (present) continue;

                var feature = ScriptableObject.CreateInstance<PixelArtSplatFeature>();
                feature.name = "Pixel Art Splats";
                feature.splatShader = Shader.Find("PixelArtSplats/Surfel");
                feature.compositeShader = Shader.Find("PixelArtSplats/Composite");
                feature.passEvent = RenderPassEvent.AfterRenderingPostProcessing;
                AssetDatabase.AddObjectToAsset(feature, data);

                var so = new SerializedObject(data);
                SerializedProperty list = so.FindProperty("m_RendererFeatures");
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
                SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
                if (map != null)
                {
                    map.arraySize++;
                    map.GetArrayElementAtIndex(map.arraySize - 1).longValue = 0;
                }
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Debug.Log($"Pixel art splats: added the pass to {path}");
            }
            AssetDatabase.SaveAssets();
        }

        // ---- demo ------------------------------------------------------------------

        [MenuItem("Pixel Art Splats/Demo/Build Hello Island", priority = 20)]
        public static void BuildDemo()
        {
            InstallFeature();

            HelloScene.Result scene = HelloScene.Build();
            string folder = SurfelBaker.Root + "/Demo";
            Directory.CreateDirectory(folder);
            SplatCloudAsset cloud = SurfelBaker.CreateCloud("HelloIsland", scene.surfels, scene.spacing,
                scene.bounds, "hello scene, ported from src/authoring/scenes/hello.js", folder);
            PixelArtPalette palette = SurfelBaker.CreatePalette("HelloPalette", HelloScene.Palette,
                HelloScene.GlowPalette, HelloScene.Background, folder);

            var unity = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // the splats
            var root = new GameObject("Hello Island");
            var splats = root.AddComponent<SplatRenderer>();
            splats.cloud = cloud;
            splats.fog = new Vector2(6.2f, 9.0f);
            splats.bands = 0;   // scenes are not cel shaded: the bake already did the stepping
            splats.cullBackfaces = true;

            // the camera: same lens as the browser, 30 degrees
            var camGo = new GameObject("Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Colours.ToColor(Colours.Hex(HelloScene.Background));
            cam.allowHDR = false;
            cam.allowMSAA = false;

            var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.antialiasing = AntialiasingMode.None;
            urp.renderPostProcessing = false;
            urp.renderShadows = false;

            var settings = camGo.AddComponent<PixelArtSettings>();
            settings.palette = palette;
            settings.pixelSize = 5;
            settings.supersample = true;
            settings.crisp = true;
            settings.outline = true;
            settings.quantise = true;
            settings.dither = 0f;

            var orbit = camGo.AddComponent<OrbitCamera>();
            orbit.target = new Vector3(0f, -0.02f, 0f);
            orbit.distance = 5.7f;
            orbit.pitch = 10f;
            orbit.yaw = 202f;
            orbit.autoOrbit = true;
            orbit.Snap();

            // the sun the live-lit splats (grass, flowers) are shaded with: the same one the bake used
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.None;
            sunGo.transform.rotation = Quaternion.LookRotation(-LightBaker.Sun);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.1f, 0.1f, 0.14f);

            EditorSceneManager.SaveScene(unity, DemoScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Pixel art splats: wrote {DemoScenePath}");
        }

        // ---- a model ---------------------------------------------------------------

        [MenuItem("Pixel Art Splats/Bake/Bake Selected Meshes To Splat Cloud", priority = 40)]
        public static void BakeSelection()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.LogError("Pixel art splats: select the model to bake first.");
                return;
            }

            Surfel[] surfels = SurfelBaker.FromMeshes(go, 120000, out float spacing, out Bounds bounds);
            if (surfels.Length == 0) return;
            SplatCloudAsset cloud = SurfelBaker.CreateCloud(go.name.Replace(" ", "") + "Splats",
                surfels, spacing, bounds, $"baked from {go.name}");

            var holder = new GameObject(go.name + " Splats");
            var r = holder.AddComponent<SplatRenderer>();
            r.cloud = cloud;
            Undo.RegisterCreatedObjectUndo(holder, "Bake splats");
            Selection.activeGameObject = holder;
            EditorGUIUtility.PingObject(cloud);
        }

        // ---- a still, for checking the look ---------------------------------------

        [MenuItem("Pixel Art Splats/Render Still", priority = 60)]
        public static void RenderStill()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("Pixel art splats: no MainCamera in the open scene.");
                return;
            }
            RenderStill(cam, 1600, 900, Path.Combine(Path.GetTempPath(), "pixelart-still.png"));
        }

        [MenuItem("Pixel Art Splats/Demo/Render Demo Still", priority = 21)]
        public static void RenderDemoStill()
        {
            string scene = FindDemoScene();
            if (scene == null)
            {
                Debug.LogError("Pixel art splats: no demo scene found. Run Demo → Build Caravan Camp…, " +
                               "or import the Caravan Camp sample from the Package Manager.");
                return;
            }
            EditorSceneManager.OpenScene(scene);
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError($"Pixel art splats: {scene} has no MainCamera.");
                return;
            }
            RenderStill(cam, 1600, 900, Path.Combine(Path.GetTempPath(), "pixelart-still.png"));
        }

        public static void RenderStill(Camera cam, int width, int height, string path)
        {
            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 24) { sRGB = true };
            var rt = new RenderTexture(desc);
            RenderTexture previous = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
            Debug.Log($"Pixel art splats: rendered {path}");
        }
    }
}
