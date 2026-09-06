using Blog.Application.Common;
using Blog.Application.Interfaces;
using Blog.Infrastructure.RateLimiting;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace Blog.WebApi.Middleware
{
    /// <summary>
    /// 令牌桶限流中间件。规则来自配置文件（路径前缀 + 方法 + 粒度 + 容量/速率），
    /// 优先 Redis 分布式桶，Redis 故障时按配置降级为内存桶或放行。
    /// 触发限流返回 429 + ApiResponse(code=4091) + Retry-After 头。
    /// </summary>
    public class RateLimitingMiddleware
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly RequestDelegate _next;
        private readonly ILogger<RateLimitingMiddleware> _logger;

        public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            IOptions<RateLimitOptions> options,
            RedisTokenBucketLimiter redisLimiter,
            InMemoryTokenBucketLimiter memoryLimiter)
        {
            var config = options.Value;
            if (!config.Enabled || !context.Request.Path.StartsWithSegments("/api"))
            {
                await _next(context);
                return;
            }

            var rule = MatchRule(context.Request, config.Rules);
            if (rule is null)
            {
                await _next(context);
                return;
            }

            var bucketKey = BuildBucketKey(context.Request, rule);

            RateLimitResult result;
            try
            {
                result = await redisLimiter.AcquireAsync(bucketKey, rule.Capacity, rule.TokensPerSecond, context.RequestAborted);
            }
            catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or InvalidOperationException)
            {
                if (!config.FallbackToMemory)
                {
                    _logger.LogWarning(ex, "Redis 限流不可用，按配置放行 {Path}", context.Request.Path);
                    await _next(context);
                    return;
                }

                _logger.LogWarning(ex, "Redis 限流不可用，降级内存限流 {Path}", context.Request.Path);
                result = await memoryLimiter.AcquireAsync(bucketKey, rule.Capacity, rule.TokensPerSecond, context.RequestAborted);
            }

            if (!result.Allowed)
            {
                _logger.LogWarning("限流触发 规则={Rule} Key={Key} Path={Path}",
                    rule.Name, bucketKey, context.Request.Path);

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(result.RetryAfterSeconds)).ToString();
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    ApiResponse.Fail(ErrorCodes.RateLimited, "请求过于频繁，请稍后再试"), JsonOptions));
                return;
            }

            context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
            await _next(context);
        }

        private static RateLimitRule? MatchRule(HttpRequest request, List<RateLimitRule> rules)
        {
            foreach (var rule in rules)
            {
                if (!string.IsNullOrEmpty(rule.PathPrefix) &&
                    !request.Path.StartsWithSegments(rule.PathPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(rule.Method) &&
                    !request.Method.Equals(rule.Method, StringComparison.OrdinalIgnoreCase))
                    continue;

                return rule;
            }
            return null;
        }

        private static string BuildBucketKey(HttpRequest request, RateLimitRule rule)
        {
            var identity = rule.Granularity.ToLowerInvariant() switch
            {
                "global" => "global",
                "endpoint" => $"{request.Method}:{request.Path.Value}",
                _ => GetClientIp(request) // 默认 Ip 粒度
            };
            return $"blog:ratelimit:{rule.Name}:{identity}";
        }

        private static string GetClientIp(HttpRequest request)
        {
            // 反向代理场景优先取 X-Forwarded-For 首个 IP
            var forwarded = request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
                return forwarded.Split(',')[0].Trim();

            return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
    }
}
