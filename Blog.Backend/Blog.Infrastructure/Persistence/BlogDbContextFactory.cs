using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Blog.Infrastructure.Persistence
{
    /// <summary>
    /// 设计期 DbContext 工厂，供 `dotnet ef migrations` / `database update` 使用。
    ///
    /// 为什么需要它：应用启动时会做一系列**生产环境才需要**的校验（如要求配置 Jwt:SigningKey、
    /// 校验多实例必须用 Redis）。如果迁移工具走宿主启动，这些校验会让本地/CI 的迁移命令失败。
    /// 用设计期工厂可以让「生成与应用迁移」独立于应用启动配置。
    ///
    /// 连接串优先级：环境变量 BLOG_CONNECTION → User Secrets/配置 → 本地开发默认值。
    /// </summary>
    public class BlogDbContextFactory : IDesignTimeDbContextFactory<BlogDbContext>
    {
        private const string DefaultDevConnection =
            "Host=localhost;Port=5432;Database=blog_stage2;Username=kky;Password=123456;";

        public BlogDbContext CreateDbContext(string[] args)
        {
            var connectionString =
                Environment.GetEnvironmentVariable("BLOG_CONNECTION")
                ?? DefaultDevConnection;

            var options = new DbContextOptionsBuilder<BlogDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            return new BlogDbContext(options);
        }
    }
}
