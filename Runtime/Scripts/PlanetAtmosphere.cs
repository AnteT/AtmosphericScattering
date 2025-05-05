using UnityEngine;
using System.Collections.Generic;

[ExecuteAlways]
[AddComponentMenu("Rendering/Planet Atmosphere")]
public class PlanetAtmosphere : MonoBehaviour
{
    [Tooltip("Assign an Atmosphere Profile asset containing the desired settings.")]
    public AtmosphereProfile atmosphereProfile;

    // Internal caches - simplified
    internal AtmosphereProfile.AtmosphereSettings currentSettings;
    internal float scaledPlanetRadius;
    internal float scaledAtmosphereRadius; // Physical outer radius, scaled

    // State for update checks
    private Vector3 _lastLossyScale = Vector3.one * -1f;

    // Static list
    private static List<PlanetAtmosphere> activeAtmospheres = new List<PlanetAtmosphere>();
    public static IReadOnlyList<PlanetAtmosphere> ActiveAtmospheres => activeAtmospheres;

    // Calculates the scaled radius intended for the visual mesh
    public float GetScaledVisualAtmosphereRadius()
    {
        float avgWorldScale = GetAverageScale();
        if (currentSettings == null) {
             if (atmosphereProfile != null) UpdateDerivedSettings();
             if (currentSettings == null) return 0f;
        }

        float unscaledPhysicalOuterRadius = currentSettings.planetRadius + Mathf.Max(0f, currentSettings.atmosphereHeight);
        unscaledPhysicalOuterRadius = Mathf.Max(currentSettings.planetRadius, unscaledPhysicalOuterRadius); // Ensure not smaller than planet
        return unscaledPhysicalOuterRadius * avgWorldScale;
    }

    private float GetAverageScale()
    {
         Vector3 currentLossyScale = transform.lossyScale;
         float avgWorldScale = (Mathf.Abs(currentLossyScale.x) + Mathf.Abs(currentLossyScale.y) + Mathf.Abs(currentLossyScale.z)) / 3.0f;
         return Mathf.Max(0f, avgWorldScale);
    }

    // Update function now primarily focuses on scale and validity checks
    public bool UpdateDerivedSettings()
    {
        bool profileChanged = true; // Increase responsiveness during dev
        Vector3 currentLossyScale = transform.lossyScale;
        bool scaleChanged = (currentLossyScale - _lastLossyScale).sqrMagnitude > 1e-6f;
        bool hadValidSettingsBefore = (currentSettings != null);

        // --- Validity Check ---
        if (atmosphereProfile == null || atmosphereProfile.settings == null) {
             if (hadValidSettingsBefore && activeAtmospheres.Contains(this)) activeAtmospheres.Remove(this);
             currentSettings = null;
             _lastLossyScale = currentLossyScale;
             scaledPlanetRadius = 0f; scaledAtmosphereRadius = 0f;
             // Optionally log error if profile exists but settings are null
             if (atmosphereProfile != null && atmosphereProfile.settings == null) {
                 Debug.LogError($"Planet Atmosphere on {gameObject.name}: Profile '{atmosphereProfile.name}' has null settings.", atmosphereProfile);
             }
             return false;
         }

        currentSettings = atmosphereProfile.settings;
        if (profileChanged || scaleChanged || !hadValidSettingsBefore)
        {
            var settings = currentSettings;
            float avgWorldScale = GetAverageScale();

            // Validate (redundant but safe)
            settings.planetRadius = Mathf.Max(0.1f, settings.planetRadius);
            settings.atmosphereHeight = Mathf.Max(0.0f, settings.atmosphereHeight);

            scaledPlanetRadius = settings.planetRadius * avgWorldScale;
            scaledAtmosphereRadius = scaledPlanetRadius + (settings.atmosphereHeight * avgWorldScale);

            _lastLossyScale = currentLossyScale;
        }

        // --- Registration logic ---
        // Add if enabled and not present, remove if disabled and present
        if (this.enabled && !activeAtmospheres.Contains(this)) {
             activeAtmospheres.Add(this);
        } else if (!this.enabled && activeAtmospheres.Contains(this)) {
             activeAtmospheres.Remove(this);
        }

        return true; // Indicate success (profile is valid)
    }

    private void OnEnable() {
        _lastLossyScale = Vector3.one * -999f; // Force update
        UpdateDerivedSettings();
    }
    private void OnDisable() {
        activeAtmospheres.Remove(this);
        currentSettings = null; _lastLossyScale = Vector3.one * -1f;
    }
    private void OnValidate() {
         bool isValidNow = UpdateDerivedSettings();
         // Synchronize registration with enabled state after validation potentially changed validity
         if(this.enabled) {
             if (isValidNow && !activeAtmospheres.Contains(this)) activeAtmospheres.Add(this);
             else if (!isValidNow && activeAtmospheres.Contains(this)) activeAtmospheres.Remove(this);
         } else {
              if (activeAtmospheres.Contains(this)) activeAtmospheres.Remove(this);
         }
    }
    private void OnDrawGizmosSelected() {
        UpdateDerivedSettings(); // Ensure latest data for gizmos
        if (currentSettings == null) return;
        Color gizmoColor = currentSettings.atmosphereTint; gizmoColor.a = 0.3f;
        Color innerColor = Color.white * 0.5f; innerColor.a = 0.3f;
        Color physOuterColor = Color.cyan * 0.7f; physOuterColor.a = 0.3f;
        Vector3 currentPosition = transform.position;

        if (scaledPlanetRadius > 0) { Gizmos.color = innerColor; Gizmos.DrawWireSphere(currentPosition, scaledPlanetRadius); }
        if (scaledAtmosphereRadius > scaledPlanetRadius) { Gizmos.color = physOuterColor; Gizmos.DrawWireSphere(currentPosition, scaledAtmosphereRadius); }

        float scaledVisualRadius = GetScaledVisualAtmosphereRadius();
        // Only draw visual gizmo if different from physical outer radius
        if (scaledVisualRadius > 0 && Mathf.Abs(scaledVisualRadius - scaledAtmosphereRadius) > 0.01f * GetAverageScale()) {
            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(currentPosition, scaledVisualRadius);
        }
    }

    #if UNITY_EDITOR
    void Update() {
        if (Application.isPlaying) return;
        // In editor, frequently check for updates in case profile is modified externally or scale changes
        UpdateDerivedSettings();
    }
    #endif
}