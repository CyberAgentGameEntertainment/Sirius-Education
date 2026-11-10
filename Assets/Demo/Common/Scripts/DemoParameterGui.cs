using System.Collections.Generic;
using Sirius.PostProcessing;
using Sirius.PostProcessing.Runtime.Scripts.Volumes;
using UnityEngine;
using UnityEngine.Rendering;

namespace Demo.Scripts
{
    /// <summary>
    ///     デモシーンのVolumeとライトのパラメーターを実行中に変更するGUI
    /// </summary>
    /// <remarks>
    ///     Volumeのプロファイルのインスタンスを書き換えるため、アセットは変更されない
    /// </remarks>
    public class DemoParameterGui : MonoBehaviour
    {
        // この高さを基準にレイアウトし、画面の高さに合わせて拡大する
        private const float ReferenceHeight = 840f;
        private const float PanelWidth = 300f;
        private const float Margin = 16f;
        private const float Padding = 16f;
        private const float ContentWidth = PanelWidth - Padding * 2f;
        private const float HeaderHeight = 106f;
        private const float FooterHeight = 100f;
        private const float CornerRadius = 14f;
        private const float AutoRotateSpeed = 12f;
        private const KeyCode ToggleKey = KeyCode.H;
        private const string DefaultHint = "項目にカーソルを合わせると説明を表示します";

        private static readonly Color PanelColor = new(0.06f, 0.07f, 0.09f, 0.88f);
        private static readonly Color OutlineColor = new(1f, 1f, 1f, 0.08f);
        private static readonly Color SurfaceColor = new(1f, 1f, 1f, 0.05f);
        private static readonly Color SurfaceHoverColor = new(1f, 1f, 1f, 0.10f);
        private static readonly Color TrackColor = new(1f, 1f, 1f, 0.14f);
        private static readonly Color TextColor = new(0.93f, 0.94f, 0.96f);
        private static readonly Color SubTextColor = new(0.58f, 0.61f, 0.67f);
        private static readonly Color InkColor = new(0.07f, 0.08f, 0.10f);

        private static readonly Color GodRayAccent = new(1f, 0.71f, 0.33f);
        private static readonly Color SsprAccent = new(0.37f, 0.84f, 1f);
        private static readonly Color SsrAccent = new(0.71f, 0.58f, 1f);

        private static readonly Color[] SunColors =
        {
            new(1f, 1f, 1f),
            new(1f, 0.95f, 0.84f),
            new(1f, 0.80f, 0.50f),
            new(1f, 0.55f, 0.28f),
            new(0.62f, 0.75f, 1f),
        };

        private static readonly string[] BlendModeLabels = { "乗算加算", "加算", "スクリーン" };
        private static readonly string[] QualityLabels = { "Low", "High" };
        private static readonly string[] ResolutionLabels = { "128", "256", "512", "1024" };
        private static readonly string[] DitherLabels = { "Bayer 8x8", "IGN" };

        private GodRayVolume _godRay;
        private SSPRVolume _sspr;
        private SSRVolume _ssr;
        private Light _sun;
        private float _sunPitch;
        private float _sunYaw;
        private bool _autoRotateSun;

        private string _title;
        private string _subtitle;
        private Color _accent;

        private readonly List<(VolumeParameter Target, VolumeParameter Initial)> _initialParameters = new();
        private Quaternion _initialSunRotation;
        private float _initialSunIntensity;
        private Color _initialSunColor;

        private bool _collapsed;
        private Vector2 _scroll;
        // 前回のイベントで測ったコンテンツの高さ。手動レイアウトのためパネルの高さはこれから決める
        private float _contentHeight = 400f;
        private float _cursorY;
        private bool _viewHovered;
        private string _hint;

        private GUIStyle _eyebrowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _strongLabelStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _onStyle;
        private GUIStyle _offStyle;
        private GUIStyle _segmentStyle;
        private GUIStyle _segmentActiveStyle;
        private GUIStyle _hintStyle;
        private GUIStyle _shortcutStyle;
        private GUIStyle _buttonStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            new GameObject(nameof(DemoParameterGui)).AddComponent<DemoParameterGui>();
        }

