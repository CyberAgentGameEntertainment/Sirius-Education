// --------------------------------------------------------------
// Copyright 2025 CyberAgent, Inc.
// --------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace Sirius.Core.Runtime.Scripts
{
    /// <summary>
    ///     Render Graph用のプロファイリングスコープ
    ///     RenderGraphProfilingScopeがAddRenderPassを使っているため使えないので自前で用意。
    ///     実装はAddUnsafeRenderPassを使っている点以外はRenderGraphProfilingScopeと同じ
    /// </summary>
    public struct CTRenderGraphProfilingScope : IDisposable
    {
        private readonly RenderGraph _renderGraph;
        private readonly ProfilingSampler _sampler;
        private bool _disposed;
        private class ProfilingScopePassData
        {
            public ProfilingSampler sampler;
        }
        private void BeginProfilingSampler([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_sampler == null)
                return;

            using var builder = _renderGraph.AddUnsafePass<ProfilingScopePassData>("Begin CTRenderGraph Profiling Scope", out var passData,
                                                                                   _sampler, file, line);
            passData.sampler = _sampler;
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((ProfilingScopePassData data, UnsafeGraphContext ctx) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                data.sampler.Begin(cmd);
            });
#endif
        }
        private void EndProfilingSampler([CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_sampler == null)
                return;

            using var builder = _renderGraph.AddUnsafePass<ProfilingScopePassData>("End CTRenderGraph Profiling Scope", out var passData,
                                                                                   _sampler, file, line);
            passData.sampler = _sampler;
            builder.AllowPassCulling(false);
            builder.SetRenderFunc((ProfilingScopePassData data, UnsafeGraphContext ctx) =>
            {
                var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                data.sampler.End(cmd);
            });
#endif
        }

        public CTRenderGraphProfilingScope(RenderGraph renderGraph, ProfilingSampler sampler)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            _renderGraph = renderGraph;
            _sampler = sampler;
            _disposed = false;
            BeginProfilingSampler();
#else
            _renderGraph = null;
            _sampler = null;
            _disposed = true;
#endif
        }
        /// <summary>
        ///     Dispose パターンの実装
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        // Dispose パターンの protected 実装
        private void Dispose(bool disposing)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if (_disposed)
                return;

            // 構造体なので空のコンストラクタで初期化されている可能性があり、クラッシュを避けるため
            // `cmd` が null でないことを確認する必要がある。クラスにすれば解決するが、
            // 毎フレーム GC Alloc が発生する（この構造体は頻繁に使われる）
            if (disposing)
            {
                EndProfilingSampler();
            }

            _disposed = true;
#endif
        }
    }
}

