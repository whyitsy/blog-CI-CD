using Blog.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Blog.Infrastructure.Security
{
    /// <summary>
    /// 部署拓扑校验（T5 决策：启动期显式配置校验，而非运行期探测）。
    ///
    /// 规则：多实例部署 **必须** 启用 Redis 缓存与限流。
    /// 原因（见 docs/backend.md §4.5）：
    ///   - 每台实例各自用内存缓存 -> DB 压力 = 实例数倍
    ///   - 每台实例各自一个内存令牌桶 -> 实际限流阈值被放大到「配置值 × 实例数」，**限流形同虚设**
    ///
    /// 这类问题靠「静默降级」会非常难排查，因此在启动时就**直接失败并给出明确原因**，
    /// 把「部署拓扑」当作运维必须显式声明的输入，而不是让代码去猜。
    /// </summary>
    public static class DeploymentGuard
    {
        public static void Validate(IConfiguration configuration)
        {
            var deployment = configuration.GetSection(DeploymentOptions.SectionName).Get<DeploymentOptions>()
                             ?? new DeploymentOptions();

            if (deployment.InstanceCount <= 1 && !deployment.RequireRedis)
                return; // 单实例且未强制要求 Redis：内存档位是允许的

            var cache = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>()
                        ?? new CacheOptions();

            var providerIsRedis = cache.Enabled &&
                                  cache.Provider.Equals("Redis", StringComparison.OrdinalIgnoreCase);

            if (providerIsRedis) return;

            var reason = !cache.Enabled
                ? "Cache:Enabled = false（缓存被完全旁路）"
                : $"Cache:Provider = \"{cache.Provider}\"（不是 Redis）";

            throw new InvalidOperationException(
                "检测到「多实例 / 强制要求 Redis」的部署配置，但缓存未使用 Redis：" + reason + "。\n" +
                $"  Deployment:InstanceCount = {deployment.InstanceCount}\n" +
                $"  Deployment:RequireRedis   = {deployment.RequireRedis}\n" +
                "多实例下使用进程内缓存与内存限流会导致：限流阈值被放大到实例数倍、缓存不共享。\n" +
                "请二选一：① 配置 Cache:Provider = Redis；② 若确实是单实例，把 Deployment:InstanceCount 设为 1。\n" +
                "（设计依据见 docs/backend.md §4.5 与 docs/tech.md §4.3）");
        }
    }
}
