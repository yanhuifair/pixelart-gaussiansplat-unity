// The pixel pass: turns a small render of hard-edged splats into pixel art.
//
// Port of src/render/post.js. Two draws:
//
//   pass 0 (art resolution)  one fragment per art pixel:
//       1. Supersample: the splats were drawn at twice the art resolution, so every
//          art pixel has four samples.
//       2. Coverage: a sample is solid when an opaque splat covers it. An art pixel
//          with at least half its samples solid belongs to the object.
//       3. Palette by vote: each solid sample snaps to its nearest palette colour and
//          the pixel takes the colour most of them chose, so no pixel is an average
//          of two materials.
//       4. Outline: a solid pixel next to the background takes a darker ink of its
//          own colour, kept bright enough to read against a dark background.
//       5. Translucent light is blended over the background and snapped to the
//          palette in its own hue, so a beam is a flat band, not a scatter of dots.
//   pass 1 (screen)          point-sampled enlargement of the art, display -> linear.
//
// With dither above 0 the pass averages the samples and dithers between the two
// nearest palette colours with a 4x4 Bayer pattern. With crisp off, splats stay
// soft and are blended classically.

Shader "PixelArtSplats/Composite"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ArtPass"
            ZTest Always ZWrite Off Cull Off Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertArt
            #pragma fragment FragArt
            #pragma exclude_renderers gles gles3

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_COLOURS 64
            #define SOLID_ALPHA 0.95

            TEXTURE2D(_SplatTex);
            SAMPLER(sampler_PointClamp);

            float4 _SrcSize;      // the low-resolution render, in texels (art * ss)
            float4 _ArtSize;      // art pixels
            float4 _PassFlags;    // x ss, y outline, z quantise, w dither
            float4 _PassFlags2;   // x crisp
            float4 _Background;
            float4 _PaletteCount; // x surface colours, y palette (surface + glow)
            StructuredBuffer<float4> _Palette;

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings VertArt(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                o.uv = GetFullScreenTriangleTexCoord(vertexID);
                return o;
            }

            // 4x4 ordered dither threshold in (0, 1)
            float Bayer(int2 p)
            {
                static const float m[16] =
                {
                    0, 8, 2, 10,
                    12, 4, 14, 6,
                    3, 11, 1, 9,
                    15, 7, 13, 5
                };
                int i = ((p.x % 4) + 4) % 4 + (((p.y % 4) + 4) % 4) * 4;
                return (m[i] + 0.5) / 16.0;
            }

            // one source texel. Already premultiplied and in display space: the colour the
            // palette lives in, so nothing has to be converted before matching.
            float4 Texel(int2 t)
            {
                t = clamp(t, int2(0, 0), int2(_SrcSize.xy) - 1);
                return SAMPLE_TEXTURE2D_LOD(_SplatTex, sampler_PointClamp, (float2(t) + 0.5) / _SrcSize.xy, 0);
            }

            // share of an art pixel's samples covered by something opaque
            float Coverage(int2 cell)
            {
                if (cell.x < 0 || cell.y < 0 || cell.x >= (int)_ArtSize.x || cell.y >= (int)_ArtSize.y)
                    return 1.0; // the screen edge is not an outline

                if (_PassFlags.x < 1.5) return step(SOLID_ALPHA, Texel(cell).a);
                int2 t = cell * 2;
                return 0.25 * (step(SOLID_ALPHA, Texel(t).a)
                             + step(SOLID_ALPHA, Texel(t + int2(1, 0)).a)
                             + step(SOLID_ALPHA, Texel(t + int2(0, 1)).a)
                             + step(SOLID_ALPHA, Texel(t + int2(1, 1)).a));
            }

            bool IsBackground(float3 c)
            {
                return distance(c, _Background.rgb) < 0.004;
            }

            // nearest palette entry; solid pixels never take the background colour
            int Nearest(float3 c, bool solid)
            {
                int best = 0;
                float bd = 1e9;
                int n = (int)_PaletteCount.x;
                for (int i = 0; i < MAX_COLOURS; i++)
                {
                    if (i >= n) break;
                    float3 p = _Palette[i].rgb;
                    if (solid && _PaletteCount.y > 1.0 && IsBackground(p)) continue;
                    float3 e = c - p;
                    float3 w = e * float3(0.30, 0.59, 0.11); // weigh by how bright each channel looks
                    float d = dot(w, w) + 0.25 * dot(e, e);
                    if (d < bd) { bd = d; best = i; }
                }
                return best;
            }

            float3 Oklab(float3 c)
            {
                c = pow(max(c, 0.0), 2.2);
                float3 lms = pow(float3(0.4122214708 * c.r + 0.5363325363 * c.g + 0.0514459929 * c.b,
                                        0.2119034982 * c.r + 0.6806995451 * c.g + 0.1073969566 * c.b,
                                        0.0883024619 * c.r + 0.2817188376 * c.g + 0.6299787005 * c.b) + 1e-9,
                                 1.0 / 3.0);
                return float3(0.2104542553 * lms.x + 0.793617785 * lms.y - 0.0040720468 * lms.z,
                              1.9779984951 * lms.x - 2.428592205 * lms.y + 0.4505937099 * lms.z,
                              0.0259040371 * lms.x + 0.7827717662 * lms.y - 0.808675766 * lms.z);
            }

            float2 HueDir(float3 lab) { return lab.yz / (length(lab.yz) + 0.035); }

            // translucent light over the background: the palette colour nearest the blend in
            // OKLab, kept in the light's own hue (a faint gold beam becomes a muted gold)
            float3 Haze(float3 light, float a)
            {
                if (a < 0.3) return _Background.rgb;
                float3 t = Oklab(lerp(_Background.rgb, light, min(1.0, a * 1.6)));
                float2 h = HueDir(Oklab(light));
                float3 best = _Background.rgb;
                float bd = 1e9;
                int n = (int)_PaletteCount.y;
                for (int i = 0; i < MAX_COLOURS; i++)
                {
                    if (i >= n) break;
                    float3 pc = _Palette[i].rgb;
                    float3 p = Oklab(pc);
                    float3 e = t - p;
                    float2 dh = HueDir(p) - h;
                    bool bg = IsBackground(pc);
                    float d = 1.4 * e.x * e.x + dot(e.yz, e.yz) + (bg ? 0.0 : 0.12 * dot(dh, dh));
                    if (d < bd) { bd = d; best = pc; }
                }
                return best;
            }

            float3 SnapDither(float3 col, int2 cell, bool solid)
            {
                float3 c1 = col, c2 = col;
                float d1 = 1e9, d2 = 1e9;
                int n = (int)_PaletteCount.x;
                for (int i = 0; i < MAX_COLOURS; i++)
                {
                    if (i >= n) break;
                    float3 p = _Palette[i].rgb;
                    if (solid && _PaletteCount.y > 1.0 && IsBackground(p)) continue;
                    float3 e = col - p;
                    float3 w = e * float3(0.30, 0.59, 0.11);
                    float d = dot(w, w) + 0.25 * dot(e, e);
                    if (d < d1) { d2 = d1; c2 = c1; d1 = d; c1 = p; }
                    else if (d < d2) { d2 = d; c2 = p; }
                }
                float3 axis = c2 - c1;
                float t = clamp(dot(col - c1, axis) / max(dot(axis, axis), 1e-5), 0.0, 0.5);
                return Bayer(cell) < t * _PassFlags.w ? c2 : c1;
            }

            // a darker shade of a colour, never so dark that it vanishes into the background
            float3 Ink(float3 body)
            {
                float3 tone = body * 0.64;
                tone = min(float3(1.0, 1.0, 1.0), tone * max(1.0, 0.12 / max(dot(tone, float3(0.30, 0.55, 0.15)), 0.001)));
                return lerp(body, tone, 0.65);
            }

            float4 FragArt(Varyings i) : SV_Target
            {
                int2 cell = int2(floor(i.uv * _ArtSize.xy)); // which art pixel
                int N = _PassFlags.x < 1.5 ? 1 : 4;

                float4 smp[4];
                float4 sum = 0.0;
                float4 solidSum = 0.0;
                for (int k = 0; k < 4; k++)
                {
                    if (k >= N) break;
                    smp[k] = (N == 1) ? Texel(cell) : Texel(cell * 2 + int2(k - (k / 2) * 2, k / 2));
                    sum += smp[k];
                    if (smp[k].a > SOLID_ALPHA) solidSum += float4(smp[k].rgb / smp[k].a, 1.0);
                }
                sum /= (float)N;
                float cov = solidSum.a / (float)N;
                bool quantise = _PassFlags.z > 0.5;
                bool dither = _PassFlags.w > 0.0;
                float3 col;

                if (_PassFlags2.x < 0.5)
                {
                    // soft Gaussians: plain blending over the background
                    col = sum.rgb + _Background.rgb * (1.0 - clamp(sum.a, 0.0, 1.0));
                    if (quantise) col = dither ? SnapDither(col, cell, false) : _Palette[Nearest(col, false)].rgb;
                }
                else if (cov >= 0.5)
                {
                    // the object. Its colour: the palette colour most samples snap to
                    float3 body = solidSum.rgb / max(solidSum.a, 1e-4);
                    bool edge = false;
                    if (_PassFlags.y > 0.5)
                    {
                        int n = (int)(Coverage(cell + int2(1, 0)) >= 0.5)
                              + (int)(Coverage(cell - int2(1, 0)) >= 0.5)
                              + (int)(Coverage(cell + int2(0, 1)) >= 0.5)
                              + (int)(Coverage(cell - int2(0, 1)) >= 0.5);
                        edge = n > 0 && n < 4; // isolated dots (embers, fireflies) keep their colour
                    }

                    if (quantise && !dither)
                    {
                        int lab[4];
                        for (int a = 0; a < 4; a++)
                        {
                            lab[a] = -1;
                            if (a < N && smp[a].a > SOLID_ALPHA) lab[a] = Nearest(smp[a].rgb / smp[a].a, true);
                        }
                        int best = 0, score = 0;
                        for (int a = 0; a < 4; a++)
                        {
                            if (lab[a] < 0) continue;
                            int repeats = 0;
                            for (int b = 0; b < 4; b++) if (lab[b] == lab[a]) repeats++;
                            if (repeats > score) { score = repeats; best = lab[a]; }
                        }
                        col = _Palette[best].rgb;
                        if (edge) col = _Palette[Nearest(Ink(col), true)].rgb;
                    }
                    else
                    {
                        if (edge) body = Ink(body);
                        if (quantise) col = edge ? _Palette[Nearest(body, true)].rgb : SnapDither(body, cell, true);
                        else col = body;
                    }
                }
                else
                {
                    // background, with whatever translucent light lies over it
                    float a = clamp(sum.a, 0.0, 1.0);
                    col = _Background.rgb * (1.0 - a) + sum.rgb;
                    if (quantise) col = dither ? SnapDither(col, cell, false) : Haze(sum.rgb / max(a, 1e-4), a);
                }

                return float4(col, cov);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Enlarge"
            ZTest Always ZWrite Off Cull Off Blend Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertUpscale
            #pragma fragment FragUpscale
            #pragma exclude_renderers gles gles3

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_ArtTex);
            SAMPLER(sampler_PointClamp);
            float4 _PassFlags2; // x crisp, y pixel size

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings VertUpscale(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                o.uv = GetFullScreenTriangleTexCoord(vertexID);
                return o;
            }

            float4 FragUpscale(Varyings i) : SV_Target
            {
                // point sampling: one art pixel, one square of screen
                float3 c = SAMPLE_TEXTURE2D_LOD(_ArtTex, sampler_PointClamp, i.uv, 0).rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
                // the palette lives in display space; the camera target wants linear
                c = SRGBToLinear(c);
            #endif
                return float4(c, 1.0);
            }
            ENDHLSL
        }

        // For checking: the small splat render on its own, magnified.
        Pass
        {
            Name "RawSplat"
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertRaw
            #pragma fragment FragRaw
            #pragma exclude_renderers gles gles3

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_SplatTex);
            SAMPLER(sampler_PointClamp);

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings VertRaw(uint vertexID : SV_VertexID)
            {
                Varyings o;
                o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                o.uv = GetFullScreenTriangleTexCoord(vertexID);
                return o;
            }

            float4 FragRaw(Varyings i) : SV_Target
            {
                float4 c = SAMPLE_TEXTURE2D_LOD(_SplatTex, sampler_PointClamp, i.uv, 0);
                float3 rgb = c.a > 0.001 ? c.rgb / c.a : c.rgb;
            #ifndef UNITY_COLORSPACE_GAMMA
                rgb = SRGBToLinear(rgb);
            #endif
                return float4(rgb, 1.0);
            }
            ENDHLSL
        }

    }

    Fallback Off
}
