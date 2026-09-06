namespace Blog.Application.Interfaces
{
    /// <summary>缓存配置（对应 appsettings 的 Cache 节）</summary>
    public class CacheOptions
    {
        public const string SectionName = "Cache";

        /// <summary>总开关：false 时缓存完全旁路，直接回源</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>提供者：Memory / Redis。Redis 不可用时自动降级回源</summary>
        public string Provider { get; set; } = "Memory";

        /// <summary>Redis 连接串（Provider = Redis 时必填）</summary>
        public string RedisConnection { get; set; } = "localhost:6379";

        /// <summary>空值哨兵 TTL（防穿透），短于正常 TTL</summary>
        public TimeSpan NullTtl { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>重建互斥锁等待超时（防击穿），超时直接回源不阻塞</summary>
        public TimeSpan LockWaitTimeout { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>TTL 随机抖动比例（防雪崩），0.2 表示 ±20%</summary>
        public double TtlJitterRatio { get; set; } = 0.2;
    }
}
