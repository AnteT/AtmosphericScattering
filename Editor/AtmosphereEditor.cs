#if UNITY_EDITOR // Ensure this whole script is only compiled in the editor

using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering; // Required for GraphicsSettings, RenderPipelineAsset
using UnityEngine.Rendering.Universal; // REQUIRED for URP types like UniversalRenderPipelineAsset, ScriptableRendererData

// Place this script in an 'Editor' folder within your package
[CustomEditor(typeof(PlanetAtmosphere))]
public class AtmosphereEditor : Editor
{
    private SerializedProperty _atmosphereProfileProp;

    // Keep a reference to the editor for the assigned profile asset
    private Editor _profileEditor = null;
    private AtmosphereProfile _currentProfile = null; // Track current profile

    // --- For Renderer Feature Check ---
    private static bool? _isFeatureAddedCache = null;
    private static RenderPipelineAsset _checkedPipelineAsset = null; // Track which pipeline asset was checked
    private static ScriptableRendererData _checkedRendererData = null; // Track which renderer data was checked
    private static int _lastCheckFrame = -1;

    private void OnEnable()
    {
        _atmosphereProfileProp = serializedObject.FindProperty("atmosphereProfile");

        _profileEditor = null; // Reset on enable
        if (_atmosphereProfileProp != null && _atmosphereProfileProp.propertyType == SerializedPropertyType.ObjectReference)
        {
             _currentProfile = _atmosphereProfileProp.objectReferenceValue as AtmosphereProfile;
             if(_currentProfile != null && _currentProfile.settings != null)
             {
                 // Only create editor if profile is initially valid
                 _profileEditor = Editor.CreateEditor(_currentProfile);
             }
             else
             {
                 _currentProfile = null;
             }
        }
        else if (_atmosphereProfileProp == null) {
            Debug.LogError($"AtmosphereEditor: Could not find SerializedProperty 'atmosphereProfile' on target object '{target.name}'. Check script field name.", target);
        }

        InvalidateFeatureCache();
    }

    private void OnDisable()
    {
        if (_profileEditor != null)
        {
            DestroyImmediate(_profileEditor);
            _profileEditor = null;
            _currentProfile = null;
        }
    }

     private void InvalidateFeatureCache() {
        _lastCheckFrame = -1;
        _isFeatureAddedCache = null;
        _checkedPipelineAsset = null;
        _checkedRendererData = null;
    }

