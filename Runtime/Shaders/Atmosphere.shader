Shader "Hidden/Atmosphere"
{
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent" // Both passes in Transparent queue
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }
        LOD 100

        // --- PASS 0: Standard Sky/Limb Rendering ---
        Pass
        {
            Name "Atmosphere Sky"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual  // Standard depth test: Draw if closer or equal
            Cull Off     // Standard culling: Don't draw back faces

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            // Include common core libraries
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Include shared atmosphere logic
            #include "AtmosphereShared.hlsl"

            ENDHLSL
        }

        // --- PASS 1: Internal Haze Rendering ---
        Pass
        {
            Name "Atmosphere Haze (Internal)"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater // Depth test: Draw ONLY if further away (back faces are further)
            Cull Front    // Cull FRONT faces: Render only back faces

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            // Include common core libraries
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Include shared atmosphere logic
            #include "AtmosphereShared.hlsl"

            ENDHLSL
        }
    }
    Fallback Off
}