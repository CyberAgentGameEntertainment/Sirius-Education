// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

using UnityEngine;
using UnityEngine.Rendering;

namespace Sirius.PostProcessing.Runtime.Scripts.Volumes
{
    [VolumeComponentMenu("Sirius/SSPR")]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = "Sirius SSPR")]
#endif
    public class SSPRVolume : VolumeComponent, IPostProcessComponent
    {
#if !UNITY_6000_3_OR_NEWER
        public SSPRVolume()
        {
            displayName = "Sirius SSPR";
        }
#endif

        public enum Quality
        {
            Low,
            High
        }
        public BoolParameter Enable = new(false);
        public FloatParameter HorizontalReflectionPlaneHeightWS = new(0.0f);
        public ClampedIntParameter ReflectionTextureSize = new(512, 128, 1024);
        public BoolParameter GenerateMipMap = new(false);
        public ClampedFloatParameter BlurIntensity = new(0.5f, 0.0f, 1f);
        public ClampedIntParameter BlurTextureMipCount = new(4, 1, 6);
        public ClampedFloatParameter BlurWidth = new(1f, 0.5f, 2f);
        public ClampedFloatParameter FadeOutScreenBorderWidthVertical = new(0.25f, 0.01f, 1f);
        public ClampedFloatParameter FadeOutScreenBorderWidthHorizontal = new(0.35f, 0.01f, 1f);
        public ClampedFloatParameter ScreenLRStretchIntensity = new(4, 0, 8f);
        public ClampedFloatParameter ScreenLRStretchThreshold = new(0.7f, -1f, 1f);
        public ColorParameter TintColor = new ColorParameter(Color.white, true, true, true);
        [Header("高度な設定")]

        [Tooltip("SSPRの品質レベル")]  
        public EnumParameter<Quality> QualityLevel = new(Quality.High);
#if HAS_URP
        public EnumParameter<UnityEngine.Rendering.Universal.RenderPassEvent> CreateReflectionTextureTiming = new(UnityEngine.Rendering.Universal.RenderPassEvent.AfterRenderingTransparents); 
#endif
        public ClampedFloatParameter HorizontalReflectionPlaneHeightWSBias = new(0.001f, 0.001f, 0.01f);

        public bool IsActive()
        {
            return Enable.value;
        }

        public bool IsTileCompatible() => false;

    }
}
