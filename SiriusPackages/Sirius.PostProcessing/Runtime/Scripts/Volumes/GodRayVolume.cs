// --------------------------------------------------------------
// Copyright 2023 CyberAgent, Inc.
// --------------------------------------------------------------

using UnityEngine;
using UnityEngine.Rendering;

namespace Sirius.PostProcessing.Runtime.Scripts.Volumes
{
    /// <summary>
    ///     GodRayの合成モード
    /// </summary>
    public enum GodRayBlendMode
    {
        /// <summary>現在のシーンカラーにGodRayを乗算加算する (デフォルト)</summary>
        Multiply,
        /// <summary>GodRayをシーンカラーに加算する</summary>
        Additive,
        /// <summary>スクリーンブレンドで合成する</summary>
        Screen,
    }

    /// <summary>
    ///     GodRayのパラメーターを調整するコントローラー
    /// </summary>
    [VolumeComponentMenu("Sirius/GodRay")]
#if UNITY_6000_3_OR_NEWER
    [DisplayInfo(name = "Sirius GodRay")]
#endif
    public class GodRayVolume : VolumeComponent, IPostProcessComponent
    {
#if !UNITY_6000_3_OR_NEWER
        public GodRayVolume()
        {
            displayName = "Sirius GodRay";
        }
#endif
        public BoolParameter Enable = new(false);
        
        // ゴッドレイパスの実行タイミング
        public EnumParameter<PostprocessTiming> ProcessTiming = new(PostprocessTiming.BeforePostprocess);

        /// サンプル数
        /// Note:現在非推奨：ポアソンディスクサンプリングで最適化しているので
        public ClampedIntParameter SampleCount = new(8, 4, GodRayHelper.RadialBlurMaxSample);

        /// RadialBlurの強さ
        public FloatParameter Strength = new ClampedFloatParameter(0.0f, 0.0f, 3.0f);

        /// 2値化の閾値
        public FloatParameter Threshold = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        /// 太陽方向への光芒の元の集中度（Henyey-Greenstein の g）
        [Tooltip("太陽に近い空ほど強く光芒の元にする度合い。0 で空全体を均一に扱う")]
        public ClampedFloatParameter SunFalloff = new(0.5f, 0.0f, 0.95f);

        /// ゴッドレイの強度
        public FloatParameter Intensity = new ClampedFloatParameter(0.0f, 0.0f, 10f);

        /// MainLightの方向を参照するか
        public BoolParameter ReferMainLightDirection = new(true);

        /// RadialBlurの中央 (MainLightの方向を参照しない、またはMainLight存在しない場合にこれを使う)
        [Tooltip("MainLightの方向を参照しない、またはMainLight存在しない場合にこっちを使う")]
        public Vector2Parameter RadialBlurCenter = new(Vector2.zero);

        /// ゴッドレイの色
        public ColorParameter GodRayColor = new(Color.white);

        /// ゴッドレイの色にMainLightの色味を掛けるか
        [Tooltip("ゴッドレイの色に MainLight の色味（明るさは除く）を掛ける")]
        public BoolParameter UseMainLightColor = new(false);

        /// 合成モード
        public EnumParameter<GodRayBlendMode> BlendMode = new(GodRayBlendMode.Multiply);

        public bool IsActive()
        {
            return Enable.value && Strength.value != 0 && Intensity.value != 0;
        }

        public bool IsTileCompatible()
        {
            return false;
        }
    }
}
