using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;
#if UNITY_EDITOR
//using UnityEditor; // Not strictly needed if OnValidate is removed
#endif

public class AtmosphereFeature : ScriptableRendererFeature
{
    private const string SHADER_PATH = "Hidden/Atmosphere";
    private const string MESH_RESOURCE_PATH = "AtmosphereMesh";

    [System.Serializable]
    public class FeatureSettings
    {
        [Tooltip("Render Pass Event for both atmosphere passes.")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    public FeatureSettings featureSettings = new FeatureSettings();

    private Mesh _loadedAtmosphereMesh;
    private AtmospherePass m_SkyPass;
    private AtmospherePass m_InternalHazePass;
    private Material _runtimeMaterialInstance;

    public static IReadOnlyList<PlanetAtmosphere> ActiveAtmospheres => PlanetAtmosphere.ActiveAtmospheres;

    static class ShaderIDs
    {
        public static readonly int PlanetWorldPosition = Shader.PropertyToID("_PlanetWorldPosition");
        public static readonly int PlanetRadius = Shader.PropertyToID("_PlanetRadius");
        public static readonly int AtmosphereRadius = Shader.PropertyToID("_AtmosphereRadius");
        public static readonly int RayleighScaleHeight = Shader.PropertyToID("_RayleighScaleHeight");
        public static readonly int MieScaleHeight = Shader.PropertyToID("_MieScaleHeight");
        public static readonly int DensityScale = Shader.PropertyToID("_DensityScale");
        public static readonly int RayleighScatteringCoeff = Shader.PropertyToID("_RayleighScatteringCoeff");
        public static readonly int MieScatteringCoeff = Shader.PropertyToID("_MieScatteringCoeff");
        public static readonly int MieG = Shader.PropertyToID("_MieG");
        public static readonly int OzoneAbsorptionCoeff = Shader.PropertyToID("_OzoneAbsorptionCoeff");
        public static readonly int OzoneCenterAltitudeNorm = Shader.PropertyToID("_OzoneCenterAltitudeNorm");
        public static readonly int OzoneWidth = Shader.PropertyToID("_OzoneWidth");
        public static readonly int SunIntensity = Shader.PropertyToID("_SunIntensity");
        public static readonly int SunDirection = Shader.PropertyToID("_SunDirection");
        public static readonly int AtmosphereTint = Shader.PropertyToID("_AtmosphereTint");
        public static readonly int ShaderPassIndex = Shader.PropertyToID("_ShaderPassIndex");
        public static readonly int DensityEdgeSmoothness = Shader.PropertyToID("_DensityEdgeSmoothness");
        public static readonly int AmbientIntensity = Shader.PropertyToID("_AmbientIntensity");
    }

    class AtmospherePass : ScriptableRenderPass
    {
        private Material _runtimeMaterial;
        private MaterialPropertyBlock _propertyBlock;
        private Mesh _sphereMesh;
        private const float MIN_SCALE_HEIGHT = 1e-5f;
        private string _profilerTag;
        private int _shaderPassIndex;

        private class PassData {
            internal Material runtimeMaterial;
            internal Mesh sphereMesh;
            internal MaterialPropertyBlock propertyBlock;
            internal Vector3 sunDirection;
            internal TextureHandle colorAttachment;
            internal TextureHandle depthAttachment;
            internal int shaderPassIndex;
        }
        public AtmospherePass(Material materialInstance, Mesh sphereMeshInstance, int shaderPassIndex, string profilerTag) { this._runtimeMaterial = materialInstance; this._sphereMesh = sphereMeshInstance; this._shaderPassIndex = shaderPassIndex; this.profilingSampler = new ProfilingSampler(profilerTag); this._profilerTag = profilerTag; if (materialInstance == null) Debug.LogError($"AtmospherePass ({profilerTag}): Runtime material is null during construction."); if (sphereMeshInstance == null) Debug.LogError($"AtmospherePass ({profilerTag}): Sphere mesh is null during construction."); _propertyBlock = new MaterialPropertyBlock(); }

        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            if (data.runtimeMaterial == null || data.sphereMesh == null || ActiveAtmospheres.Count == 0) return;
            var cmd = context.cmd;
            cmd.SetGlobalVector(ShaderIDs.SunDirection, data.sunDirection);
            const float baseMeshRadius = 0.5f;

            foreach (var atmosphere in ActiveAtmospheres)
            {
                if (atmosphere == null || !atmosphere.isActiveAndEnabled || atmosphere.currentSettings == null) continue;
                #if UNITY_EDITOR
                if (!Application.isPlaying) { atmosphere.UpdateDerivedSettings(); }
                #endif
                if (atmosphere.currentSettings == null) continue;

                Transform planetTransform = atmosphere.transform;
                var settings = atmosphere.currentSettings;
                data.propertyBlock.Clear();

                float scaledAtmosphereHeight = Mathf.Max(0.0f, atmosphere.scaledAtmosphereRadius - atmosphere.scaledPlanetRadius);
                float scaledRayleighH = Mathf.Max(MIN_SCALE_HEIGHT, settings.rayleighScaleHeightNorm * scaledAtmosphereHeight);
                float scaledMieH = Mathf.Max(MIN_SCALE_HEIGHT, settings.mieScaleHeightNorm * scaledAtmosphereHeight);

                data.propertyBlock.SetVector(ShaderIDs.PlanetWorldPosition, planetTransform.position);
                data.propertyBlock.SetFloat(ShaderIDs.PlanetRadius, atmosphere.scaledPlanetRadius);
                data.propertyBlock.SetFloat(ShaderIDs.AtmosphereRadius, atmosphere.scaledAtmosphereRadius);
                data.propertyBlock.SetFloat(ShaderIDs.RayleighScaleHeight, scaledRayleighH);
                data.propertyBlock.SetFloat(ShaderIDs.MieScaleHeight, scaledMieH);
                data.propertyBlock.SetFloat(ShaderIDs.DensityScale, settings.densityScale);
                data.propertyBlock.SetColor(ShaderIDs.RayleighScatteringCoeff, settings.rayleighScatteringCoeff * settings.rayleighScaleFactorInternal);
                data.propertyBlock.SetColor(ShaderIDs.MieScatteringCoeff, settings.mieScatteringCoeff * settings.mieScaleFactorInternal);
                data.propertyBlock.SetColor(ShaderIDs.OzoneAbsorptionCoeff, settings.ozoneAbsorptionCoeff * settings.ozoneScaleFactorInternal);
                data.propertyBlock.SetFloat(ShaderIDs.MieG, settings.mieG);
                data.propertyBlock.SetFloat(ShaderIDs.OzoneCenterAltitudeNorm, settings.ozoneCenterAltitudeNorm);
                data.propertyBlock.SetFloat(ShaderIDs.OzoneWidth, settings.ozoneWidth);
                data.propertyBlock.SetFloat(ShaderIDs.SunIntensity, settings.sunIntensity);
                data.propertyBlock.SetColor(ShaderIDs.AtmosphereTint, settings.atmosphereTint);
                data.propertyBlock.SetInteger(ShaderIDs.ShaderPassIndex, data.shaderPassIndex);
                data.propertyBlock.SetFloat(ShaderIDs.DensityEdgeSmoothness, settings.densityEdgeSmoothness);
                data.propertyBlock.SetFloat(ShaderIDs.AmbientIntensity, settings.ambientIntensity);

                float scaledVisualRadius = atmosphere.GetScaledVisualAtmosphereRadius();
                if (scaledVisualRadius <= atmosphere.scaledPlanetRadius + 1e-5f) 
                    continue;
                float requiredScale = scaledVisualRadius / baseMeshRadius;
                if (requiredScale <= 0f) 
                    continue;
                Matrix4x4 finalMatrix = Matrix4x4.TRS(planetTransform.position, planetTransform.rotation, Vector3.one * requiredScale);
                cmd.DrawMesh(data.sphereMesh, finalMatrix, data.runtimeMaterial, 0, data.shaderPassIndex, data.propertyBlock);
            }
        }
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) { 
            if (_runtimeMaterial == null || _sphereMesh == null || ActiveAtmospheres.Count == 0)
                return;
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            if (cameraData.isPreviewCamera || (cameraData.renderType != CameraRenderType.Base && cameraData.renderType != CameraRenderType.Overlay))
                return;
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(_profilerTag, out var passData)) {
                builder.AllowGlobalStateModification(true);
                passData.runtimeMaterial = _runtimeMaterial;
                passData.sphereMesh = _sphereMesh;
                passData.propertyBlock = _propertyBlock;
                passData.shaderPassIndex = _shaderPassIndex;
                int mainLightIndex = lightData.mainLightIndex;
                Vector3 sunDir = Vector3.down;
                if (mainLightIndex != -1 && mainLightIndex < lightData.visibleLights.Length) {
                    sunDir = -lightData.visibleLights[mainLightIndex].localToWorldMatrix.GetColumn(2);
                } else { Light sun = RenderSettings.sun; if (sun != null && sun.type == LightType.Directional) sunDir = -sun.transform.forward; }
                 passData.sunDirection = sunDir.normalized;
                passData.colorAttachment = resourceData.activeColorTexture;
                passData.depthAttachment = resourceData.activeDepthTexture;
                builder.SetRenderAttachment(passData.colorAttachment, 0);
                builder.SetRenderAttachmentDepth(passData.depthAttachment, AccessFlags.Read);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }
         }
    } // End AtmospherePass Class

    public override void Create() { 
        DisposeInstances();
        _loadedAtmosphereMesh = Resources.Load<Mesh>(MESH_RESOURCE_PATH);
        if (_loadedAtmosphereMesh == null) { Debug.LogError($"Atmosphere Feature: Failed to load mesh from Resources at path '{MESH_RESOURCE_PATH}'. Atmosphere will not render."); DisposeInstances(); return; }
        Shader atmosphereShader = Shader.Find(SHADER_PATH);
        if (atmosphereShader == null) { Debug.LogError($"AtmosphereFeature: Failed to find shader '{SHADER_PATH}'."); DisposeInstances(); return; }
        _runtimeMaterialInstance = CoreUtils.CreateEngineMaterial(atmosphereShader);
        if (_runtimeMaterialInstance == null) { Debug.LogError($"Atmosphere Feature: Failed to create runtime material instance from shader '{SHADER_PATH}'."); DisposeInstances(); return; }
        m_SkyPass = new AtmospherePass(_runtimeMaterialInstance, _loadedAtmosphereMesh, 0, "Atmosphere Sky");
        m_InternalHazePass = new AtmospherePass(_runtimeMaterialInstance, _loadedAtmosphereMesh, 1, "Atmosphere Haze (Internal)");
        m_SkyPass.renderPassEvent = featureSettings.renderPassEvent;
        m_InternalHazePass.renderPassEvent = featureSettings.renderPassEvent;
    }
    private void DisposeInstances() { 
        CoreUtils.Destroy(_runtimeMaterialInstance);
        _runtimeMaterialInstance = null;
        m_SkyPass = null;
        m_InternalHazePass = null;
        _loadedAtmosphereMesh = null;
    }
    protected override void Dispose(bool disposing) { 
        DisposeInstances(); base.Dispose(disposing); 
    }
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) { 
        if (_runtimeMaterialInstance == null || _loadedAtmosphereMesh == null || ActiveAtmospheres.Count == 0) 
            return;
        if (m_SkyPass != null) 
            renderer.EnqueuePass(m_SkyPass); 
        else { 
            Debug.LogError("Atmosphere Feature: Sky Pass is null."); 
            return; 
        }
        bool cameraIsInsideAnyAtmosphere = false;
        Vector3 cameraPos = renderingData.cameraData.worldSpaceCameraPos;
        foreach (var atmosphere in ActiveAtmospheres) { 
            if (atmosphere != null && atmosphere.isActiveAndEnabled && atmosphere.currentSettings != null) { 
                float distSq = (cameraPos - atmosphere.transform.position).sqrMagnitude;
                float atmosphereRadiusSq = atmosphere.scaledAtmosphereRadius * atmosphere.scaledAtmosphereRadius;
                if (distSq < atmosphereRadiusSq) { 
                    cameraIsInsideAnyAtmosphere = true; 
                    break; 
                } 
            } 
        }
        if (cameraIsInsideAnyAtmosphere) { 
            if (m_InternalHazePass != null) 
                renderer.EnqueuePass(m_InternalHazePass); 
            else 
                Debug.LogError("Atmosphere Feature: Internal Haze Pass is null."); 
        }
     }
}