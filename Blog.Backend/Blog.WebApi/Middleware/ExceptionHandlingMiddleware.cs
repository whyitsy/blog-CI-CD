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
                var status = ex.Code == ErrorCodes.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest;
                await WriteAsync(context, status, ex.Code, ex.Message);
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
