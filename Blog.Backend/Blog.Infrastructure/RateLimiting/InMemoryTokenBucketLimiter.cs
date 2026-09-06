using Blog.Application.Interfaces;
using System.Collections.Concurrent;

namespace Blog.Infrastructure.RateLimiting
{
    /// <summary>
    /// 内存令牌桶：单实例环境的默认实现，也是 Redis 不可用时的降级实现。
    /// 桶状态保存在内存，锁保证并发下补令牌与取令牌的原子性。
    /// </summary>
    public class InMemoryTokenBucketLimiter : ITokenBucketLimiter
    {
        private sealed class Bucket
        {
            public double Tokens;
            public long TimestampMs;
            public readonly object Gate = new();
        }

        private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
        private long _lastCleanupMs;

        public Task<RateLimitResult> AcquireAsync(string bucketKey, int capacity, int tokensPerSecond,
            CancellationToken cancellationToken = default)
        {
            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var bucket = _buckets.GetOrAdd(bucketKey, _ => new Bucket { Tokens = capacity, TimestampMs = nowMs });

            lock (bucket.Gate)
            {
                var elapsed = (nowMs - bucket.TimestampMs) / 1000.0;
                bucket.Tokens = Math.Min(capacity, bucket.Tokens + elapsed * tokensPerSecond);
                bucket.TimestampMs = nowMs;

                if (bucket.Tokens >= 1)
                {
                    bucket.Tokens -= 1;
                    CleanupIfNeeded(nowMs, capacity, tokensPerSecond);
                    return Task.FromResult(RateLimitResult.Allow((int)bucket.Tokens));
                }

                var retryAfter = (1 - bucket.Tokens) / tokensPerSecond;
                return Task.FromResult(RateLimitResult.Reject(retryAfter));
            }
        }

        /// <summary>周期性清理闲置桶，避免内存无限增长</summary>
        private void CleanupIfNeeded(long nowMs, int capacity, int tokensPerSecond)
        {
            if (nowMs - Interlocked.Read(ref _lastCleanupMs) < 60_000) return;
            Interlocked.Exchange(ref _lastCleanupMs, nowMs);

            var idleThresholdMs = Math.Max(60_000, capacity / (double)Math.Max(1, tokensPerSecond) * 4000);
            foreach (var (key, bucket) in _buckets)
            {
                if (nowMs - bucket.TimestampMs > idleThresholdMs)
                    _buckets.TryRemove(key, out _);
            }
        }
    }
}
