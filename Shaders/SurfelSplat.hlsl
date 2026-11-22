// Shared program for both passes of PixelArtSplats/Surfel.
//
// Port of src/render/shade.js (per-splat lighting) plus the surfel drawing Spark does.
// A surfel is drawn as one instanced quad, six vertices, no mesh asset:
//
//   * the two wide axes of the disc become the quad's edges, so the quad is the
//     splat's own footprint, projected. The disc test happens in the fragment on
//     the quad's own (u, v), which makes an ellipse in screen space for free.
//   * the corners carry their real world position, so depth is the depth of the
//     surfel plane at that pixel, not the centre's depth.
//   * the colour is worked out per splat in the vertex shader, because it is the
//     same for the whole disc. The fragment does almost nothing.

#ifndef PIXELART_SURFEL_INCLUDED
#define PIXELART_SURFEL_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct SurfelData
{
    float3 pos;
    float3 axis1;
    float3 axis2;
    float3 nrm;
    float3 col;
    uint flags;
};

StructuredBuffer<SurfelData> _Surfels;

float4x4 _PS_ObjToWorld;
float4x4 _PS_ObjToWorldIT;
float4x4 _PS_ViewProj;
float4 _PS_CamPos;
float4 _PS_SunDir;      // xyz: towards the sun
float4 _PS_Fog;         // x start, y end
float4 _PS_Params;      // x falloff, y min texels, z bands, w cull
float4 _PS_LowRes;      // x, y: size of the buffer being drawn into
float4 _PS_PointPos[4];
float4 _PS_PointColor[4];
float4 _PS_LightCount;  // x: how many point lights are live
float _PS_SplatBase;
float _PS_Exposure;
float _PS_Time;

#define PS_EMISSIVE  1u
#define PS_TWO_SIDED 2u
#define PS_GLOW      4u
#define PS_BAKED     8u
#define PS_UP_NORMAL 16u
#define PS_SWAY      32u

// brightness = ambient (darker from below, lighter from above) + key light
#define AMBIENT_LOW  0.36
#define AMBIENT_HIGH 0.58
#define DIFFUSE      0.62

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : TEXCOORD1;   // rgb + alpha, kept in one float4: float3 varyings get padded
};

float3 SwayOffset(SurfelData s)
{
    // a whole blade leans, the way particles.js leans one splat
    return float3(0.016 * sin(_PS_Time * 1.6 + s.pos.x * 5.0 + s.pos.z * 3.0), 0.0,
                  0.011 * cos(_PS_Time * 1.3 + s.pos.z * 4.0 - s.pos.x * 2.0));
}

float3 ShadeSurfel(SurfelData s, float3 pWS, float3 cWS, float3 camPos, out float alpha)
{
    uint kind = s.flags & 0xFFu;
    alpha = ((s.flags >> 8) & 0xFFu) * (1.0 / 255.0);

    float3 n = normalize(mul((float3x3)_PS_ObjToWorldIT, s.nrm));
    float3 toCam = camPos - cWS;
    float dist = max(length(toCam), 1e-4);
    float facing = dot(n, toCam / dist);
    float3 albedo = s.col;

    bool emissive = (kind & PS_EMISSIVE) != 0;
    bool baked = (kind & PS_BAKED) != 0;
    bool twoSided = (kind & PS_TWO_SIDED) != 0;

    float3 col = albedo;
    if ((kind & PS_GLOW) == 0)
    {
        if (twoSided && facing < 0.0) n = -n;
        if ((kind & PS_UP_NORMAL) != 0) n = normalize(n * 0.3 + float3(0.0, 1.0, 0.0));

        if (baked)
        {
            // the colour already holds its light (authoring/light.js)
            col = albedo * _PS_Exposure;
        }
        else
        {
            float ndl = dot(n, _PS_SunDir.xyz);
            float light = lerp(AMBIENT_LOW, AMBIENT_HIGH, n.y * 0.5 + 0.5)
                        + DIFFUSE * max(ndl, 0.0) + 0.22 * max(facing, 0.0);
            if (_PS_Params.z > 0.5) light = (floor(light * _PS_Params.z) + 0.5) / _PS_Params.z;

            col = albedo * light * _PS_Exposure;
            // a blade or a leaflet is lit as if it faced up, and a little dimmer for it
            if ((kind & PS_UP_NORMAL) != 0) col *= 0.74;
            col *= lerp(float3(0.92, 0.95, 1.05), float3(1.04, 1.0, 0.95), step(0.0, ndl));

            int count = (int)_PS_LightCount.x;
            for (int k = 0; k < 4; k++)
            {
                if (k >= count) break;
                float3 toLight = _PS_PointPos[k].xyz - pWS;
                float d = length(toLight);
                float w = (max(dot(n, toLight / max(d, 1e-4)), 0.0) * 0.85 + 0.15) / (1.0 + d * d * 9.0);
                col += (albedo * 0.85 + 0.1) * _PS_PointColor[k].rgb * w;
            }
        }

        if (emissive) col = albedo * 1.15;
        else col = lerp(col, float3(0.03, 0.035, 0.05), 0.75 * smoothstep(_PS_Fog.x, _PS_Fog.y, dist));
    }

    return col;
}

