using Blog.Application.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Blog.Infrastructure.RateLimiting
{
    /// <summary>
    /// Redis 令牌桶：Lua 脚本保证「补令牌 + 取令牌」原子性，分布式环境多实例共享同一桶。
    /// Redis 故障时抛出 RedisException 由中间件决定是否降级内存实现。
    /// </summary>
    public class RedisTokenBucketLimiter : ITokenBucketLimiter
    {
        // KEYS[1] = 桶 key
        // ARGV[1] = 容量, ARGV[2] = 速率(个/秒), ARGV[3] = 当前毫秒时间戳
        // 返回 { allowed(0/1), remaining, retryAfterMs }
        private const string Script = """
            local bucket = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
            local capacity = tonumber(ARGV[1])
            local rate = tonumber(ARGV[2])
            local now = tonumber(ARGV[3])

            local tokens = tonumber(bucket[1]) or capacity
            local ts = tonumber(bucket[2]) or now

            -- 按流逝时间补充令牌
            tokens = math.min(capacity, tokens + (now - ts) / 1000 * rate)

            local allowed = 0
            local retryAfter = 0
            if tokens >= 1 then
                tokens = tokens - 1
                allowed = 1
            else
                retryAfter = math.ceil((1 - tokens) / rate * 1000)
            end

            redis.call('HMSET', KEYS[1], 'tokens', tokens, 'ts', now)
            -- 桶闲置 2 倍填满时间后自动过期，避免 key 无限增长
            redis.call('PEXPIRE', KEYS[1], math.ceil(capacity / rate * 2000))
            return { allowed, math.floor(tokens), retryAfter }
            """;

        private readonly ILogger<RedisTokenBucketLimiter> _logger;
        private readonly Lazy<ConnectionMultiplexer> _multiplexer;

        public RedisTokenBucketLimiter(Microsoft.Extensions.Options.IOptions<CacheOptions> cacheOptions,
            ILogger<RedisTokenBucketLimiter> logger)
        {
            _logger = logger;
            _multiplexer = new Lazy<ConnectionMultiplexer>(() =>
                ConnectionMultiplexer.Connect(cacheOptions.Value.RedisConnection));
        }

        public async Task<RateLimitResult> AcquireAsync(string bucketKey, int capacity, int tokensPerSecond,
            CancellationToken cancellationToken = default)
        {
            var db = _multiplexer.Value.GetDatabase();
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var result = (RedisValue[]?)await db.ScriptEvaluateAsync(Script,
                keys: [bucketKey],
                values: [capacity, tokensPerSecond, nowMs]);

            if (result is null || result.Length < 3)
            {
                _logger.LogWarning("限流脚本返回异常 {Key}，放行", bucketKey);
                return RateLimitResult.Allow(capacity);
            }

            var allowed = (int)result[0] == 1;
            return allowed
                ? RateLimitResult.Allow((int)result[1])
                : RateLimitResult.Reject((long)result[2] / 1000.0);
        }
    }
}
