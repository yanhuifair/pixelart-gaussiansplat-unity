using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PixelArtSplats.EditorTools
{
    /// <summary>
    /// Builds the sample scene: the caravan camp from the browser project, imported through
    /// <see cref="DioImport"/> so both packages show the same world.
    /// </summary>
    public static class CaravanDemo
    {
        public const string Folder = SurfelBaker.Root + "/Demo";
        public const string ScenePath = Folder + "/Caravan.unity";

        [MenuItem("Pixel Art Splats/Demo/Build Caravan Camp…", priority = 22)]
        public static void BuildFromDialog()
        {
            string path = EditorUtility.OpenFilePanel("caravan.dio from the browser project", "", "dio");
            if (!string.IsNullOrEmpty(path)) Build(path);
        }

        /// <summary>Batch entry: build from any <c>caravan.dio</c> under <c>Assets/</c>.</summary>
        public static void BuildSample()
        {
            string[] found = Directory.GetFiles("Assets", "caravan.dio", SearchOption.AllDirectories);
            if (found.Length == 0)
            {
                Debug.LogError("Pixel art splats: no caravan.dio under Assets/. Copy one in first.");
                return;
            }
            Build(found[0].Replace('\\', '/'));
        }

        public static void Build(string dioPath)
        {
            PixelArtSetup.InstallFeature();

            byte[] blob = File.ReadAllBytes(dioPath);
            Surfel[] surfels = DioImport.ParseDio(blob, out DioImport.Meta meta);
            if (surfels.Length == 0) return;

            Directory.CreateDirectory(Folder);
            string note = $"caravan camp, imported from {Path.GetFileName(dioPath)}: " +
                          $"{meta.count} surfels and {meta.free} free splats, baked";
            SplatCloudAsset cloud = SurfelBaker.CreateCloud("Caravan", surfels, meta.h,
                DioImport.BoundsOf(surfels), note, Folder);
            PixelArtPalette palette = SurfelBaker.CreatePalette("CaravanPalette",
                meta.palette, new string[0], meta.background, Folder);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // the splats: baked, so no live lighting and no cel stepping; the browser never culls a
            // world's splats, and a baked silhouette has surfaces facing every way
            var root = new GameObject("Caravan Camp");
            var splats = root.AddComponent<SplatRenderer>();
            splats.cloud = cloud;
            splats.bands = 0;
            splats.cullBackfaces = false;
            splats.fog = new Vector2(9.2f, 13.0f);

            // the camera: the browser's 30 degree lens, zoomed out until a tall island fits
            var camGo = new GameObject("Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = PixelArtPalette.TryParse(meta.background, out Color bg) ? bg : Color.black;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
            urp.antialiasing = AntialiasingMode.None;
            urp.renderPostProcessing = false;
            urp.renderShadows = false;

            var settings = camGo.AddComponent<PixelArtSettings>();
            settings.palette = palette;
            settings.pixelSize = 7;      // 1600 / 7 is about the 230 art pixels the browser used
            settings.supersample = true;
            settings.crisp = true;
            settings.outline = true;
            settings.quantise = true;
            settings.dither = 0f;

            var orbit = camGo.AddComponent<OrbitCamera>();
            orbit.target = new Vector3(0f, -0.02f, 0f);
            orbit.distance = 9f;
            orbit.pitch = 16.7f;
            orbit.yaw = 130f;            // 180 - the Godot scene's yaw: the import mirrors x, this is what that leaves
            orbit.autoOrbit = true;
            orbit.Snap();

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

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Pixel art splats: wrote {ScenePath} — {surfels.Length} surfels, " +
                      $"{meta.palette?.Length ?? 0} palette colours");
        }
    }
}
