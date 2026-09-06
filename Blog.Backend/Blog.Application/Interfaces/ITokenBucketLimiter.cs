namespace Blog.Application.Interfaces
{
    public record RateLimitResult(bool Allowed, double RetryAfterSeconds, int Remaining)
    {
        public static RateLimitResult Allow(int remaining) => new(true, 0, remaining);
        public static RateLimitResult Reject(double retryAfterSeconds) => new(false, retryAfterSeconds, 0);
    }

    /// <summary>
    /// 令牌桶限流抽象：Redis 实现（分布式）/ 内存实现（Redis 不可用或关闭时降级）
    /// </summary>
    public interface ITokenBucketLimiter
    {
        Task<RateLimitResult> AcquireAsync(string bucketKey, int capacity, int tokensPerSecond,
            CancellationToken cancellationToken = default);
    }
}
