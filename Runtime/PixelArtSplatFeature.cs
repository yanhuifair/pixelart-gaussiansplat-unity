using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PixelArtSplats
{
    /// <summary>
    /// Draws every <see cref="SplatRenderer"/> into one low-resolution buffer, then turns that
    /// buffer into pixel art and puts it back on the screen.
    /// <para>Add this to the URP renderer asset (it appears under Renderer Features) and set the look
    /// right here: the palette, the pixel grid and the switches all live on the feature.</para>
    /// </summary>
    public class PixelArtSplatFeature : ScriptableRendererFeature
    {
        public Shader splatShader;
        public Shader compositeShader;

        [Tooltip("Must be after post processing: the pass replaces the whole picture.")]
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingPostProcessing;

        [Header("Look")]
        [Tooltip("The palette, the pixel grid and the switches. The hud and the editor window edit these.")]
        public PixelArtSettings settings = new PixelArtSettings();

        PixelArtPass _pass;

        static readonly List<PixelArtSplatFeature> s_all = new List<PixelArtSplatFeature>();

        /// <summary>Every instance loaded, so the editor can find one to edit.</summary>
        public static IReadOnlyList<PixelArtSplatFeature> All => s_all;

        /// <summary>The instance that last drew a picture: the one the panel edits.</summary>
        public static PixelArtSplatFeature LastRendered { get; private set; }

        /// <summary>The feature that is drawing, or any loaded one: what the panel edits.</summary>
        public static PixelArtSplatFeature Current
        {
            get
            {
                if (LastRendered != null) return LastRendered;
                foreach (PixelArtSplatFeature feature in s_all)
                    if (feature != null) return feature;
                return null;
            }
        }

        /// <summary>The look in force, for the hud and the editor window.</summary>
        public static PixelArtSettings CurrentSettings
        {
            get
            {
                PixelArtSplatFeature feature = Current;
                return feature != null ? feature.Settings : null;
            }
        }

        /// <summary>Never null: an asset made before this field existed gets a fresh one.</summary>
        public PixelArtSettings Settings => settings != null ? settings : settings = new PixelArtSettings();

        static PixelArtPalette s_fallback;
        static bool s_warned;

        /// <summary>
        /// The palette to paint with: the one on the settings, or the built-in default, so a fresh
        /// install draws something instead of nothing.
        /// </summary>
        public PixelArtPalette Palette
        {
            get
            {
                if (Settings.palette != null) return Settings.palette;
                if (!s_warned)
                {
                    s_warned = true;
                    Debug.LogWarning("Pixel art splats: the renderer feature has no palette. " +
                                     "Using the default one — assign yours under Renderer Features.", this);
                }
                return s_fallback != null ? s_fallback : s_fallback = ScriptableObject.CreateInstance<PixelArtPalette>();
            }
        }

        void OnEnable()
        {
            if (!s_all.Contains(this)) s_all.Add(this);
        }

        void OnDisable()
        {
            s_all.Remove(this);
            if (LastRendered == this) LastRendered = null;
        }

        public override void Create()
        {
            _pass ??= new PixelArtPass();
            _pass.renderPassEvent = passEvent;
            _pass.Setup(splatShader != null ? splatShader : Shader.Find("PixelArtSplats/Surfel"),
                compositeShader != null ? compositeShader : Shader.Find("PixelArtSplats/Composite"));
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!_pass.Ready) return;
            CameraType type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView && type != CameraType.Preview) return;

            if (SplatRenderer.Active.Count == 0) return;

            LastRendered = this;
            _pass.settings = Settings;
            _pass.palette = Palette;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
        }

        class PixelArtPass : ScriptableRenderPass
        {
            static readonly int IdSurfels = Shader.PropertyToID("_Surfels");
            static readonly int IdPalette = Shader.PropertyToID("_Palette");
            static readonly int IdPaletteCount = Shader.PropertyToID("_PaletteCount");
            static readonly int IdSrcSize = Shader.PropertyToID("_SrcSize");
            static readonly int IdArtSize = Shader.PropertyToID("_ArtSize");
            static readonly int IdPassFlags = Shader.PropertyToID("_PassFlags");
            static readonly int IdPassFlags2 = Shader.PropertyToID("_PassFlags2");
            static readonly int IdBackground = Shader.PropertyToID("_Background");
            static readonly int IdSplatTex = Shader.PropertyToID("_SplatTex");
            static readonly int IdArtTex = Shader.PropertyToID("_ArtTex");
            static readonly int IdObjToWorld = Shader.PropertyToID("_PS_ObjToWorld");
            static readonly int IdObjToWorldIT = Shader.PropertyToID("_PS_ObjToWorldIT");
            static readonly int IdViewProj = Shader.PropertyToID("_PS_ViewProj");
            static readonly int IdCamPos = Shader.PropertyToID("_PS_CamPos");
            static readonly int IdSunDir = Shader.PropertyToID("_PS_SunDir");
            static readonly int IdFog = Shader.PropertyToID("_PS_Fog");
            static readonly int IdParams = Shader.PropertyToID("_PS_Params");
            static readonly int IdLowRes = Shader.PropertyToID("_PS_LowRes");
            static readonly int IdPointPos = Shader.PropertyToID("_PS_PointPos");
            static readonly int IdPointColor = Shader.PropertyToID("_PS_PointColor");
            static readonly int IdLightCount = Shader.PropertyToID("_PS_LightCount");
            static readonly int IdSplatBase = Shader.PropertyToID("_PS_SplatBase");
            static readonly int IdExposure = Shader.PropertyToID("_PS_Exposure");
            static readonly int IdTime = Shader.PropertyToID("_PS_Time");

            static readonly Vector3 DefaultSun = new Vector3(0.45f, 0.8f, 0.4f).normalized;

            public PixelArtSettings settings;
            public PixelArtPalette palette;

            Material _splat;
            Material _composite;
            RenderTexture _splatRT;
            RenderTexture _artRT;
            int _splatW, _splatH, _artW, _artH;
            ComputeBuffer _palette;
            readonly Vector4[] _paletteData = new Vector4[64];
            readonly List<Color> _colors = new List<Color>(64);
            readonly Vector4[] _pointPos = new Vector4[4];
            readonly Vector4[] _pointColor = new Vector4[4];
            readonly List<SplatRenderer> _draw = new List<SplatRenderer>(8);

            public bool Ready => _splat != null && _composite != null;

            bool _logged;

            public void Setup(Shader splat, Shader composite)
            {
                bool changed = false;
                if (splat != null && (_splat == null || _splat.shader != splat))
                {
                    CoreUtils.Destroy(_splat);
                    _splat = CoreUtils.CreateEngineMaterial(splat);
                    changed = true;
                }
                if (composite != null && (_composite == null || _composite.shader != composite))
                {
                    CoreUtils.Destroy(_composite);
                    _composite = CoreUtils.CreateEngineMaterial(composite);
                    changed = true;
                }
                if (changed && _splat == null)
                    Debug.LogError("PixelArtSplats: splat shader not found (PixelArtSplats/Surfel).");
                if (_palette == null) _palette = new ComputeBuffer(64, 16);
            }

            public void Dispose()
            {
                CoreUtils.Destroy(_splat);
                CoreUtils.Destroy(_composite);
                Release(ref _splatRT);
                Release(ref _artRT);
                _palette?.Release();
                _palette = null;
            }

            static void Release(ref RenderTexture rt)
            {
                if (rt == null) return;
                rt.Release();
                CoreUtils.Destroy(rt);
                rt = null;
            }

            static RenderTexture Ensure(ref RenderTexture rt, in RenderTextureDescriptor desc,
                int width, int height, ref int cacheW, ref int cacheH)
            {
                if (rt != null && cacheW == width && cacheH == height) return rt;
                Release(ref rt);
                rt = new RenderTexture(desc) { name = "PixelArtSplats", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                rt.Create();
                cacheW = width;
                cacheH = height;
                return rt;
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (settings == null || !Ready) return;

                CameraData camData = renderingData.cameraData;
                Camera camera = camData.camera;
                RTHandle screen = camData.renderer.cameraColorTargetHandle;
                if (screen == null) return;

                CollectDrawable(camera.cullingMask);
                if (_draw.Count == 0) return;

                int pixelSize = Mathf.Max(1, settings.pixelSize);
                int screenW = Mathf.Max(1, camData.cameraTargetDescriptor.width);
                int screenH = Mathf.Max(1, camData.cameraTargetDescriptor.height);
                int artW = Mathf.Max(1, (screenW + pixelSize - 1) / pixelSize);
                int artH = Mathf.Max(1, (screenH + pixelSize - 1) / pixelSize);
                int ss = settings.supersample ? 2 : 1;
                int srcW = artW * ss;
                int srcH = artH * ss;

                RenderTextureDescriptor srcDesc = camData.cameraTargetDescriptor;
                srcDesc.width = srcW;
                srcDesc.height = srcH;
                srcDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
                srcDesc.depthStencilFormat = GraphicsFormat.D24_UNorm_S8_UInt;
                srcDesc.depthBufferBits = 24;
                srcDesc.msaaSamples = 1;
                srcDesc.useMipMap = false;
                srcDesc.autoGenerateMips = false;
                srcDesc.bindMS = false;
                _splatRT = Ensure(ref _splatRT, srcDesc, srcW, srcH, ref _splatW, ref _splatH);

                RenderTextureDescriptor artDesc = srcDesc;
                artDesc.width = artW;
                artDesc.height = artH;
                artDesc.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
                artDesc.depthStencilFormat = GraphicsFormat.None;
                artDesc.depthBufferBits = 0;
                _artRT = Ensure(ref _artRT, artDesc, artW, artH, ref _artW, ref _artH);

                CommandBuffer cmd = CommandBufferPool.Get("Pixel Art Splats");
                UpdatePalette(cmd);
                if (!_logged)
                {
                    _logged = true;
                    int total = 0;
                    foreach (SplatRenderer r in _draw) total += r.Count;
                    Debug.Log($"Pixel art splats: screen {screenW}x{screenH}, art {artW}x{artH}, " +
                              $"source {srcW}x{srcH}, {_draw.Count} renderer(s), {total} surfels, " +
                              $"crisp {settings.crisp}, ss {ss}");
                }

                // ---- 1. the splats, small and hard ----------------------------------
                cmd.SetRenderTarget(_splatRT);
                cmd.ClearRenderTarget(true, true, Color.clear);
                cmd.SetViewport(new Rect(0, 0, srcW, srcH));

                Matrix4x4 viewProj = camData.GetGPUProjectionMatrix() * camera.worldToCameraMatrix;
                Vector3 camPos = camera.transform.position;
                // crisp: hard discs, no falloff at all. soft: the Gaussian comes back.
                float falloff = settings.crisp ? 0f : 1f;

                cmd.SetGlobalMatrix(IdViewProj, viewProj);
                cmd.SetGlobalVector(IdCamPos, camPos);
                cmd.SetGlobalVector(IdLowRes, new Vector4(srcW, srcH, 0, 0));
                cmd.SetGlobalFloat(IdTime, Application.isPlaying ? Time.time : Time.realtimeSinceStartup);
                GatherLights(cmd);

                for (int i = 0; i < _draw.Count; i++)
                {
                    SplatRenderer r = _draw[i];
                    Matrix4x4 toWorld = r.transform.localToWorldMatrix;
                    cmd.SetGlobalMatrix(IdObjToWorld, toWorld);
                    cmd.SetGlobalMatrix(IdObjToWorldIT, toWorld.inverse.transpose);
                    cmd.SetGlobalBuffer(IdSurfels, r.Buffer);
                    cmd.SetGlobalFloat(IdExposure, r.exposure);
                    cmd.SetGlobalVector(IdFog, new Vector4(r.fog.x, r.fog.y, 0, 0));
                    cmd.SetGlobalVector(IdParams, new Vector4(falloff, settings.minTexels,
                        r.bands > 0 ? r.bands : 0, r.cullBackfaces ? 1 : 0));
                    cmd.SetGlobalFloat(IdSplatBase, 0);

                    if (settings.crisp)
                    {
                        if (r.OpaqueCount > 0)
                        {
                            cmd.SetGlobalFloat(IdSplatBase, 0);
                            cmd.DrawProcedural(Matrix4x4.identity, _splat, 0, MeshTopology.Triangles, 6, r.OpaqueCount);
                        }
                        int glowCount = r.Count - r.OpaqueCount;
                        if (glowCount > 0)
                        {
                            cmd.SetGlobalFloat(IdSplatBase, r.OpaqueCount);
                            cmd.DrawProcedural(Matrix4x4.identity, _splat, 1, MeshTopology.Triangles, 6, glowCount);
                        }
                    }
                    else
                    {
                        // soft mode: everything blended, the way a plain splat renderer draws
                        cmd.SetGlobalFloat(IdSplatBase, 0);
                        cmd.DrawProcedural(Matrix4x4.identity, _splat, 1, MeshTopology.Triangles, 6, r.Count);
                    }
                }

                // ---- 2. one fragment per art pixel: coverage, vote, outline ---------
                cmd.SetGlobalTexture(IdSplatTex, _splatRT);
                cmd.SetGlobalVector(IdSrcSize, new Vector4(srcW, srcH, 0, 0));
                cmd.SetGlobalVector(IdArtSize, new Vector4(artW, artH, 0, 0));
                cmd.SetGlobalVector(IdPassFlags, new Vector4(ss, settings.outline ? 1 : 0,
                    settings.quantise ? 1 : 0, settings.dither));
                cmd.SetGlobalVector(IdPassFlags2, new Vector4(settings.crisp ? 1 : 0, pixelSize, 0, 0));

                if (settings.debugRawSplat)
                {
                    // the small render on its own: tells the splats apart from the pixel pass
                    CoreUtils.SetRenderTarget(cmd, screen);
                    cmd.SetViewport(new Rect(0, 0, screenW, screenH));
                    cmd.DrawProcedural(Matrix4x4.identity, _composite, 2, MeshTopology.Triangles, 3);
                    context.ExecuteCommandBuffer(cmd);
                    CommandBufferPool.Release(cmd);
                    return;
                }

                cmd.SetRenderTarget(_artRT);
                cmd.ClearRenderTarget(true, true, Color.clear);
                cmd.SetViewport(new Rect(0, 0, artW, artH));
                cmd.DrawProcedural(Matrix4x4.identity, _composite, 0, MeshTopology.Triangles, 3);

                // ---- 3. enlarge, point sampled, display space -> linear -------------
                cmd.SetGlobalTexture(IdArtTex, _artRT);
                CoreUtils.SetRenderTarget(cmd, screen);
                cmd.SetViewport(new Rect(0, 0, screenW, screenH));
                cmd.DrawProcedural(Matrix4x4.identity, _composite, 1, MeshTopology.Triangles, 3);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            void CollectDrawable(int cullingMask)
            {
                _draw.Clear();
                for (int i = 0; i < SplatRenderer.Active.Count; i++)
                {
                    SplatRenderer r = SplatRenderer.Active[i];
                    if (r == null || !r.isActiveAndEnabled || r.Buffer == null) continue;
                    if ((cullingMask & (1 << r.gameObject.layer)) == 0) continue;
                    _draw.Add(r);
                }
            }

            void UpdatePalette(CommandBuffer cmd)
            {
                palette.Resolve(_colors, out int surfaceCount, out Color background);
                int total = Mathf.Min(_colors.Count, 64);
                for (int i = 0; i < 64; i++)
                {
                    Color c = i < total ? _colors[i] : Color.black;
                    _paletteData[i] = new Vector4(c.r, c.g, c.b, 1f);
                }
                _palette.SetData(_paletteData);
                cmd.SetGlobalBuffer(IdPalette, _palette);
                cmd.SetGlobalVector(IdPaletteCount, new Vector4(surfaceCount, total, 0, 0));
                cmd.SetGlobalVector(IdBackground, new Vector4(background.r, background.g, background.b, 1f));
            }

            void GatherLights(CommandBuffer cmd)
            {
                Light[] all = Object.FindObjectsOfType<Light>();
                Vector3 sun = DefaultSun;
                Light sunLight = RenderSettings.sun;
                if (sunLight == null)
                    for (int i = 0; i < all.Length; i++)
                        if (all[i].type == LightType.Directional && all[i].isActiveAndEnabled) { sunLight = all[i]; break; }
                if (sunLight != null) sun = -sunLight.transform.forward;
                cmd.SetGlobalVector(IdSunDir, sun);

                int count = 0;
                for (int i = 0; i < all.Length && count < 4; i++)
                {
                    Light l = all[i];
                    if (!l.isActiveAndEnabled || l.type == LightType.Directional) continue;
                    Color c = l.color;
                    float k = l.intensity * 0.35f;
                    _pointPos[count] = l.transform.position;
                    _pointColor[count] = new Vector4(c.r * k, c.g * k, c.b * k, 0);
                    count++;
                }
                for (int i = count; i < 4; i++)
                {
                    _pointPos[i] = new Vector4(0, -99, 0, 1);
                    _pointColor[i] = Vector4.zero;
                }
                cmd.SetGlobalVectorArray(IdPointPos, _pointPos);
                cmd.SetGlobalVectorArray(IdPointColor, _pointColor);
                cmd.SetGlobalVector(IdLightCount, new Vector4(count, 0, 0, 0));
            }
        }
    }
}
