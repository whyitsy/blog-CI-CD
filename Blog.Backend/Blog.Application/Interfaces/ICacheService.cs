namespace Blog.Application.Interfaces
{
    /// <summary>
    /// 缓存抽象。实现需保证：Redis 不可用时降级（不抛异常），并在内部处理
    /// 缓存穿透（空值哨兵）、缓存击穿（互斥重建）、缓存雪崩（TTL 随机抖动）。
    /// </summary>
    public interface ICacheService
    {
        Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

        Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default);

        Task RemoveAsync(string key, CancellationToken cancellationToken = default);

        Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

        /// <summary>
        /// 查缓存，未命中则回源并写入缓存。
        /// </summary>
        /// <param name="cacheNull">是否缓存空结果（防穿透），列表类建议 true，详情类建议 false</param>
        Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl,
            bool cacheNull = true, CancellationToken cancellationToken = default);
    }
}
