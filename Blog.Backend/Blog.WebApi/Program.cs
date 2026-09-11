using System.Text;
using System.Text.Json;
using Blog.Application;
using Blog.Application.Common;
using Blog.Application.Interfaces;
using Blog.Domain.Entities;
using Blog.Infrastructure;
using Blog.Infrastructure.Persistence;
using Blog.Infrastructure.Security;
using Blog.WebApi.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;

// Serilog 启动早期引导日志（应用构建前的异常也能记录）
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Serilog：控制台 + 滚动文件（logs/blog-.log，按天滚动保留 30 天）
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            path: Path.Combine("logs", "blog-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"));

    builder.Services.AddControllers();
    builder.Services.AddOpenApi();

    // ---------------------------------------------------------------- 认证（JWT）
    // 只发 Access Token，不做 Refresh Token（T7）。有效期见 appsettings 的 Jwt 节。
    var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
    // 环境变量用 .NET 标准的分层写法：Jwt__SigningKey（双下划线代表冒号）
    var signingKey = jwtOptions.SigningKey;
    if (string.IsNullOrWhiteSpace(signingKey))
        signingKey = builder.Configuration["Jwt:SigningKey"] ?? string.Empty;

    if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
    {
        // 未配置签名密钥时**直接中止启动**：静默使用弱密钥意味着「任何人都能伪造 token」，
        // 后果比启动失败严重得多。
        throw new InvalidOperationException(
            "未配置 Jwt:SigningKey（或长度不足 32 字节）。签名密钥是敏感信息，不能写进 appsettings.json。\n" +
            "开发期用用户机密或环境变量提供，例如：\n" +
            "  dotnet user-secrets set \"Jwt:SigningKey\" \"<至少32字节的随机字符串>\"\n" +
            "  # 或设置环境变量 Jwt__SigningKey（注意是双下划线）\n" +
            "生成示例：openssl rand -base64 48");
    }

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ValidateLifetime = true,
                // 默认 5 分钟时钟偏移会让「刚过期」的 token 仍可用，显式收紧
                ClockSkew = TimeSpan.FromSeconds(30),
            };

            // 让 401 / 403 也走统一响应体，与业务错误保持一致
            options.Events = new JwtBearerEvents
            {
                OnChallenge = async context =>
                {
                    context.HandleResponse(); // 阻止默认空响应体
                    if (context.Response.HasStarted) return;

                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(
                        ApiResponse.Fail(ErrorCodes.Unauthorized, "未认证或登录已过期，请重新登录"),
                        UnifiedJsonOptions.Value));
                },
                OnForbidden = async context =>
                {
                    if (context.Response.HasStarted) return;

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(
                        ApiResponse.Fail(ErrorCodes.Forbidden, "无权限执行该操作"),
                        UnifiedJsonOptions.Value));
                },
            };
        });

    // 授权策略：两档角色（见 docs/tech.md §2.4）
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy(AuthorizationPolicies.AdminOnly, policy =>
            policy.RequireRole(nameof(UserRole.Admin)));

        options.AddPolicy(AuthorizationPolicies.ContentWriter, policy =>
            policy.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Author)));
    });

    // 前端开发服务器跨域（Vue3 Vite 默认 5173，可按需扩展）
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Frontend", policy => policy
            .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                ?? ["http://localhost:5173"])
            .AllowAnyHeader()
            .AllowAnyMethod());
    });

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    // 请求日志（含耗时，慢请求一目了然）。
    //
    // 必须注册在 ExceptionHandlingMiddleware **外层**（即先于它注册）。
    // 否则 BusinessException 会先冒泡穿过本中间件，记录下的是异常发生时的 500 与完整堆栈，
    // 而客户端实际收到的是 ExceptionHandlingMiddleware 映射后的 404/403/409 —— 日志与事实不符，
    // 且正常业务失败（重复邮箱、资源不存在等）会刷满 ERR，淹没真正的故障。
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} 响应 {StatusCode} 耗时 {Elapsed:0.0000} ms";

        // 分级：5xx = ERR（真故障），4xx = WRN（业务/权限类可预期失败），其余 = INF
        options.GetLevel = (httpContext, _, ex) =>
            ex is not null ? LogEventLevel.Error
            : httpContext.Response.StatusCode switch
            {
                >= 500 => LogEventLevel.Error,
                >= 400 => LogEventLevel.Warning,
                _ => LogEventLevel.Information,
            };
    });

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // 令牌桶限流（规则见 appsettings RateLimit 节，Redis 故障自动降级内存桶）
    app.UseMiddleware<RateLimitingMiddleware>();

    app.UseCors("Frontend");

    // 认证必须在授权之前：否则 [Authorize] 完全失效（这正是本次修复的 P0 缺陷之一）
    app.UseAuthentication();

    // 解析并校验登录用户（含 TokenVersion 校验），结果放入 HttpContext.Items 供 ICurrentUser 读取。
    // 放在 UseAuthentication 之后，因为需要先有 ClaimsPrincipal。
    app.UseMiddleware<CurrentUserResolutionMiddleware>();

    app.UseAuthorization();

    app.MapControllers();
    app.MapGet("/", () => Results.Ok(new { name = "Blog API", status = "running" }));

    // 启动时自动应用迁移（种子数据由 HasData 保证）
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<BlogDbContext>();
        dbContext.Database.Migrate();
    }

    app.Run();
}
catch (Exception ex) when (ex is not Microsoft.Extensions.Hosting.HostAbortedException)
{
    // HostAbortedException 为 EF Core 设计期工具（dotnet-ef）正常中止宿主，不算启动失败
    Log.Fatal(ex, "应用启动失败");
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>授权策略名（控制器与 Program 共用，避免魔法字符串不一致）</summary>
internal static class AuthorizationPolicies
{
    /// <summary>仅管理员</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>能写内容的人：管理员或作者</summary>
    public const string ContentWriter = "ContentWriter";
}

/// <summary>统一响应体序列化选项（与 ExceptionHandlingMiddleware 保持一致：camelCase）</summary>
internal static class UnifiedJsonOptions
{
    public static readonly JsonSerializerOptions Value = new(JsonSerializerDefaults.Web);
}