    public override void OnInspectorGUI()
    {
        PlanetAtmosphere atmosphereComponent = (PlanetAtmosphere)target;
        if (atmosphereComponent == null) return;

        if (_atmosphereProfileProp == null) {
             EditorGUILayout.HelpBox("AtmosphereEditor Error: Could not find the 'atmosphereProfile' property. Check the script field name.", MessageType.Error);
             DrawDefaultInspector();
             return;
        }

        serializedObject.UpdateIfRequiredOrScript();

        // --- Section 1: Renderer Feature Check & Status (Moved Up) ---
        var activePipeline = GraphicsSettings.currentRenderPipeline;
        ScriptableRendererData activeRendererData = GetActiveRendererData(activePipeline);

        if (Time.frameCount != _lastCheckFrame || activePipeline != _checkedPipelineAsset || activeRendererData != _checkedRendererData)
        {
             _isFeatureAddedCache = CheckFeature(activePipeline, activeRendererData);
             _checkedPipelineAsset = activePipeline;
             _checkedRendererData = activeRendererData;
             _lastCheckFrame = Time.frameCount;
        }

        // --- Conditional URP Integration Status Display (Only if Feature Not Added) ---
        if (_isFeatureAddedCache != true)
        {
            EditorGUILayout.LabelField("URP Integration Status", EditorStyles.boldLabel);

            string rendererNameToDisplay = (activeRendererData != null ? activeRendererData.name : "Default/First");
            switch (_isFeatureAddedCache)
            {
                case false:
                     EditorGUILayout.HelpBox(
                        "REQUIRED: The 'AtmosphereFeature' is MISSING from the active URP Renderer.\n" + // Added "REQUIRED"
                        "Atmosphere effects will not render!\n\n" +
                        $"How to fix: Go to Project Settings > Graphics > URP Global Settings, select your active URP Asset, find the 'Renderer List', select the active Renderer Asset (e.g., '{rendererNameToDisplay}'), and add 'Atmosphere Feature' using the 'Add Renderer Feature' button.",
                        MessageType.Warning);
                    break;
                case null:
                     EditorGUILayout.HelpBox(
                        "Could not verify URP setup.\n" +
                        "Reason: No active Universal Render Pipeline Asset found in Project Settings > Graphics, or the URP Asset has no valid Renderer assigned in its list.",
                        MessageType.Info);
                     break;
            }

             if (GUILayout.Button("Open Graphics Settings"))
             {
                 SettingsService.OpenProjectSettings("Project/Graphics");
             }
             if (activePipeline is UniversalRenderPipelineAsset urpAsset)
             {
                if (GUILayout.Button("Ping Active URP Asset")) {
                    EditorGUIUtility.PingObject(urpAsset);
                }
                if (activeRendererData != null && GUILayout.Button($"Ping Active Renderer ({activeRendererData.name})"))
                {
                     EditorGUIUtility.PingObject(activeRendererData);
                }
             }
             EditorGUILayout.Space(10); // Add space after this section
             EditorGUILayout.LabelField("", GUI.skin.horizontalSlider); // Separator
             EditorGUILayout.Space(10); // Add space after separator
        } // --- End of conditional URP Status Section ---


        // --- Section 2: Atmosphere Profile Assignment & Editor ---
        // Only proceed if the feature check didn't fail (or if status is unknown)
        // You might want to disable the profile field if the feature is missing,
        // but for now, just showing the warning first is clearer.

        EditorGUILayout.LabelField("Atmosphere Configuration", EditorStyles.boldLabel); // Header for this section
        EditorGUILayout.PropertyField(_atmosphereProfileProp);
        AtmosphereProfile profileAsset = _atmosphereProfileProp.objectReferenceValue as AtmosphereProfile;

        // --- Warning 1 (Profile): Assignment / Validity ---
        bool isProfileValid = profileAsset != null && profileAsset.settings != null;
        if (!isProfileValid)
        {
            // Destroy editor if profile is invalid or unassigned
            if (_profileEditor != null)
            {
                DestroyImmediate(_profileEditor);
                _profileEditor = null;
                _currentProfile = null;
            }

            if (profileAsset == null) {
                // Show warning only if URP check didn't already show a critical error
                if (_isFeatureAddedCache == true) {
                     EditorGUILayout.HelpBox(
                        "REQUIRED: Assign an existing Atmosphere Profile asset. Create a new one by right-clicking in the Assets folder:\n\n" +
                        "Create > Atmosphere > Atmosphere Profile\n\n" +
                        "Then assign it to the Atmosphere Profile slot on this object", // Added "REQUIRED"
                        MessageType.Warning);
                } else {
                     EditorGUILayout.HelpBox(
                        "Assign an Atmosphere Profile asset (after fixing URP setup).",
                        MessageType.Info); // Less critical if URP isn't set up
                }

            } else { // profileAsset != null but settings == null
                 EditorGUILayout.HelpBox(
                    $"ERROR: The assigned Atmosphere Profile '{profileAsset.name}' seems invalid. Please check or recreate it.", // Added "ERROR"
                    MessageType.Error);
            }
        }
        else // --- Profile is Valid: Embed Editor ---
        {
            // Don't show embedded editor if URP Feature is missing
            if (_isFeatureAddedCache == true)
            {
                EditorGUILayout.Space(5); // Add a bit of space before embedded editor
                // Label moved inside valid & feature check
                EditorGUILayout.LabelField($"{profileAsset.name} Settings", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                bool profileChanged = _currentProfile != profileAsset;
                if (_profileEditor == null || profileChanged)
                {
                    if (_profileEditor != null) DestroyImmediate(_profileEditor);
                    // Create editor only if profile is still valid (safety check)
                    if (profileAsset != null && profileAsset.settings != null) {
                         _profileEditor = Editor.CreateEditor(profileAsset);
                         _currentProfile = profileAsset;
                    } else {
                        _profileEditor = null;
                        _currentProfile = null;
                    }
                }

                if (_profileEditor != null)
                {
                    // Keep embedded editor indented
                    // EditorGUI.indentLevel++;
                    EditorGUI.BeginChangeCheck();
                    _profileEditor.OnInspectorGUI();
                    if(EditorGUI.EndChangeCheck())
                    {
                        EditorUtility.SetDirty(profileAsset);
                        // atmosphereComponent.UpdateDerivedSettings(); // Optional immediate update
                    }
                    // EditorGUI.indentLevel--;
                     EditorGUILayout.LabelField("", GUI.skin.horizontalSlider); // Add separator after editor
                }
            } else {
                 // Optionally show a message indicating why the editor isn't shown
                 // EditorGUILayout.HelpBox("Profile settings hidden until URP Feature is added.", MessageType.Info);

                 // Destroy editor if URP feature was removed while profile was assigned
                 if (_profileEditor != null)
                 {
                    DestroyImmediate(_profileEditor);
                    _profileEditor = null;
                    _currentProfile = null;
                 }
            }
        }

        // Apply changes (Profile assignment, etc.)
        serializedObject.ApplyModifiedProperties();
    }


    // --- Helper Methods (GetActiveRendererData, CheckFeature) remain unchanged ---

    private ScriptableRendererData GetActiveRendererData(RenderPipelineAsset activePipeline) {
         if (activePipeline is UniversalRenderPipelineAsset urpAsset)
         {
            var rendererList = urpAsset.rendererDataList;
            if (rendererList != null && rendererList.Length > 0 && rendererList[0] != null) {
                 return rendererList[0];
            }
         }
         return null;
    }

    private bool? CheckFeature(RenderPipelineAsset activePipeline, ScriptableRendererData rendererData)
    {
        if (!(activePipeline is UniversalRenderPipelineAsset) || rendererData == null) {
             return null;
        }
        foreach (var feature in rendererData.rendererFeatures)
        {
            if (feature != null && feature is AtmosphereFeature)
            {
                return true; // Found it!
            }
        }
        return false; // Not found
    }
}

#endif // UNITY_EDITOR