using UnityEngine;

[CreateAssetMenu(fileName = "NewAtmosphereProfile", menuName = "Atmosphere/Atmosphere Profile", order = 1)]
public class AtmosphereProfile : ScriptableObject
{
    // Define the internal min/max range for the three primary scale factors
    public const float MIN_SCALE_FACTOR = 0.0001f;
    public const float MAX_SCALE_FACTOR = 0.01f;

    [System.Serializable]
    public class AtmosphereSettings
    {
        [Header("Geometry & Density")]

        [Tooltip("Physical radius of the planet body in world units (before transform scaling).")]
        [Min(0.1f)]
        public float planetRadius = 1;

        [Tooltip("Total height of the atmosphere above the planet surface (before transform scaling).")]
        [Min(0.0f)]
        public float atmosphereHeight = 1;

        [Tooltip("Overall multiplier for density effect, affecting opacity and scattering intensity.")]
        [Range(0.1f, 100.0f)]
        public float densityScale = 15.0f;

        [Tooltip("Blends density towards zero at the top edge of the atmosphere (0 = sharp edge, 1 = fully smoothed).")]
        [Range(0f, 1f)]
        public float densityEdgeSmoothness = 0.2f;

        [Header("Component Profiles")]

        [Tooltip("Normalized scale height for Rayleigh scattering density falloff (relative to atmosphere height).")]
        [Range(0.01f, 0.99f)]
        public float rayleighScaleHeightNorm = 0.084f;

        [Tooltip("Normalized scale height for Mie scattering density falloff (relative to atmosphere height).")]
        [Range(0.01f, 0.99f)]
        public float mieScaleHeightNorm = 0.012f;

        [Tooltip("Normalized altitude where Ozone layer density peaks (relative to atmosphere height).")]
        [Range(0.0f, 1.0f)]
        public float ozoneCenterAltitudeNorm = 0.25f;

        [Tooltip("Approximate width/thickness of the Ozone layer (normalized units, relative to atmosphere height).")]
        [Range(0.01f, 1.0f)]
        public float ozoneWidth = 0.3f;

        // --- Group 3: Optical Coefficients & Intensity ---
        [Header("Optical Coefficients & Intensity")]

        [Tooltip("Base Rayleigh scattering coefficients (RGB).")]
        [ColorUsage(false, true)]
        public Color rayleighScatteringCoeff = new Color(5.8f, 13.5f, 33.1f);

        [Tooltip("Adjusts Rayleigh scattering intensity (0 = min, 1 = max).")]
        [Range(0f, 1f)]
        public float rayleighScaleFactor = 0.1f; // Default maps to 0.001 using default MIN/MAX
        [System.NonSerialized] public float rayleighScaleFactorInternal = 0.001f;

        [Tooltip("Base Mie scattering coefficients (RGB).")]
        [ColorUsage(false, true)]
        public Color mieScatteringCoeff = new Color(3.9f, 3.9f, 3.9f);

        [Tooltip("Adjusts Mie scattering intensity (0 = min, 1 = max).")]
        [Range(0f, 1f)]
        public float mieScaleFactor = 0.1f;
        [System.NonSerialized] public float mieScaleFactorInternal = 0.001f;


        [Tooltip("Base Ozone absorption coefficients (RGB).")]
        [ColorUsage(false, true)]
        public Color ozoneAbsorptionCoeff = new Color(0.6f, 1.9f, 0.05f);

        [Tooltip("Adjusts Ozone absorption intensity (0 = min, 1 = max).")]
        [Range(0f, 1f)]
        public float ozoneScaleFactor = 0.1f;
        [System.NonSerialized] public float ozoneScaleFactorInternal = 0.001f;

        [Tooltip("Mie scattering directionality (-1: backscatter, 0: uniform, 1: forward scatter). Affects brightness around light source.")]
        [Range(-0.99f, 0.99f)]
        public float mieG = 0.76f;

        [Header("Lighting & Final Tint")]

        [Tooltip("Intensity multiplier for incoming sun light.")]
        [Min(0.0f)]
        public float sunIntensity = 20.0f;

