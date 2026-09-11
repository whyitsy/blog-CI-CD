using Blog.Application.Interfaces;
using Blog.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace Blog.WebApi.HealthChecks
{
    /// <summary>
    /// 健康检查。
    ///
    /// **对外只回「健康 / 不健康」，不暴露任何内部细节**：
    ///   - 响应体固定为纯文本 <c>Healthy</c> / <c>Unhealthy</c>
    ///   - 数据库/Redis 地址、异常信息、依赖拓扑等**只写日志**，不进响应
    /// 理由：/health 通常对编排系统与负载均衡开放（甚至匿名可访问），
    /// 在响应里带上「哪个依赖挂了 + 报什么错」等于向匿名访问者泄露内部结构，
    /// 而排障真正需要的信息看服务端日志即可。
    ///
    /// 检查项（任一失败则整体 Unhealthy → 503）：
    ///   1. PostgreSQL 连通性
    ///   2. 必需扩展 <c>zhparser</c> 是否可用（中文全文检索的硬依赖）
    ///   3. 迁移是否已全部应用（避免"库结构落后于代码"这种隐蔽故障）
    ///   4. Redis 连通性（仅当 Cache:Provider = Redis 时）
    ///
    /// 内置的 Microsoft.Extensions.Diagnostics.HealthChecks 已在共享框架里，
    /// **不引入任何第三方健康检查包**。
    /// </summary>
    public static class HealthCheckSetup
    {
        /// <summary>数据库与必需扩展的标签</summary>
        public const string DatabaseTag = "db";

        /// <summary>Redis 标签（仅在启用 Redis 时注册）</summary>
        public const string RedisTag = "redis";

        public static IServiceCollection AddBlogHealthChecks(this IServiceCollection services)
        {
            services.AddHealthChecks()
                // 1 + 2 + 3：一个检查里完成「连得上 / 扩展在 / 迁移齐」，
                // 它们都需要打开一次数据库连接，合并可少一次往返。
                .AddCheck<DatabaseHealthCheck>("database", tags: [DatabaseTag])
                // 4：Redis 用与缓存相同的连接串
                .AddCheck<RedisHealthCheck>("redis", tags: [RedisTag]);

            return services;
        }

        /// <summary>
        /// 映射 /health，响应体只含健康状态文本；详细结果写入日志。
        /// </summary>
        public static IEndpointConventionBuilder MapBlogHealthChecks(this IEndpointRouteBuilder endpoints) =>
            endpoints.MapHealthChecks("/health", new HealthCheckOptions
            {
                ResponseWriter = async (context, report) =>
                {
                    var logger = context.RequestServices
                        .GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Blog.WebApi.HealthChecks");

                    // 详情进日志（含每个检查项的状态与失败原因）
                    if (report.Status == HealthStatus.Healthy)
                    {
                        logger.LogInformation("健康检查通过，耗时 {Duration:F0} ms", report.TotalDuration.TotalMilliseconds);
                    }
                    else
                    {
                        var details = report.Entries.Select(e => new
                        {
                            Check = e.Key,
                            Status = e.Value.Status.ToString(),
                            e.Value.Duration,
                            Error = e.Value.Exception?.Message ?? e.Value.Description,
                        });
                        logger.LogWarning("健康检查未通过（{Status}），详情：{Details}",
                            report.Status, JsonSerializer.Serialize(details));
                    }

                    // 对外只给状态文本
                    context.Response.ContentType = "text/plain; charset=utf-8";
                    await context.Response.WriteAsync(report.Status.ToString());
                },
            });
    }

    /// <summary>
    /// 数据库健康检查：能连上 + zhparser 扩展可用 + 迁移已全部应用。
    /// </summary>
    public sealed class DatabaseHealthCheck : IHealthCheck
    {
        private readonly BlogDbContext _db;
        private readonly ILogger<DatabaseHealthCheck> _logger;

        public DatabaseHealthCheck(BlogDbContext db, ILogger<DatabaseHealthCheck> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                // 1) 连通性 + 2) zhparser 扩展可用性：用一次查询同时验证。
                //    直接调用 to_tsvector('chinese', ...) 才能验证「检索配置 chinese 存在」，
                //    只查 pg_extension 是查不出检索配置被删的情况的。
                var extensionOk = await _db.Database
                    .SqlQuery<int>($@"SELECT count(*)::int AS ""Value"" FROM pg_extension WHERE extname = 'zhparser'")
                    .SingleAsync(cancellationToken);

                if (extensionOk == 0)
                {
                    return HealthCheckResult.Unhealthy(
                        "PostgreSQL 缺少 zhparser 扩展（中文全文检索不可用）");
                }

                var chineseConfigOk = await _db.Database
                    .SqlQuery<int>($@"SELECT count(*)::int AS ""Value"" FROM pg_ts_config WHERE cfgname = 'chinese'")
                    .SingleAsync(cancellationToken);

                if (chineseConfigOk == 0)
                {
                    return HealthCheckResult.Unhealthy(
                        "PostgreSQL 缺少 chinese 全文检索配置（中文全文检索不可用）");
                }

                // 实际试一次分词，确保配置真的能用（而不只是存在）
                await _db.Database
                    .SqlQuery<string>($@"SELECT to_tsvector('chinese', '健康检查')::text AS ""Value""")
                    .SingleAsync(cancellationToken);

                // 3) 迁移是否全部应用
                var pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count > 0)
                {
                    return HealthCheckResult.Unhealthy(
                        $"存在未应用的迁移 {pending.Count} 个：{string.Join(", ", pending)}");
                }

                return HealthCheckResult.Healthy("PostgreSQL 正常，zhparser 与迁移均就绪");
            }
            catch (Exception ex)
            {
                // 异常详情只进日志（响应体由 ResponseWriter 统一成状态文本）
                _logger.LogError(ex, "数据库健康检查失败");
                return HealthCheckResult.Unhealthy("数据库健康检查失败", ex);
            }
        }
    }

    /// <summary>
    /// Redis 健康检查。仅当 <c>Cache:Provider = Redis</c> 时才有意义，
    /// 但注册后即使配置成 Memory 也会执行——因此内部按配置短路，返回 Healthy。
    /// </summary>
    public sealed class RedisHealthCheck : IHealthCheck
    {
        private readonly CacheOptions _cache;
        private readonly ILogger<RedisHealthCheck> _logger;

        public RedisHealthCheck(IOptions<CacheOptions> cache, ILogger<RedisHealthCheck> logger)
        {
            _cache = cache.Value;
            _logger = logger;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            // 未启用 Redis 时不把它当作必需依赖（缓存可降级为内存/直连回源）
            if (!_cache.Enabled || !_cache.Provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
                return HealthCheckResult.Healthy("未启用 Redis，跳过");

            try
            {
                await using var conn = await ConnectionMultiplexer.ConnectAsync(_cache.RedisConnection);
                var pong = await conn.GetDatabase().PingAsync();
                return HealthCheckResult.Healthy($"Redis 正常，Ping {pong.TotalMilliseconds:F0} ms");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis 健康检查失败");
                return HealthCheckResult.Unhealthy("Redis 连接失败", ex);
            }
        }
    }
}
