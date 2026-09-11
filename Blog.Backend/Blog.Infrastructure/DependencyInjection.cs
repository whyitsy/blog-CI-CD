using Blog.Application.Interfaces;
using Blog.Domain.IRepository;
using Blog.Infrastructure.Caching;
using Blog.Infrastructure.Files;
using Blog.Infrastructure.Images;
using Blog.Infrastructure.Persistence;
using Blog.Infrastructure.Persistence.Interceptors;
using Blog.Infrastructure.Persistence.Repositories;
using Blog.Infrastructure.RateLimiting;
using Blog.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Blog.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<SlowQueryInterceptor>();
            services.AddDbContext<BlogDbContext>((sp, options) =>
            {
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
                options.AddInterceptors(sp.GetRequiredService<SlowQueryInterceptor>());
            });
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            // 写侧仓储
            services.AddScoped<IAuthorRepository, AuthorRepository>();
            services.AddScoped<IPostRepository, PostRepository>();
            services.AddScoped<ICategoryRepository, CategoryRepository>();
            services.AddScoped<ITagRepository, TagRepository>();
            services.AddScoped<ISiteConfigRepository, SiteConfigRepository>();
            services.AddScoped<ISocialLinkRepository, SocialLinkRepository>();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<ICollectionRepository, CollectionRepository>();

            // 读侧查询仓储
            services.AddScoped<IPostQueryRepository, PostQueryRepository>();
            services.AddScoped<ICategoryQueryRepository, TaxonomyQueryRepository>();
            services.AddScoped<ITagQueryRepository, TaxonomyQueryRepository>();
            services.AddScoped<ISiteQueryRepository, SiteQueryRepository>();
            services.AddScoped<ICollectionQueryRepository, CollectionQueryRepository>();

            // 缓存：Cache:Enabled 总开关 + Cache:Provider 选择 Memory / Redis
            services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.SectionName));
            services.AddMemoryCache();
            services.AddSingleton<MemoryCacheService>();
            services.AddSingleton<RedisCacheService>();
            services.AddSingleton<ICacheService>(sp =>
            {
                var options = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new CacheOptions();
                if (!options.Enabled)
                    return new NullCacheService();

                return options.Provider.Equals("Redis", StringComparison.OrdinalIgnoreCase)
                    ? sp.GetRequiredService<RedisCacheService>()
                    : sp.GetRequiredService<MemoryCacheService>();
            });

            // 文件存储（本地磁盘，/api/files 独立接口对外）
            services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
            // 上传图片转 WebP（节省传输流量）。无状态，单例即可。
            services.Configure<ImageOptimizationOptions>(configuration.GetSection(ImageOptimizationOptions.SectionName));
            services.AddSingleton<IImageOptimizer, ImageSharpOptimizer>();
            services.AddSingleton<IFileStorageService, LocalFileStorageService>();

            // 令牌桶限流：Redis 分布式桶 + 内存降级桶（规则见 appsettings RateLimit 节）
            services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
            services.AddSingleton<RedisTokenBucketLimiter>();
            services.AddSingleton<InMemoryTokenBucketLimiter>();

            // ---------------------------------------------------------------- 认证与安全
            services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
            services.AddHttpContextAccessor();

            services.AddSingleton<IPasswordHasher>(_ => new Pbkdf2PasswordHasher());
            services.AddSingleton<ITokenService, JwtTokenService>();
            services.AddScoped<ICurrentUser, HttpCurrentUser>();

            // 多实例 + 无 Redis 属于危险配置：限流阈值会被放大到实例数倍（见 docs/backend.md §4.5）。
            // 在启动期显式校验并直接失败，而不是运行期静默降级。
            DeploymentGuard.Validate(configuration);

            return services;
        }
    }
}
