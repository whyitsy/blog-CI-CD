using Blog.Application.Interfaces;
using Blog.Domain.IRepository;
using Blog.Infrastructure.Caching;
using Blog.Infrastructure.Files;
using Blog.Infrastructure.Persistence;
using Blog.Infrastructure.Persistence.Interceptors;
using Blog.Infrastructure.Persistence.Repositories;
using Blog.Infrastructure.RateLimiting;
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

            // 读侧查询仓储
            services.AddScoped<IPostQueryRepository, PostQueryRepository>();
            services.AddScoped<ICategoryQueryRepository, TaxonomyQueryRepository>();
            services.AddScoped<ITagQueryRepository, TaxonomyQueryRepository>();
            services.AddScoped<ISiteQueryRepository, SiteQueryRepository>();

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
            services.AddSingleton<IFileStorageService, LocalFileStorageService>();

            // 令牌桶限流：Redis 分布式桶 + 内存降级桶（规则见 appsettings RateLimit 节）
            services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
            services.AddSingleton<RedisTokenBucketLimiter>();
            services.AddSingleton<InMemoryTokenBucketLimiter>();

            return services;
        }
    }
}
