// --------------------------------------------------------------
// Copyright 2026 CyberAgent, Inc.
// --------------------------------------------------------------

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Sirius.PostProcessing
{
    public enum SSRDitherMode
    {
        Dither8x8 = 0,
        InterleavedGradient,
    }

    public enum SSRDitherTarget
    {
        ReflectedUV = 0,
        OriginUV = 1,
    }

    [VolumeComponentMenu("Sirius/Screen Space Reflections")]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = "Sirius Screen Space Reflections")]
#endif
    public class SSRVolume : VolumeComponent, IPostProcessComponent
    {
#if !UNITY_6000_3_OR_NEWER
        public SSRVolume()
        {
            displayName = "Sirius Screen Space Reflections";
        }
#endif
        public BoolParameter enabled = new(false);
        public ClampedFloatParameter minSmoothness = new(0.5f, 0f, 1f);
        public BoolParameter downSample = new(false);
        public MinIntParameter maxSteps = new(64, 8);
        public ClampedIntParameter hiZTraceStartLevel = new(0, 0, 10);
        public ClampedIntParameter hiZTraceMaxLevel = new(10, 0, 10);
        public BoolParameter reflectSky = new(false);
        [FormerlySerializedAs("ditherType")]
        public EnumParameter<SSRDitherMode> ditherMode = new(SSRDitherMode.Dither8x8);
        public EnumParameter<SSRDitherTarget> ditherTarget = new(SSRDitherTarget.ReflectedUV);
#if HAS_URP
        public EnumParameter<UnityEngine.Rendering.Universal.RenderPassEvent> renderPassEvent = new(UnityEngine.Rendering.Universal.RenderPassEvent.BeforeRenderingTransparents);
#endif

        // デバッグ
        public BoolParameter debugMode = new(false);
        public ColorParameter debugDiffuseColor = new(Color.gray);
        public FloatParameter debugSmoothness = new(0.5f);
        public FloatParameter debugMetallic = new(0.5f);
        public FloatParameter debugAO = new(1f);

        public bool IsActive()
        {
            return enabled.value;
        }

        public bool IsTileCompatible() => false;
    }
}
