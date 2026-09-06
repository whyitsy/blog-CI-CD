namespace Blog.Application.Interfaces
{
    /// <summary>限流配置（对应 appsettings 的 RateLimit 节），粒度与速率均可配置</summary>
    public class RateLimitOptions
    {
        public const string SectionName = "RateLimit";

        /// <summary>总开关</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Redis 不可用时是否降级为内存限流（false 则故障时放行）</summary>
        public bool FallbackToMemory { get; set; } = true;

        /// <summary>规则列表，按顺序匹配，命中即停</summary>
        public List<RateLimitRule> Rules { get; set; } = [];
    }

    public class RateLimitRule
    {
        /// <summary>规则名（日志用）</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>匹配的路径前缀（如 /api/posts），空表示全部</summary>
        public string PathPrefix { get; set; } = string.Empty;

        /// <summary>匹配的 HTTP 方法（如 GET / POST），空表示全部</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>限流粒度：Ip（每客户端 IP）/ Global（全局限流）/ Endpoint（路径+方法维度）</summary>
        public string Granularity { get; set; } = "Ip";

        /// <summary>桶容量（突发允许的最大请求数）</summary>
        public int Capacity { get; set; } = 60;

        /// <summary>令牌生成速率（个/秒）</summary>
        public int TokensPerSecond { get; set; } = 10;
    }
}
