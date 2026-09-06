using Blog.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace Blog.Infrastructure.Caching
{
    /// <summary>
    /// Redis 缓存实现。任何 Redis 故障（连接断开、超时）都降级为直查回源，不向调用方抛异常。
    /// 穿透：空值哨兵短 TTL；击穿：Redis 分布式锁互斥重建，抢锁失败直接回源；雪崩：TTL 随机抖动。
    /// </summary>
    public class RedisCacheService : ICacheService, IDisposable
    {
        private const string NullSentinel = "∅";
        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

        private readonly CacheOptions _options;
        private readonly ILogger<RedisCacheService> _logger;
        private readonly Lazy<ConnectionMultiplexer> _multiplexer;

        public RedisCacheService(IOptions<CacheOptions> options, ILogger<RedisCacheService> logger)
        {
            _options = options.Value;
            _logger = logger;
            _multiplexer = new Lazy<ConnectionMultiplexer>(() =>
                ConnectionMultiplexer.Connect(_options.RedisConnection));
        }

        private IDatabase? TryGetDatabase()
        {
            try
            {
                var db = _multiplexer.Value.GetDatabase();
                return _multiplexer.Value.IsConnected ? db : null;
            }
            catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Redis 不可用，缓存操作降级");
                return null;
            }
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            var db = TryGetDatabase();
            if (db is null) return default;

            try
            {
                var raw = await db.StringGetAsync(key);
                if (!raw.HasValue || raw == NullSentinel) return default;
                return JsonSerializer.Deserialize<T>((string)raw!, SerializerOptions);
            }
            catch (Exception ex) when (ex is RedisException or JsonException)
            {
                _logger.LogWarning(ex, "Redis 读取失败 {Key}，降级", key);
                return default;
            }
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            var db = TryGetDatabase();
            if (db is null) return;

            try
            {
                var effectiveTtl = ApplyJitter(value is null ? _options.NullTtl : ttl);
                var payload = value is null ? NullSentinel : JsonSerializer.Serialize(value, SerializerOptions);
                await db.StringSetAsync(key, payload, effectiveTtl);
            }
            catch (RedisException ex)
            {
                _logger.LogWarning(ex, "Redis 写入失败 {Key}，忽略", key);
            }
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            var db = TryGetDatabase();
            if (db is null) return;

            try
            {
                await db.KeyDeleteAsync(key);
            }
            catch (RedisException ex)
            {
                _logger.LogWarning(ex, "Redis 删除失败 {Key}，忽略", key);
            }
        }

        public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            var db = TryGetDatabase();
            if (db is null) return;

            try
            {
                foreach (var server in _multiplexer.Value.GetServers())
                {
                    if (!server.IsConnected) continue;
                    var keys = server.Keys(pattern: $"{prefix}*").ToArray();
                    if (keys.Length > 0)
                        await db.KeyDeleteAsync(keys);
                }
            }
            catch (RedisException ex)
            {
                _logger.LogWarning(ex, "Redis 按前缀删除失败 {Prefix}，忽略", prefix);
            }
        }

        public async Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl,
            bool cacheNull = true, CancellationToken cancellationToken = default)
        {
            var cached = await GetAsync<T>(key, cancellationToken);
            if (cached is not null)
            {
                _logger.LogDebug("缓存命中 {Key}", key);
                return cached;
            }

            // 防击穿：分布式锁，仅一个请求回源重建；抢锁失败直接回源（短等待，不阻塞）
            var db = TryGetDatabase();
            var lockKey = $"lock:{key}";
            var lockToken = Guid.NewGuid().ToString("N");
            var lockAcquired = false;

            if (db is not null)
            {
                try
                {
                    lockAcquired = await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(10));
                }
                catch (RedisException ex)
                {
                    _logger.LogWarning(ex, "Redis 锁获取失败 {Key}，直接回源", key);
                }
            }

            if (!lockAcquired)
                return await factory(cancellationToken);

            try
            {
                // 双检：拿到锁后再查一次，可能已被其他请求重建
                cached = await GetAsync<T>(key, cancellationToken);
                if (cached is not null) return cached;

                var created = await factory(cancellationToken);
                if (created is null && !cacheNull)
                    return created;

                await SetAsync(key, created, ttl, cancellationToken);
                return created;
            }
            finally
            {
                try
                {
                    await db!.LockReleaseAsync(lockKey, lockToken);
                }
                catch (RedisException ex)
                {
                    _logger.LogWarning(ex, "Redis 锁释放失败 {Key}，等待自动过期", key);
                }
            }
        }

        private TimeSpan ApplyJitter(TimeSpan ttl)
        {
            var ratio = _options.TtlJitterRatio;
            if (ratio <= 0) return ttl;

            var factor = 1 + (Random.Shared.NextDouble() * 2 - 1) * ratio;
            return TimeSpan.FromTicks(Math.Max(1, (long)(ttl.Ticks * factor)));
        }

        public void Dispose()
        {
            if (_multiplexer.IsValueCreated)
                _multiplexer.Value.Dispose();
        }
    }
}