        [Tooltip("Intensity factor for the approximated ambient/secondary scattering term which brightens shadowed areas. A reasonable default is 0.01 providing some attenuation to unlit side.")]
        [Range(0f, 100f)] // Allow a wider range for tuning, might need > 1
        public float ambientIntensity = 0.01f; // Start with a small default value

        [Tooltip("Optional Tint Color applied AFTER scattering calculations.")]
        [ColorUsage(false, true)]
        public Color atmosphereTint = Color.white;

        // --- Internal calculation method ---
        public void UpdateInternalScaleFactors()
        {
            // Use the correct public normalized field names here
            rayleighScaleFactorInternal = Mathf.Lerp(MIN_SCALE_FACTOR, MAX_SCALE_FACTOR, rayleighScaleFactor);
            mieScaleFactorInternal = Mathf.Lerp(MIN_SCALE_FACTOR, MAX_SCALE_FACTOR, mieScaleFactor);
            ozoneScaleFactorInternal = Mathf.Lerp(MIN_SCALE_FACTOR, MAX_SCALE_FACTOR, ozoneScaleFactor);
        }
    }

    public AtmosphereSettings settings = new AtmosphereSettings();

    // OnValidate remains the same, ensuring clamping and internal factor updates
    private void OnValidate()
    {
        if (settings == null) settings = new AtmosphereSettings();
        // Clamp public normalized sliders first
        settings.rayleighScaleFactor = Mathf.Clamp01(settings.rayleighScaleFactor);
        settings.mieScaleFactor = Mathf.Clamp01(settings.mieScaleFactor);
        settings.ozoneScaleFactor = Mathf.Clamp01(settings.ozoneScaleFactor);

        // Calculate internal factors based on normalized values
        settings.UpdateInternalScaleFactors();

        // Clamp Dimensions
        settings.planetRadius = Mathf.Max(0.1f, settings.planetRadius);
        settings.atmosphereHeight = Mathf.Max(0.0f, settings.atmosphereHeight);

        // Clamp Component Profiles
        settings.rayleighScaleHeightNorm = Mathf.Clamp(settings.rayleighScaleHeightNorm, 0.01f, 0.99f);
        settings.mieScaleHeightNorm = Mathf.Clamp(settings.mieScaleHeightNorm, 0.01f, 0.99f);
        settings.ozoneCenterAltitudeNorm = Mathf.Clamp01(settings.ozoneCenterAltitudeNorm);
        settings.ozoneWidth = Mathf.Max(0.01f, settings.ozoneWidth);

        // Clamp Density/Edge
        settings.densityScale = Mathf.Max(0.0f, settings.densityScale);
        settings.densityEdgeSmoothness = Mathf.Clamp01(settings.densityEdgeSmoothness);

        // Clamp Optical Coefficients & MieG
        settings.mieG = Mathf.Clamp(settings.mieG, -0.99f, 0.99f);
        settings.rayleighScatteringCoeff.r = Mathf.Max(0f, settings.rayleighScatteringCoeff.r); settings.rayleighScatteringCoeff.g = Mathf.Max(0f, settings.rayleighScatteringCoeff.g); settings.rayleighScatteringCoeff.b = Mathf.Max(0f, settings.rayleighScatteringCoeff.b);
        settings.mieScatteringCoeff.r = Mathf.Max(0f, settings.mieScatteringCoeff.r); settings.mieScatteringCoeff.g = Mathf.Max(0f, settings.mieScatteringCoeff.g); settings.mieScatteringCoeff.b = Mathf.Max(0f, settings.mieScatteringCoeff.b);
        settings.ozoneAbsorptionCoeff.r = Mathf.Max(0f, settings.ozoneAbsorptionCoeff.r); settings.ozoneAbsorptionCoeff.g = Mathf.Max(0f, settings.ozoneAbsorptionCoeff.g); settings.ozoneAbsorptionCoeff.b = Mathf.Max(0f, settings.ozoneAbsorptionCoeff.b);

        // Clamp Lighting
        settings.sunIntensity = Mathf.Max(0.0f, settings.sunIntensity);
        settings.ambientIntensity = Mathf.Max(0.0f, settings.ambientIntensity);
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        if (settings != null)
        {
            // Ensure internal factors match normalized values on load/enable
            settings.UpdateInternalScaleFactors();
        }
    }
#endif
}