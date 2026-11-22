// Two passes over the same surfel buffer: solid, then translucent light.
// The C# side picks them; see PixelArtSplatFeature.

Shader "PixelArtSplats/Surfel"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        // ---- 0: solid. Written as-is into the low-resolution buffer. -------------
        Pass
        {
            Name "SurfelSolid"
            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertSurfel
            #pragma fragment FragSolid
            #pragma exclude_renderers gles gles3

            #include "SurfelSplat.hlsl"
            ENDHLSL
        }

        // ---- 1: translucent light (halos, beams, smoke). Blended, no depth write. --
        Pass
        {
            Name "SurfelGlow"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertSurfel
            #pragma fragment FragBlended
            #pragma exclude_renderers gles gles3

            #include "SurfelSplat.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
}