        private void Start()
        {
            foreach (var volume in FindObjectsByType<Volume>(FindObjectsSortMode.None))
            {
                var profile = volume.profile;
                if (profile == null)
                {
                    continue;
                }

                profile.TryGet(out GodRayVolume godRay);
                _godRay ??= godRay;
                profile.TryGet(out SSPRVolume sspr);
                _sspr ??= sspr;
                profile.TryGet(out SSRVolume ssr);
                _ssr ??= ssr;
            }

            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                {
                    _sun = light;
                    break;
                }
            }
            if (_sun != null)
            {
                _initialSunRotation = _sun.transform.rotation;
                _initialSunIntensity = _sun.intensity;
                _initialSunColor = _sun.color;
                ReadSunAngles();
            }

            if (_godRay != null)
            {
                _title = "God Ray";
                _subtitle = "明るい空を太陽方向へ放射状にぼかし、光の筋を描く";
                _accent = GodRayAccent;
                Expose(_godRay, _godRay.Enable, _godRay.BlendMode, _godRay.Intensity, _godRay.Strength,
                    _godRay.SunFalloff, _godRay.Threshold, _godRay.UseMainLightColor);
            }
            else if (_sspr != null)
            {
                _title = "SSPR";
                _subtitle = "水平面を基準に画面を折り返し、平面反射を描く";
                _accent = SsprAccent;
                Expose(_sspr, _sspr.Enable, _sspr.QualityLevel, _sspr.ReflectionTextureSize,
                    _sspr.HorizontalReflectionPlaneHeightWS, _sspr.BlurIntensity, _sspr.BlurWidth,
                    _sspr.ScreenLRStretchIntensity, _sspr.FadeOutScreenBorderWidthVertical,
                    _sspr.FadeOutScreenBorderWidthHorizontal);
            }
            else if (_ssr != null)
            {
                _title = "SSR";
                _subtitle = "深度バッファ上をレイマーチし、画面内の映り込みを描く";
                _accent = SsrAccent;
                Expose(_ssr, _ssr.enabled, _ssr.maxSteps, _ssr.hiZTraceMaxLevel, _ssr.ditherMode, _ssr.downSample,
                    _ssr.minSmoothness, _ssr.reflectSky, _ssr.debugMode, _ssr.debugSmoothness, _ssr.debugMetallic);
            }
            else
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (_sun == null || !_autoRotateSun)
            {
                return;
            }

