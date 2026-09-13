using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
            catch (DbUpdateException ex) when (IsStringTooLong(ex, out var detail))
            {
                // 兜底：某个字段超过了数据库列定义的长度。
                //
                // 正常路径上不该走到这里 —— Application 层应当先拦下（见 FieldLimits）。
                // 但**万一漏了某个字段**，用户也不该看到「服务器内部错误」：
                // 「你输入的内容太长」明明是 4xx 语义。这里降级为 4001 并记 Warning，
                // 既给出可读提示，又保证它在日志里仍然可见（不会被当成正常请求淹没）。
                _logger.LogWarning(ex,
                    "字段超长被数据库拒绝 {Path}：{Detail}。说明 Application 层漏了该校验（见 FieldLimits）",
                    context.Request.Path, detail);
                await WriteAsync(context, StatusCodes.Status400BadRequest, ErrorCodes.InvalidArgument,
                    "提交的内容超过了字段允许的长度，请缩短后重试");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "未处理异常 {Path}", context.Request.Path);
                await WriteAsync(context, StatusCodes.Status500InternalServerError, ErrorCodes.InternalError,
                    "服务器内部错误");
            }
        }

        /// <summary>
        /// PostgreSQL SQLSTATE 22001：字符串超出列定义长度
        /// （<c>value too long for type character varying(n)</c>）。
        /// 这里用字面量而不是 Npgsql 的常量，避免依赖具体版本的常量命名。
        /// </summary>
        private const string SqlStateStringDataRightTruncation = "22001";

        /// <summary>
        /// 在异常链里找 PostgreSQL 的 22001。
        /// Npgsql 会把它包在 <see cref="DbUpdateException"/>.InnerException 里，
        /// 所以必须**逐层**找，不能只看最外层。
        /// </summary>
        private static bool IsStringTooLong(Exception? exception, out string? detail)
        {
            for (var current = exception; current is not null; current = current.InnerException)
            {
                if (current is PostgresException { SqlState: SqlStateStringDataRightTruncation } pg)
                {
                    detail = pg.MessageText;
                    return true;
                }
            }

            detail = null;
            return false;
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
