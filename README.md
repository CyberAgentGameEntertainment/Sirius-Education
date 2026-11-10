# Sirius 学習用リポジトリ: God Ray / SSPR / SSR

Sirius レンダリングシステムのうち、学習用に以下 3 つのポストプロセスだけを抜き出したリポジトリです。

| 機能 | 概要 | デモシーン |
|---|---|---|
| God Ray | 高輝度部分を 2 値化し、光源方向へラジアルブラーして合成する | `Assets/Demo/PostProcess_GodRay/GodRay.unity` |
| SSPR (Screen Space Planar Reflection) | Compute Shader で水平面の平面反射テクスチャを生成する | `Assets/Demo/PostProcess_SSPR/SSPR.unity` |
| SSR (Screen Space Reflection) | 深度ミップマップ (Hi-Z) を使ったスクリーンスペースレイマーチ反射 | `Assets/Demo/PostProcess_SSR/SSR.unity` |

各アルゴリズムの詳しい説明は [Docs/](Docs/README.md) を参照してください。

## 動作環境

- Unity 6000.3 系（`ProjectSettings/ProjectVersion.txt` を参照）
- Universal Render Pipeline 17.3 / Render Graph 有効

## セットアップ

```sh
git clone <this repository>
```

クローンしたフォルダを Unity Hub から開いてください。
Sirius のパッケージは `Packages/manifest.json` から `file:../SiriusPackages/...` でローカル参照しています。

## コードの場所

```
SiriusPackages/
  Sirius.PostProcessing/
    Runtime/Scripts/Features/SiriusPostProcessingFeature.cs   ... RendererFeature 本体（各パスの登録）
    Runtime/Scripts/Features/Passes/GodRay/GodRayRenderPass.cs ... God Ray
    Runtime/Scripts/Features/Passes/SSPR/SSPRPostProcessPass.cs ... SSPR
    Runtime/Scripts/Features/Passes/SSR/SSRPass.cs             ... SSR
    Runtime/Scripts/Features/Passes/SetupPass.cs               ... SSPR 用グローバルキーワード / 前フレーム VP 行列の設定
    Runtime/Scripts/Volumes/                                   ... 各機能の VolumeComponent
    Runtime/Shaders/GodRay/                                    ... God Ray シェーダー
    Runtime/Shaders/SSR/                                       ... SSR シェーダー
    Runtime/Shaders/Resources/SSPRCompute.compute              ... SSPR Compute Shader
  Sirius.Core/
    Runtime/Scripts/DepthMipMapUtil/                           ... SSR が使う深度ミップマップ生成
    Runtime/Scripts/DualBlurHelper.cs                          ... SSPR が使う Dual Blur
    Runtime/Shaders/                                           ... 共通 HLSL ユーティリティ
```

## 使い方

1. `Assets/Settings/UniversalRenderPipelineAsset_Renderer.asset`（Forward+）または
   `UniversalRenderPipelineAsset_Defferd_Renderer.asset`（Deferred）に `SiriusPostProcessingFeature` が追加されています。
   各機能の有効化フラグは RendererFeature のインスペクタで切り替えます。
2. シーン内の Volume に `Sirius/GodRay`、`Sirius/SSPR`、`Sirius/Screen Space Reflections` の各 VolumeComponent を追加し、
   `Enable`（SSR は `enabled`）を ON にするとパスが実行されます。
3. SSPR の反射を受ける面には `Assets/Demo/PostProcess_SSPR/Scripts/SSPRRecieverDemo.shader` のように
   `_CT_ENABLE_SSPR` キーワードで反射テクスチャをサンプリングするシェーダーを使います。

## ライセンス

[LICENSE](LICENSE) を参照してください。
サードパーティのコードについては [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照してください。