            _sunYaw = Mathf.Repeat(_sunYaw + AutoRotateSpeed * Time.deltaTime + 180f, 360f) - 180f;
            ApplySunRotation();
        }

        private void Expose(VolumeComponent component, params VolumeParameter[] parameters)
        {
            foreach (var parameter in parameters)
            {
                parameter.overrideState = true;
            }
            foreach (var parameter in component.parameters)
            {
                _initialParameters.Add((parameter, (VolumeParameter)parameter.Clone()));
            }
        }

        private void ResetAll()
        {
            foreach (var (target, initial) in _initialParameters)
            {
                target.SetValue(initial);
            }

            if (_sun != null)
            {
                _autoRotateSun = false;
                _sun.transform.rotation = _initialSunRotation;
                _sun.intensity = _initialSunIntensity;
                _sun.color = _initialSunColor;
                ReadSunAngles();
            }
        }

        private void ReadSunAngles()
        {
            var euler = _sun.transform.eulerAngles;
            _sunPitch = Mathf.DeltaAngle(0f, euler.x);
            _sunYaw = Mathf.DeltaAngle(0f, euler.y);
        }

        private void ApplySunRotation()
        {
            _sun.transform.rotation = Quaternion.Euler(_sunPitch, _sunYaw, 0f);
        }

        #region Layout

        private void OnGUI()
        {
            EnsureStyles();

            var current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == ToggleKey)
            {
                _collapsed = !_collapsed;
                current.Use();
            }

            var scale = Mathf.Max(1f, Screen.height / ReferenceHeight);
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            if (_collapsed)
            {
                DrawCollapsed();
            }
            else
            {
                DrawPanel(Screen.height / scale - Margin * 2f);
            }

            GUI.matrix = previousMatrix;
        }

        private void DrawCollapsed()
        {
            var pill = new Rect(Margin, Margin, 184f, 40f);
            var hovered = pill.Contains(Event.current.mousePosition);
            DrawRect(pill, PanelColor, pill.height * 0.5f);
            DrawOutline(pill, hovered ? SurfaceHoverColor : OutlineColor, pill.height * 0.5f);
            DrawRect(new Rect(pill.x + 16f, pill.center.y - 4f, 8f, 8f), _accent, 4f);
            GUI.Label(new Rect(pill.x + 32f, pill.y, pill.width - 48f, pill.height), _title, _strongLabelStyle);
            GUI.Label(new Rect(pill.x, pill.y, pill.width - 16f, pill.height), $"{ToggleKey} で表示", _shortcutStyle);
            if (GUI.Button(pill, GUIContent.none, GUIStyle.none))
            {
                _collapsed = false;
            }
        }

        private void DrawPanel(float maxHeight)
        {
            var panelHeight = Mathf.Min(HeaderHeight + _contentHeight + FooterHeight, maxHeight);
            var panel = new Rect(Margin, Margin, PanelWidth, panelHeight);
            DrawRect(new Rect(panel.x - 4f, panel.y - 2f, panel.width + 8f, panel.height + 10f), new Color(0f, 0f, 0f, 0.18f), CornerRadius + 4f);
            DrawRect(panel, PanelColor, CornerRadius);
            DrawOutline(panel, OutlineColor, CornerRadius);

            DrawHeader(new Rect(panel.x + Padding, panel.y + Padding, ContentWidth, HeaderHeight - Padding));

            var view = new Rect(panel.x, panel.y + HeaderHeight, PanelWidth, panelHeight - HeaderHeight - FooterHeight);
            var current = Event.current;
            if (current.type == EventType.Repaint)
            {
                _hint = null;
                _viewHovered = view.Contains(current.mousePosition);
            }

            // スクロールバーは描かず、ホイールでスクロールさせて細いインジケーターだけ表示する
            var scrollDelta = 0f;
            if (current.type == EventType.ScrollWheel && view.Contains(current.mousePosition))
            {
                scrollDelta = current.delta.y * 20f;
                current.Use();
            }
            _scroll.y = Mathf.Clamp(_scroll.y + scrollDelta, 0f, Mathf.Max(0f, _contentHeight - view.height));
            _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0f, 0f, PanelWidth, _contentHeight), GUIStyle.none, GUIStyle.none);
            _cursorY = 0f;
            if (_godRay != null)
            {
                DrawGodRay(_godRay);
                if (_sun != null)
                {
                    DrawSun(_sun);
                }
            }
            else if (_sspr != null)
            {
                DrawSSPR(_sspr);
            }
            else if (_ssr != null)
            {
                DrawSSR(_ssr);
            }
            _contentHeight = _cursorY + 8f;
            GUI.EndScrollView();

            if (_contentHeight > view.height)
            {
                var ratio = view.height / _contentHeight;
                var barHeight = view.height * ratio;
                var barY = view.y + _scroll.y * ratio;
                DrawRect(new Rect(view.xMax - 6f, barY, 3f, barHeight), TrackColor, 1.5f);
            }

            DrawFooter(new Rect(panel.x + Padding, view.yMax, ContentWidth, FooterHeight - Padding));
        }

        private void DrawHeader(Rect rect)
        {
            DrawRect(new Rect(rect.x, rect.y + 4f, 6f, 6f), _accent, 3f);
            GUI.Label(new Rect(rect.x + 12f, rect.y, rect.width, 14f), "SIRIUS  POST PROCESS", _eyebrowStyle);
            GUI.Label(new Rect(rect.x, rect.y + 16f, rect.width, 28f), _title, _titleStyle);
            GUI.Label(new Rect(rect.x, rect.y + 46f, rect.width, 30f), _subtitle, _subtitleStyle);

            var close = new Rect(rect.xMax - 24f, rect.y - 4f, 24f, 24f);
            DrawRect(close, close.Contains(Event.current.mousePosition) ? SurfaceHoverColor : SurfaceColor, 12f);
            DrawRect(new Rect(close.center.x - 5f, close.center.y - 0.75f, 10f, 1.5f), SubTextColor, 0.75f);
            if (GUI.Button(close, GUIContent.none, GUIStyle.none))
            {
                _collapsed = true;
            }
        }

        private void DrawFooter(Rect rect)
        {
            DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), OutlineColor, 0f);
            GUI.Label(new Rect(rect.x, rect.y + 10f, rect.width, 44f), _hint ?? DefaultHint, _hintStyle);

            var reset = new Rect(rect.x, rect.yMax - 24f, 84f, 24f);
            DrawRect(reset, reset.Contains(Event.current.mousePosition) ? SurfaceHoverColor : SurfaceColor, 12f);
            GUI.Label(reset, "リセット", _buttonStyle);
            if (GUI.Button(reset, GUIContent.none, GUIStyle.none))
            {
                ResetAll();
            }
            GUI.Label(new Rect(rect.x, reset.y, rect.width, reset.height), $"{ToggleKey} キーで隠す", _shortcutStyle);
        }

        #endregion

        #region Parameters

        private void DrawGodRay(GodRayVolume godRay)
        {
            godRay.Enable.value = EffectSwitch(godRay.Enable.value);

            Section("光芒");
            godRay.BlendMode.value = (GodRayBlendMode)Segmented("合成モード",
                "乗算加算: シーン色に応じて足す / 加算: そのまま足す / スクリーン: 白飛びを抑えて合成",
                (int)godRay.BlendMode.value, BlendModeLabels);
            godRay.Intensity.value = Slider("強度", "光芒の明るさ",
                godRay.Intensity.value, 0f, 10f);
            godRay.Strength.value = Slider("光の筋の長さ", "放射状ブラーの長さ。大きいほど光の筋が太陽から遠くまで伸びる",
                godRay.Strength.value, 0f, 3f);
            godRay.SunFalloff.value = Slider("太陽への集中度", "太陽に近い空ほど光芒の元として強く扱う度合い。0 で空全体が均一",
                godRay.SunFalloff.value, 0f, 0.95f);
            godRay.Threshold.value = Slider("2値化の閾値", "光芒の元にする明るさの閾値。上げると明るい部分だけが光る",
                godRay.Threshold.value, 0f, 1f);
            godRay.UseMainLightColor.value = Switch("ライトの色を反映", "光芒にライトの色味を掛ける。下のライトの色と組み合わせて確認",
                godRay.UseMainLightColor.value);
        }

        private void DrawSun(Light sun)
        {
            Section("ライト");
            _sunPitch = Slider("仰角", "ライトの高さ。光芒は太陽の方向から放射状に伸びる", _sunPitch, -90f, 90f, "0°");
            _sunYaw = Slider("方位角", "ライトの向き。光芒の放射中心が太陽の方向に追従する", _sunYaw, -180f, 180f, "0°");
            ApplySunRotation();
            _autoRotateSun = Switch("方位角を自動回転", "方位角を回し続け、遮蔽物越しに光芒が変化する様子を観察する", _autoRotateSun);
            sun.intensity = Slider("強度", "ライトの明るさ", sun.intensity, 0.1f, 5f);
            sun.color = Swatches("色", "ライトの色。「ライトの色を反映」がオンなら光芒の色も変わる", sun.color, SunColors);
        }

        private void DrawSSPR(SSPRVolume sspr)
        {
            sspr.Enable.value = EffectSwitch(sspr.Enable.value);

            Section("反射テクスチャ");
            sspr.QualityLevel.value = (SSPRVolume.Quality)Segmented("品質",
                "High は反射テクスチャの書き込まれなかった画素（穴）を周囲から埋める。Low にすると穴が見える",
                (int)sspr.QualityLevel.value, QualityLabels);
            var resolutionIndex = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(sspr.ReflectionTextureSize.value / 128f, 2f)), 0, ResolutionLabels.Length - 1);
            resolutionIndex = Segmented("解像度", "反射テクスチャの解像度。下げるとジャギーやちらつきが目立つ",
                resolutionIndex, ResolutionLabels);
            sspr.ReflectionTextureSize.value = 128 << resolutionIndex;
            sspr.HorizontalReflectionPlaneHeightWS.value = Slider("反射平面の高さ",
                "折り返しの基準にする水平面のワールド高さ。床からずらすと反射もずれる",
                sspr.HorizontalReflectionPlaneHeightWS.value, -1f, 1f);

            Section("ぼかし");
            sspr.BlurIntensity.value = Slider("強さ", "0 で鏡面、上げるほど艶を抑えた床のような反射になる",
                sspr.BlurIntensity.value, 0f, 1f);
            sspr.BlurWidth.value = Slider("幅", "ぼかしのサンプル間隔。大きいほど広くにじむ",
                sspr.BlurWidth.value, 0.5f, 2f);

            Section("画面端の処理");
            sspr.ScreenLRStretchIntensity.value = Slider("左右の引き伸ばし",
                "画面の左右端で反射を引き伸ばし、画面外の情報が欠けた部分を目立ちにくくする",
                sspr.ScreenLRStretchIntensity.value, 0f, 8f);
            sspr.FadeOutScreenBorderWidthVertical.value = Slider("上下のフェード幅",
                "反射元が画面の上下端に近いほど反射を薄くする。0 に近づけると反射が途切れる境目が見える",
                sspr.FadeOutScreenBorderWidthVertical.value, 0.01f, 1f);
            sspr.FadeOutScreenBorderWidthHorizontal.value = Slider("左右のフェード幅",
                "反射元が画面の左右端に近いほど反射を薄くする",
                sspr.FadeOutScreenBorderWidthHorizontal.value, 0.01f, 1f);
        }

        private void DrawSSR(SSRVolume ssr)
        {
            ssr.enabled.value = EffectSwitch(ssr.enabled.value);

            Section("レイマーチ");
            ssr.maxSteps.value = IntSlider("最大ステップ数", "レイを進める最大回数。少ないと遠くの物が映らなくなる",
                ssr.maxSteps.value, 8, 128);
            ssr.hiZTraceMaxLevel.value = IntSlider("Hi-Z 最大レベル",
                "深度ミップで一度に飛ばせる距離の上限。0 だと 1 画素ずつ進むため、少ないステップでは近くしか映らない",
                ssr.hiZTraceMaxLevel.value, 0, 10);
            ssr.ditherMode.value = (SSRDitherMode)Segmented("ディザ", "レイの開始位置をずらすパターン。反射に乗るノイズの模様が変わる",
                (int)ssr.ditherMode.value, DitherLabels);
            ssr.downSample.value = Switch("ダウンサンプル", "半解像度で計算して負荷を下げる。反射がぼやける",
                ssr.downSample.value);

            Section("反射の対象");
            ssr.minSmoothness.value = Slider("最小スムースネス", "これより滑らかな画素だけ反射を計算する。下げると粗い面にも映り込む",
                ssr.minSmoothness.value, 0f, 1f);
            ssr.reflectSky.value = Switch("空を反射", "レイが何にも当たらなかったとき空を映す",
                ssr.reflectSky.value);

            Section("デバッグ");
            ssr.debugMode.value = Switch("全面を均一なマテリアルにする",
                "G-Buffer の代わりに下の値を全画素に使って SSR を計算する。反射の届く範囲を確かめやすい",
                ssr.debugMode.value);
            if (ssr.debugMode.value)
            {
                ssr.debugSmoothness.value = Slider("スムースネス", "全画素に使うスムースネス", ssr.debugSmoothness.value, 0f, 1f);
                ssr.debugMetallic.value = Slider("メタリック", "全画素に使うメタリック", ssr.debugMetallic.value, 0f, 1f);
            }
        }

        #endregion

        #region Widgets

        private Rect NextRow(float height)
        {
            var rect = new Rect(Padding, _cursorY, ContentWidth, height);
            _cursorY += height;
            return rect;
        }

        private void TrackHint(Rect rect, string hint)
        {
            if (Event.current.type == EventType.Repaint && _viewHovered && rect.Contains(Event.current.mousePosition))
            {
                _hint = hint;
            }
        }

        private bool EffectSwitch(bool value)
        {
            var card = NextRow(44f);
            _cursorY += 4f;
            TrackHint(card, "エフェクト全体のオン/オフ。切り替えて差分を確認する");
            DrawRect(card, card.Contains(Event.current.mousePosition) ? SurfaceHoverColor : SurfaceColor, 10f);
            GUI.Label(new Rect(card.x + 14f, card.y, card.width, card.height), "エフェクト", _strongLabelStyle);
            GUI.Label(new Rect(card.x, card.y, card.width - 60f, card.height), value ? "ON" : "OFF", value ? _onStyle : _offStyle);
            DrawSwitch(new Rect(card.xMax - 50f, card.center.y - 10f, 36f, 20f), value);
            return GUI.Button(card, GUIContent.none, GUIStyle.none) ? !value : value;
        }

        private void Section(string title)
        {
            _cursorY += 12f;
            var row = NextRow(20f);
            var titleWidth = _sectionStyle.CalcSize(new GUIContent(title)).x;
            GUI.Label(row, title, _sectionStyle);
            DrawRect(new Rect(row.x + titleWidth + 10f, row.center.y, row.width - titleWidth - 10f, 1f), OutlineColor, 0f);
        }

        private float Slider(string label, string hint, float value, float min, float max, string format = "0.00")
        {
            var row = NextRow(42f);
            TrackHint(row, hint);
            GUI.Label(new Rect(row.x, row.y, row.width, 20f), label, _labelStyle);
            GUI.Label(new Rect(row.x, row.y, row.width, 20f), value.ToString(format), _valueStyle);
            return SliderControl(new Rect(row.x, row.y + 20f, row.width, 18f), value, min, max);
        }

        private int IntSlider(string label, string hint, int value, int min, int max)
        {
            return Mathf.RoundToInt(Slider(label, hint, value, min, max, "0"));
        }

        private float SliderControl(Rect rect, float value, float min, float max)
        {
            var id = GUIUtility.GetControlID(FocusType.Passive);
            var current = Event.current;
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown when current.button == 0 && rect.Contains(current.mousePosition):
                    GUIUtility.hotControl = id;
                    value = Mathf.Lerp(min, max, Mathf.InverseLerp(rect.xMin, rect.xMax, current.mousePosition.x));
                    current.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    value = Mathf.Lerp(min, max, Mathf.InverseLerp(rect.xMin, rect.xMax, current.mousePosition.x));
                    current.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    current.Use();
                    break;
                case EventType.Repaint:
                    var t = Mathf.InverseLerp(min, max, value);
                    var trackY = rect.center.y - 2f;
                    DrawRect(new Rect(rect.x, trackY, rect.width, 4f), TrackColor, 2f);
                    DrawRect(new Rect(rect.x, trackY, rect.width * t, 4f), _accent, 2f);

                    var thumbCenter = new Vector2(rect.x + rect.width * t, rect.center.y);
                    if (GUIUtility.hotControl == id || rect.Contains(current.mousePosition))
                    {
                        DrawCircle(thumbCenter, 11f, new Color(_accent.r, _accent.g, _accent.b, 0.25f));
                    }
                    DrawCircle(thumbCenter, 6.5f, TextColor);
                    break;
            }
            return value;
        }

        private bool Switch(string label, string hint, bool value)
        {
            var row = NextRow(32f);
            TrackHint(row, hint);
            GUI.Label(row, label, _labelStyle);
            DrawSwitch(new Rect(row.xMax - 36f, row.center.y - 10f, 36f, 20f), value);
            return GUI.Button(row, GUIContent.none, GUIStyle.none) ? !value : value;
        }

        private void DrawSwitch(Rect rect, bool value)
        {
            DrawRect(rect, value ? _accent : TrackColor, rect.height * 0.5f);
            var knobX = value ? rect.xMax - rect.height * 0.5f : rect.x + rect.height * 0.5f;
            DrawCircle(new Vector2(knobX, rect.center.y), rect.height * 0.5f - 3f, value ? InkColor : TextColor);
        }

        private int Segmented(string label, string hint, int selected, string[] options)
        {
            var row = NextRow(54f);
            TrackHint(row, hint);
            GUI.Label(new Rect(row.x, row.y, row.width, 20f), label, _labelStyle);

            var bar = new Rect(row.x, row.y + 22f, row.width, 26f);
            DrawRect(bar, SurfaceColor, 8f);
            var segmentWidth = bar.width / options.Length;
            for (var i = 0; i < options.Length; i++)
            {
                var segment = new Rect(bar.x + segmentWidth * i, bar.y, segmentWidth, bar.height);
                var isSelected = i == selected;
                if (isSelected)
                {
                    DrawRect(new Rect(segment.x + 2f, segment.y + 2f, segment.width - 4f, segment.height - 4f), _accent, 6f);
                }
                else if (segment.Contains(Event.current.mousePosition))
                {
                    DrawRect(new Rect(segment.x + 2f, segment.y + 2f, segment.width - 4f, segment.height - 4f), SurfaceColor, 6f);
                }
                GUI.Label(segment, options[i], isSelected ? _segmentActiveStyle : _segmentStyle);
                if (GUI.Button(segment, GUIContent.none, GUIStyle.none))
                {
                    selected = i;
                }
            }
            return selected;
        }

        private Color Swatches(string label, string hint, Color value, Color[] colors)
        {
            var row = NextRow(54f);
            TrackHint(row, hint);
            GUI.Label(new Rect(row.x, row.y, row.width, 20f), label, _labelStyle);

            const float size = 24f;
            const float spacing = 10f;
            for (var i = 0; i < colors.Length; i++)
            {
                var swatch = new Rect(row.x + (size + spacing) * i, row.y + 24f, size, size);
                var isSelected = IsSameColor(value, colors[i]);
                if (isSelected || swatch.Contains(Event.current.mousePosition))
                {
                    DrawCircle(swatch.center, size * 0.5f + 3f, isSelected ? _accent : TrackColor);
                    DrawCircle(swatch.center, size * 0.5f + 1f, PanelColor);
                }
                DrawCircle(swatch.center, size * 0.5f - 1f, colors[i]);
                if (GUI.Button(swatch, GUIContent.none, GUIStyle.none))
                {
                    value = colors[i];
                }
            }
            return value;
        }

        private static bool IsSameColor(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
        }

        private static void DrawCircle(Vector2 center, float radius, Color color)
        {
            DrawRect(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), color, radius);
        }

        private static void DrawRect(Rect rect, Color color, float radius)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, radius);
        }

        private static void DrawOutline(Rect rect, Color color, float radius)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 1f, radius);
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null)
            {
                return;
            }

            _labelStyle = CreateStyle(TextColor, 13, TextAnchor.MiddleLeft, FontStyle.Normal);
            _strongLabelStyle = CreateStyle(TextColor, 14, TextAnchor.MiddleLeft);
            _valueStyle = CreateStyle(_accent, 12, TextAnchor.MiddleRight);
            _onStyle = CreateStyle(_accent, 11, TextAnchor.MiddleRight);
            _offStyle = CreateStyle(SubTextColor, 11, TextAnchor.MiddleRight);
            _eyebrowStyle = CreateStyle(SubTextColor, 10, TextAnchor.UpperLeft);
            _titleStyle = CreateStyle(TextColor, 22, TextAnchor.UpperLeft);
            _subtitleStyle = CreateStyle(SubTextColor, 11, TextAnchor.UpperLeft, FontStyle.Normal, true);
            _sectionStyle = CreateStyle(SubTextColor, 11, TextAnchor.MiddleLeft);
            _segmentStyle = CreateStyle(SubTextColor, 11, TextAnchor.MiddleCenter, FontStyle.Normal);
            _segmentActiveStyle = CreateStyle(InkColor, 11, TextAnchor.MiddleCenter);
            _hintStyle = CreateStyle(SubTextColor, 11, TextAnchor.UpperLeft, FontStyle.Normal, true);
            _shortcutStyle = CreateStyle(SubTextColor, 11, TextAnchor.MiddleRight, FontStyle.Normal);
            _buttonStyle = CreateStyle(TextColor, 11, TextAnchor.MiddleCenter);
        }

        private static GUIStyle CreateStyle(Color color, int fontSize, TextAnchor alignment,
            FontStyle fontStyle = FontStyle.Bold, bool wordWrap = false)
        {
            return new GUIStyle(GUI.skin.label)
            {
                padding = new RectOffset(),
                margin = new RectOffset(),
                clipping = TextClipping.Clip,
                fontSize = fontSize,
                fontStyle = fontStyle,
                alignment = alignment,
                wordWrap = wordWrap,
                normal = { textColor = color },
            };
        }

        #endregion
    }
}