// Where the quad's corners go, and whether the splat is worth drawing at all.
bool ProjectSurfel(SurfelData s, float2 uv, float3 camPos, out float4 positionCS)
{
    uint kind = s.flags & 0xFFu;

    float3 centre = s.pos + (((kind & PS_SWAY) != 0) ? SwayOffset(s) : 0.0);
    float3 cWS = mul(_PS_ObjToWorld, float4(centre, 1.0)).xyz;
    float4 clipC = mul(_PS_ViewProj, float4(cWS, 1.0));
    positionCS = float4(0.0, 0.0, -2.0, 1.0);
    if (clipC.w <= 0.0001) return false;

    float3 n = normalize(mul((float3x3)_PS_ObjToWorldIT, s.nrm));
    float3 toCam = camPos - cWS;
    float facing = dot(n, toCam / max(length(toCam), 1e-4));
    bool twoSided = (kind & PS_TWO_SIDED) != 0;
    bool glow = (kind & PS_GLOW) != 0;
    // hide splats facing away, the way the viewer's cull does
    if (_PS_Params.w > 0.5 && !twoSided && !glow && facing < -0.08) return false;

    float3 pObj = centre + uv.x * s.axis1 + uv.y * s.axis2;
    float3 pWS = mul(_PS_ObjToWorld, float4(pObj, 1.0)).xyz;
    float4 clipP = mul(_PS_ViewProj, float4(pWS, 1.0));

    float2 ndcC = clipC.xy / clipC.w;
    float2 ndcP = clipP.xy / max(clipP.w, 1e-5);
    float2 d = ndcP - ndcC;

    // do not let a splat fall below a texel: distant surfaces would get holes
    float2 texels = abs(d) * _PS_LowRes.xy * 0.5;
    float biggest = max(texels.x, texels.y);
    float grow = (biggest > 1e-4) ? max(1.0, _PS_Params.y / biggest) : 1.0;

    float2 ndc = ndcC + d * grow;
    positionCS = float4(ndc * clipP.w, clipP.z, clipP.w);
    return true;
}

Varyings VertSurfel(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
{
    Varyings o;
    // two triangles of one quad
    float2 uv;
    uv.x = (vertexID == 1 || vertexID == 2 || vertexID == 4) ? 1.0 : -1.0;
    uv.y = (vertexID == 2 || vertexID == 4 || vertexID == 5) ? 1.0 : -1.0;

    uint index = instanceID + (uint)_PS_SplatBase;
    SurfelData s = _Surfels[index];

    float3 sway = ((s.flags & PS_SWAY) != 0) ? SwayOffset(s) : 0.0;
    float3 centre = s.pos + sway;
    float3 pObj = centre + uv.x * s.axis1 + uv.y * s.axis2;
    float3 pWS = mul(_PS_ObjToWorld, float4(pObj, 1.0)).xyz;
    float3 cWS = mul(_PS_ObjToWorld, float4(centre, 1.0)).xyz;
    float3 camPos = _PS_CamPos.xyz;

    float alpha;
    float3 col = ShadeSurfel(s, pWS, cWS, camPos, alpha);

    float4 positionCS;
    if (!ProjectSurfel(s, uv, camPos, positionCS))
    {
        col = 0.0;
        alpha = 0.0;
    }

    o.positionCS = positionCS;
    o.uv = uv;
    o.color = float4(col, alpha);
    return o;
}

float4 FragSolid(Varyings i) : SV_Target
{
    float d2 = dot(i.uv, i.uv);
    if (d2 > 1.0) discard;
    return float4(i.color.rgb, 1.0);
}

float4 FragBlended(Varyings i) : SV_Target
{
    float d2 = dot(i.uv, i.uv);
    if (d2 > 1.0) discard;
    // falloff 0 = the hard disc pixel art wants, 1 = a soft Gaussian
    float a = 1.0;
    if (_PS_Params.x > 0.001)
    {
        float soft = saturate((exp(-3.0 * d2) - 0.0497870684) / 0.9502129316);
        a = lerp(1.0, soft, _PS_Params.x);
    }
    a *= i.color.a;
    if (a < 0.004) discard;
    return float4(i.color.rgb * a, a);
}

#endif // PIXELART_SURFEL_INCLUDED
