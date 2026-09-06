using Blog.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace Blog.Infrastructure.Caching
{
    /// <summary>
    /// 内存缓存实现（无 Redis 环境的默认降级方案）。
    /// 穿透：空值哨兵缓存短 TTL；击穿：key 级信号量互斥重建；雪崩：TTL 随机抖动。
    /// </summary>
    public class MemoryCacheService : ICacheService
    {
        private static readonly object NullSentinel = new();

        private readonly IMemoryCache _cache;
        private readonly CacheOptions _options;
        private readonly ILogger<MemoryCacheService> _logger;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);

        public MemoryCacheService(IMemoryCache cache, IOptions<CacheOptions> options, ILogger<MemoryCacheService> logger)
        {
            _cache = cache;
            _options = options.Value;
            _logger = logger;
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (!_cache.TryGetValue(key, out var raw))
                return Task.FromResult<T?>(default);

            return Task.FromResult(ReferenceEquals(raw, NullSentinel) ? default : (T?)raw);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            _cache.Set(key, value is null ? NullSentinel : value, ApplyJitter(value is null ? _options.NullTtl : ttl));
            _keys.TryAdd(key, 0);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _cache.Remove(key);
            _keys.TryRemove(key, out _);
            return Task.CompletedTask;
        }

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            foreach (var key in _keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
            {
                _cache.Remove(key);
                _keys.TryRemove(key, out _);
            }
            return Task.CompletedTask;
        }

        public async Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl,
            bool cacheNull = true, CancellationToken cancellationToken = default)
        {
            var (hit, value) = TryGet<T>(key);
            if (hit)
            {
                _logger.LogDebug("缓存命中 {Key}", key);
                return value;
            }

            // 防击穿：同 key 只有一个线程回源重建，其余短暂等待后直接回源（不阻塞）
            var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(_options.LockWaitTimeout, cancellationToken))
            {
                _logger.LogWarning("缓存重建锁等待超时，直接回源 {Key}", key);
                return await factory(cancellationToken);
            }

            try
            {
                (hit, value) = TryGet<T>(key);
                if (hit) return value;

                var created = await factory(cancellationToken);

                if (created is null && !cacheNull)
                    return created;

                // 防穿透：空结果也缓存（哨兵），但 TTL 更短；防雪崩：TTL 加抖动
                var effectiveTtl = created is null ? _options.NullTtl : ttl;
                _cache.Set(key, created is null ? NullSentinel : created, ApplyJitter(effectiveTtl));
                _keys.TryAdd(key, 0);
                return created;
            }
            finally
            {
                gate.Release();
            }
        }

        private (bool Hit, T? Value) TryGet<T>(string key)
        {
            if (!_cache.TryGetValue(key, out var raw))
                return (false, default);

            return (true, ReferenceEquals(raw, NullSentinel) ? default : (T?)raw);
        }

        private TimeSpan ApplyJitter(TimeSpan ttl)
        {
            var ratio = _options.TtlJitterRatio;
            if (ratio <= 0) return ttl;

            var factor = 1 + (Random.Shared.NextDouble() * 2 - 1) * ratio;
            return TimeSpan.FromTicks(Math.Max(1, (long)(ttl.Ticks * factor)));
        }
    }
}
