using Blog.Application.Interfaces;

namespace Blog.Infrastructure.Caching
{
    /// <summary>缓存总开关关闭时的旁路实现：不读写任何缓存，直接回源</summary>
    public class NullCacheService : ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<T?>(default);

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task<T?> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan ttl,
            bool cacheNull = true, CancellationToken cancellationToken = default) =>
            await factory(cancellationToken);
    }
}
