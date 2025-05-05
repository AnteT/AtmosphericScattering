// AtmosphereShared.hlsl

#ifndef PHYSICS_ATMOSPHERE_SHARED_HLSL
#define PHYSICS_ATMOSPHERE_SHARED_HLSL

#ifndef PI
    #define PI 3.1415926535f
#endif
#define EPSILON 1e-6f

// Primay atmospheric haze and scattering
#define INSCATTER_STEPS_PASS0     16
#define OPTICAL_DEPTH_STEPS_PASS0 8

// Inside atmosphere looking at surface
#define INSCATTER_STEPS_PASS1     8
#define OPTICAL_DEPTH_STEPS_PASS1 4
#define TERMINATOR_FALLOFF 1 // How quickly the ambient brightness modifier fallsoff as it wraps around towards the anti-light point opposite the main sun dir.
// --- Structures ---
struct Attributes
{
    float4 positionOS   : POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS       : SV_POSITION;
    float3 positionWS       : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// --- Constant Buffers and Globals ---
CBUFFER_START(UnityPerMaterial)
    float3 _PlanetWorldPosition;
    float  _PlanetRadius;
    float  _AtmosphereRadius;
    float  _RayleighScaleHeight;
    float  _MieScaleHeight;
    float  _DensityScale;
    float3 _RayleighScatteringCoeff;
    float3 _MieScatteringCoeff;
    float  _MieG;
    float3 _OzoneAbsorptionCoeff;
    float  _OzoneCenterAltitudeNorm;
    float  _OzoneWidth;
    float  _SunIntensity;
    float3 _AtmosphereTint;
    float  _DensityEdgeSmoothness;
    float  _AmbientIntensity;
CBUFFER_END

CBUFFER_START(UnityPerPass)
    int _ShaderPassIndex;
CBUFFER_END

float3 _SunDirection;

// --- Helper Functions ---

bool RaySphereIntersect(float3 rayOrigin, float3 rayDir, float sphereRadius, out float2 t)
{
    t = float2(-1.0f, -1.0f);
    float3 oc = rayOrigin;
    float b = dot(oc, rayDir);
    float c = dot(oc, oc) - sphereRadius * sphereRadius;
    float h = b * b - c;
    if (h < 0.0f) return false;
    h = sqrt(h);
    t.x = -b - h;
    t.y = -b + h;
    return t.y >= 0.0f;
}

float RayleighPhase(float cosTheta)
{
    return (3.0f / (16.0f * PI)) * (1.0f + cosTheta * cosTheta);
}

float MiePhase(float cosTheta, float g)
{
    float g2 = g * g;
    float numerator = (1.0f - g2);
    float denominator = 1.0f + g2 - 2.0f * g * cosTheta;
    denominator = pow(max(abs(denominator), 1e-4f), 1.5f);
    return (1.0f / (4.0f * PI)) * (numerator / denominator);
}

void GetAltitude(float sampleDistFromCenter, float planetRadius, float atmosphereRadius, out float altitude, out float normAltitude)
{
    altitude = max(0.0f, sampleDistFromCenter - planetRadius);
    float atmosphereHeight = max(EPSILON, atmosphereRadius - planetRadius);
    normAltitude = saturate(altitude / atmosphereHeight);
}

// --- MODIFIED GetDensity (Kept) ---
float GetDensity(float altitude, float normAltitude, float scaleHeight)
{
    scaleHeight = max(EPSILON, scaleHeight);
    float expDensity = exp(-altitude / scaleHeight);
    float smoothedDensity = expDensity * saturate(1.0 - normAltitude);
    return lerp(expDensity, smoothedDensity, _DensityEdgeSmoothness);
}


float GetOzoneDensity(float normAltitude, float ozoneCenterNormAlt, float ozoneWidth)
{
    float x = (normAltitude - ozoneCenterNormAlt) / max(EPSILON, ozoneWidth * 0.5f);
    return exp(-x * x);
}

float3 GetOpticalDepth(
    float3 rayOriginRel, float3 rayDir, float rayLength, int numSteps,
    float planetRadius, float atmosphereRadius, float rayleighScaleH, float mieScaleH,
    float3 betaRayleigh, float3 betaMie, float3 betaOzoneAbs,
    float ozoneCenterNorm, float ozoneWidth)
{
    float3 opticalDepth = float3(0.0f, 0.0f, 0.0f);
    if (numSteps <= 0 || rayLength < EPSILON) return opticalDepth;
    float stepSize = rayLength / (float)numSteps;

    for (int i = 0; i < numSteps; ++i)
    {
        float sampleT = (float(i) + 0.5f) * stepSize;
        sampleT = min(sampleT, rayLength - 1e-5f);
        float3 samplePosRel = rayOriginRel + rayDir * sampleT;
        float sampleDistFromCenter = length(samplePosRel);

        if (sampleDistFromCenter >= planetRadius && sampleDistFromCenter <= atmosphereRadius)
        {
            float altitude, normAltitude;
            
            // GetAltitude(sampleDistFromCenter, planetRadius, atmosphereRadius, altitude, normAltitude);
            altitude = max(0.0f, sampleDistFromCenter - planetRadius);
            float atmosphereHeight = max(EPSILON, atmosphereRadius - planetRadius);
            normAltitude = saturate(altitude / atmosphereHeight);


            float densityR = GetDensity(altitude, normAltitude, rayleighScaleH);
            float densityM = GetDensity(altitude, normAltitude, mieScaleH);
            float densityOzone = GetOzoneDensity(normAltitude, ozoneCenterNorm, ozoneWidth);
            float3 localExtinction = (betaRayleigh * densityR) + (betaMie * densityM) + (betaOzoneAbs * densityOzone);
            opticalDepth += localExtinction * stepSize;
        }
    }
    return max(0.0f, opticalDepth);
}

float3 CalculateSunTransmittance(
    float3 samplePosRel, float3 sunDir, int numOpticalDepthSteps,
    float planetRadius, float atmosphereRadius, float rayleighScaleH, float mieScaleH,
    float3 betaRayleigh, float3 betaMie, float3 betaOzoneAbs, float densityScale,
    float ozoneCenterNorm, float ozoneWidth)
{
    float2 planetHit;
    if (RaySphereIntersect(samplePosRel, sunDir, planetRadius, planetHit) && planetHit.x > EPSILON)
    {
        return float3(0.0f, 0.0f, 0.0f);
    }
    float2 sunAtmoHit;
    float sunRayLength = 0.0f;
    if (RaySphereIntersect(samplePosRel, sunDir, atmosphereRadius, sunAtmoHit))
    {
        sunRayLength = max(0.0f, sunAtmoHit.y);
    }
    float3 sunOpticalDepth = float3(0.0f, 0.0f, 0.0f);
    if (sunRayLength > EPSILON)
    {
         sunOpticalDepth = GetOpticalDepth(
             samplePosRel, sunDir, sunRayLength, numOpticalDepthSteps,
             planetRadius, atmosphereRadius, rayleighScaleH, mieScaleH,
             betaRayleigh, betaMie, betaOzoneAbs,
             ozoneCenterNorm, ozoneWidth);
    }
    float3 sunTransmittance = exp(-sunOpticalDepth * densityScale);
    return sunTransmittance;
}


// --- Main In-Scattering Calculation ---
float4 CalculateInScatter(
    float3 viewRayStartRel, float3 viewDir, float viewRayLength,
    float planetRadius, float atmosphereRadius,
    float rayleighScaleH, float mieScaleH, float densityScale,
    float3 betaRayleigh, float3 betaMie, float3 betaOzoneAbs,
    float mieG, float ozoneCenterNorm, float ozoneWidth,
    float3 sunDir, float sunIntensity, float3 tint)
{
    float3 totalInScatter = float3(0.0f, 0.0f, 0.0f);
    float3 totalViewOpticalDepth = float3(0.0f, 0.0f, 0.0f);

    int inscatterSteps = (_ShaderPassIndex == 1) ? INSCATTER_STEPS_PASS1 : INSCATTER_STEPS_PASS0;
    int opticalDepthSteps = (_ShaderPassIndex == 1) ? OPTICAL_DEPTH_STEPS_PASS1 : OPTICAL_DEPTH_STEPS_PASS0;

    if (inscatterSteps <= 0 || viewRayLength < EPSILON)
    {
        // If no view ray, return fully transparent black
        return float4(0, 0, 0, 0);
    }

    float stepSize = viewRayLength / (float)inscatterSteps;
    float viewRayLengthMinusEps = viewRayLength - 1e-5f;

    for (int i = 0; i < inscatterSteps; ++i)
    {
        float sampleT = (float(i) + 0.5f) * stepSize;
        sampleT = min(sampleT, viewRayLengthMinusEps);
        float3 samplePosRel = viewRayStartRel + viewDir * sampleT;
        float sampleDistFromCenter = length(samplePosRel);

        if (sampleDistFromCenter < planetRadius || sampleDistFromCenter > atmosphereRadius)
        {
            continue; // Skip samples outside the atmosphere shell
        }

        // --- Calculate Densities and Extinction ---
        float altitude = max(0.0f, sampleDistFromCenter - planetRadius);
        float atmosphereHeight = max(EPSILON, atmosphereRadius - planetRadius);
        float normAltitude = saturate(altitude / atmosphereHeight);

        float localDensityR = GetDensity(altitude, normAltitude, rayleighScaleH);
        float localDensityM = GetDensity(altitude, normAltitude, mieScaleH);
        float localDensityOzone = GetOzoneDensity(normAltitude, ozoneCenterNorm, ozoneWidth);

        float3 baseLocalExtinction = (betaRayleigh * localDensityR) + (betaMie * localDensityM) + (betaOzoneAbs * localDensityOzone);
        float3 scaledLocalExtinction = baseLocalExtinction * densityScale;

        // --- Calculate View Transmittance ---
        // Transmittance from camera to the *start* of this step
        float3 viewTransmittanceToStepStart = exp(-totalViewOpticalDepth);
        // Accumulate optical depth for the *next* step
        totalViewOpticalDepth += scaledLocalExtinction * stepSize;

        // --- Calculate Sun Transmittance to Sample Point ---
        float3 sunTransmittance = CalculateSunTransmittance(
            samplePosRel, sunDir, opticalDepthSteps,
            planetRadius, atmosphereRadius, rayleighScaleH, mieScaleH,
            betaRayleigh, betaMie, betaOzoneAbs, densityScale,
            ozoneCenterNorm, ozoneWidth);

        // --- Calculate Phase Functions ---
        // Use dot(viewDir, sunDir) - phase relative to original sun direction
        float cosTheta = dot(viewDir, sunDir);
        float rayleighPhaseVal = RayleighPhase(cosTheta); // Less directional peak
        float miePhaseVal = MiePhase(cosTheta, mieG);    // Can have extreme peak
        
        // --- Calculate Base Phased Scattering Potential (for Direct) ---
        // Includes full contribution from both potentially peaky phase functions
        float3 baseScatteringPhasedDirect = (betaRayleigh * rayleighPhaseVal * localDensityR) + (betaMie * miePhaseVal * localDensityM);
        baseScatteringPhasedDirect *= densityScale;
        
        // --- Calculate Direct Contribution ---
        // Apply sun intensity and transmittance to the full phased scattering base
        float3 directContribution = sunIntensity * sunTransmittance * baseScatteringPhasedDirect;
        
        
        // --- Calculate Ambient "Wrap" Contribution (with Dampened Mie) ---
        
        // 1. Selectively Dampen Mie Phase for Ambient:
        //    We want to reduce the impact of the sharp Mie peak for ambient light.
        //    Let's use a much lower clamp value specifically for the Mie phase in ambient.
        //    Alternatively, could lerp towards 1.0 (uniform), but clamping is simpler.
        //    TUNABLE: MIE_AMBIENT_CLAMP controls how much Mie directionality affects ambient.
        //    Try values like 1.0, 2.0, or 5.0. Lower values make ambient more uniform.
        const float MIE_AMBIENT_CLAMP = 2.0;
        float miePhaseAmbient = min(miePhaseVal, MIE_AMBIENT_CLAMP);
        //    Keep Rayleigh phase unclamped as it's less peaky.
        float rayleighPhaseAmbient = rayleighPhaseVal;
        
        // 2. Calculate Ambient Base using Dampened Mie:
        float3 ambientBasePhasedDampened = (betaRayleigh * rayleighPhaseAmbient * localDensityR) + (betaMie * miePhaseAmbient * localDensityM);
        ambientBasePhasedDampened *= densityScale; // Apply density scale
        
        // 3. Base Ambient Strength (Decoupled):
        //    Scale the dampened-Mie base ONLY by the user-controlled _AmbientIntensity.
        float3 potentialAmbient = ambientBasePhasedDampened * _AmbientIntensity;
        
        // 4. Modulation Factors:
        float avgSunVisibility = dot(saturate(sunTransmittance), float3(1.0/3.0, 1.0/3.0, 1.0/3.0));
        float shadowFactor = 1.0 - avgSunVisibility; // Simple factor
        float3 sampleDirFromCenter = normalize(samplePosRel);
        float cosSunAngle = dot(sampleDirFromCenter, sunDir);
        float baseFalloff = saturate(cosSunAngle + 1.0);
        float terminatorFalloff = pow(baseFalloff, TERMINATOR_FALLOFF); // Use #define
        
        // 5. Final Ambient Term for this step:
        float3 stepAmbientContribution = potentialAmbient * shadowFactor * terminatorFalloff;
        // --- End Ambient Contribution ---
        
        
        // --- Combine Contributions for this step ---
        // Add direct contribution (full phase) and the ambient contribution (dampened Mie phase)
        totalInScatter += (directContribution + stepAmbientContribution) * viewTransmittanceToStepStart * stepSize;
    }
        
        // ... (rest of the loop) ...
        
        // --- Final Color & Alpha Calculation (remains the same) ---
        totalInScatter *= tint;
        totalInScatter = max(0.0f, totalInScatter);
        float avgOpticalDepth = dot(totalViewOpticalDepth, float3(1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f));
        float opticalDepthAlpha = saturate(1.0f - exp(-avgOpticalDepth));
        float brightnessFactor = saturate(dot(totalInScatter, float3(1,1,1)) * 0.5f); // Adjust 0.5 multiplier if needed
        float finalAlpha = opticalDepthAlpha * brightnessFactor;
        
        return float4(totalInScatter, finalAlpha);
}

// --- Vertex Shader ---
Varyings vert(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.positionCS = TransformWorldToHClip(output.positionWS);
    return output;
}

// --- Fragment Shader ---
float4 frag(Varyings input) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float3 cameraPosWS = _WorldSpaceCameraPos;
    float3 viewDir = normalize(input.positionWS - cameraPosWS);
    float3 rayOriginRel = cameraPosWS - _PlanetWorldPosition;

