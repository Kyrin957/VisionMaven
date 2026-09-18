using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VisionMaven.Core.Abstractions;
using VisionMaven.Core.Domain;

namespace VisionMaven.Flow.Inference;

/// <summary>会话池实现。</summary>
public sealed class InferenceSessionPool : IInferenceSessionPool
{
    private readonly IInferenceEngineRegistry _registry;
    private readonly ILogger<InferenceSessionPool> _logger;
    private readonly ConcurrentDictionary<string, IInferenceEngine> _engines = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public InferenceSessionPool(IInferenceEngineRegistry registry, ILogger<InferenceSessionPool> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public int LoadedCount => _engines.Count;

    public async Task<IInferenceEngine> GetAsync(ModelDescriptor descriptor, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var key = $"{descriptor.ModelId}|{descriptor.Device}|{descriptor.Precision}";
        var engine = _engines.GetValueOrDefault(key);
        if (engine is not null && engine.IsLoaded)
        {
            return engine;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            engine = _engines.GetValueOrDefault(key);
            if (engine is not null && engine.IsLoaded)
            {
                return engine;
            }

            if (engine is not null)
            {
                await engine.DisposeAsync().ConfigureAwait(false);
                _engines.TryRemove(key, out _);
            }

            var created = _registry.Create(InferenceEngineRegistryKey);
            await created.LoadAsync(descriptor, new InferenceOptions(), ct).ConfigureAwait(false);
            _engines[key] = created;
            _logger.LogInformation(
                "推理会话已加载：{ModelId} EP={Device} 会话数={Count}",
                descriptor.ModelId,
                created.ActiveDevice,
                _engines.Count);
            return created;
        }
        catch
        {
            _engines.TryRemove(key, out _);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>默认使用首个可用引擎。</summary>
    private string InferenceEngineRegistryKey
        => _registry.Engines.Count > 0 ? _registry.Engines[0].EngineKey : "onnxruntime";

    public async Task ReleaseAllAsync()
    {
        foreach (var pair in _engines.ToArray())
        {
            if (_engines.TryRemove(pair.Key, out var engine))
            {
                await engine.DisposeAsync().ConfigureAwait(false);
            }
        }

        _logger.LogInformation("推理会话已全部释放");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await ReleaseAllAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
