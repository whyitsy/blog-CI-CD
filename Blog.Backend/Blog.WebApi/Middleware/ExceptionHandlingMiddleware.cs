using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Blog.WebApi.Middleware
{
    /// <summary>
    /// 全局异常处理：BusinessException → 对应业务码；DbUpdateConcurrencyException → 409；
    /// 其他未处理异常 → 500。统一输出 ApiResponse 结构。
    /// </summary>
    public class ExceptionHandlingMiddleware
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (BusinessException ex)
            {
                _logger.LogWarning("业务异常 {Code}: {Message} {Path}", ex.Code, ex.Message, context.Request.Path);
                await WriteAsync(context, MapStatusCode(ex.Code), ex.Code, ex.Message);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "乐观锁并发冲突 {Path}", context.Request.Path);
                await WriteAsync(context, StatusCodes.Status409Conflict, ErrorCodes.ConcurrencyConflict,
                    "数据已被其他请求修改，请刷新后重试");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "未处理异常 {Path}", context.Request.Path);
                await WriteAsync(context, StatusCodes.Status500InternalServerError, ErrorCodes.InternalError,
                    "服务器内部错误");
            }
        }

        /// <summary>
        /// 业务码 -> HTTP 状态码。
        /// 4010/4030 必须映射为 401/403 而非 400，否则：
        ///   - 前端无法用 HTTP 状态判断「需要重新登录」（拦截器通常只看 401）
        ///   - 语义错误：400 表示「请求本身有问题」，而这两个码表示「身份/权限有问题」
        /// </summary>
        private static int MapStatusCode(int code) => code switch
        {
            ErrorCodes.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorCodes.Forbidden => StatusCodes.Status403Forbidden,
            ErrorCodes.NotFound => StatusCodes.Status404NotFound,
            ErrorCodes.ConcurrencyConflict => StatusCodes.Status409Conflict,
            ErrorCodes.RateLimited => StatusCodes.Status429TooManyRequests,
            ErrorCodes.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
            _ => StatusCodes.Status400BadRequest,
        };

        private static async Task WriteAsync(HttpContext context, int httpStatus, int code, string message)
        {
            if (context.Response.HasStarted) return;

            context.Response.StatusCode = httpStatus;
            context.Response.ContentType = "application/json; charset=utf-8";
            var body = ApiResponse.Fail(code, message);
            await context.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
        }
    }
}