    if (length(rayOriginRel) < _PlanetRadius - EPSILON)
    {
        return float4(0, 0, 0, 0);
    }

    float2 atmosphereHit;
    bool intersectsAtmosphere = RaySphereIntersect(rayOriginRel, viewDir, _AtmosphereRadius, atmosphereHit);
    float4 finalColor = float4(0, 0, 0, 0);

    if (intersectsAtmosphere && atmosphereHit.y > EPSILON)
    {
        float rayStartT = max(0.0f, atmosphereHit.x);
        float rayEndT = atmosphereHit.y;
        float2 planetHit;
        if (RaySphereIntersect(rayOriginRel, viewDir, _PlanetRadius, planetHit))
        {
            if (planetHit.x > rayStartT + EPSILON && planetHit.x < rayEndT)
            {
                rayEndT = planetHit.x;
            }
        }
        float rayLength = max(0.0f, rayEndT - rayStartT);
        if (rayLength > EPSILON)
        {
            float3 viewRayStartPosRel = rayOriginRel + viewDir * rayStartT;
            finalColor = CalculateInScatter(
                viewRayStartPosRel, viewDir, rayLength,
                _PlanetRadius, _AtmosphereRadius,
                _RayleighScaleHeight, _MieScaleHeight, _DensityScale,
                _RayleighScatteringCoeff, _MieScatteringCoeff, _OzoneAbsorptionCoeff, _MieG,
                _OzoneCenterAltitudeNorm, _OzoneWidth,
                _SunDirection, _SunIntensity, _AtmosphereTint);
        }
    }

    finalColor.rgb = max(0.0f, finalColor.rgb);
    return finalColor;
}

#endif // PHYSICS_ATMOSPHERE_SHARED_HLSL