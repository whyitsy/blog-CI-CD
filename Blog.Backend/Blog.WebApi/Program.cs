using Blog.Application;
using Blog.Infrastructure;
using Blog.Infrastructure.Persistence;
using Blog.WebApi.Middleware;
using Microsoft.EntityFrameworkCore;
using Serilog;

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
    builder.Services.AddInfrastructureServices(builder.Configuration);

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseMiddleware<ExceptionHandlingMiddleware>();

    // 令牌桶限流（规则见 appsettings RateLimit 节，Redis 故障自动降级内存桶）
    app.UseMiddleware<RateLimitingMiddleware>();

    // 请求日志（含耗时，慢请求一目了然）
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} 响应 {StatusCode} 耗时 {Elapsed:0.0000} ms";
    });

    app.UseCors("Frontend");

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
