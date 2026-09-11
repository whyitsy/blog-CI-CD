using Blog.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Blog.Tests.Infrastructure;

/// <summary>
/// 集成测试用的应用工厂。
///
/// 与生产/开发配置的差别只有一处：**把连接串指向独立的测试库**。
/// 其余全部走真实实现（真实 PostgreSQL、真实中间件管道、真实认证授权），
/// 这样测出来的才是有意义的语义——尤其是 [Authorize] 这类只能在完整管道里体现的行为。
///
/// 测试库命名：<c>{原库名}_test_{短随机后缀}</c>。
/// 整个测试运行期间只创建一次、结束时删除，因此不会污染开发库。
/// </summary>
public sealed class BlogApiFactory : WebApplicationFactory<Program>
{
    /// <summary>测试库名（含随机后缀，避免与开发库或其他测试运行冲突）</summary>
    public string TestDatabaseName { get; }

    private readonly string _adminConnectionString;

    /// <summary>原始连接串（指向开发库），仅用于解析出主机/凭据与读取库名</summary>
    private readonly string _sourceConnectionString;

    public BlogApiFactory()
    {
        _sourceConnectionString = ResolveDevelopmentConnectionString();

        var builder = new NpgsqlConnectionStringBuilder(_sourceConnectionString);
        var baseDb = builder.Database ?? "blog_stage2";
        // PostgreSQL 标识符上限 63 字节，这里取 8 位短后缀足够避免碰撞且始终安全
        TestDatabaseName = $"{baseDb}_test_{Guid.NewGuid():N}"[..(baseDb.Length + 14)];

        // 连到 postgres 维护库来建/删测试库
        builder.Database = "postgres";
        _adminConnectionString = builder.ConnectionString;

        CreateTestDatabase();
        InitializeChineseTextSearch();
        MigrateTestDatabase();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // 用环境变量来源覆盖连接串：它晚于 appsettings 加载，因此优先级更高。
        // 应用启动时会自动 Migrate()，测试库的表结构与种子数据由此建立。
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = BuildTestConnectionString(),

                // 集成测试会连续发很多请求，远超开发配置的限流阈值
                // （search 规则容量 20、write 规则容量 30），会误报 429 code 4091。
                // 更麻烦的是限流桶存在 **Redis** 里，是跨测试运行共享的外部状态，
                // 会污染后续运行。这里直接关掉：本组测试关注的是权限/并发语义，
                // 限流本身另有专门验证（见 docs 中的 curl 记录）。
                ["RateLimit:Enabled"] = "false",
            });
        });
    }

    private string BuildTestConnectionString()
    {
        var b = new NpgsqlConnectionStringBuilder(_sourceConnectionString) { Database = TestDatabaseName };
        return b.ConnectionString;
    }

    /// <summary>
    /// 在**宿主启动之前**把测试库的表结构与种子数据建好。
    ///
    /// 为什么必须提前建：应用启动时会执行 <c>Database.Migrate()</c>，
    /// 而 **Migrate 不会创建数据库本身**（只建表/应用迁移）。若库是空的，
    /// 启动就会抛异常；WebApplicationFactory 又会把这个异常吞掉，
    /// 只报 "The server has not been started or no web application was configured"，
    /// 非常难排查。所以这里自己先把迁移跑一遍。
    /// </summary>
    private void MigrateTestDatabase()
    {
        var options = new DbContextOptionsBuilder<BlogDbContext>()
            .UseNpgsql(BuildTestConnectionString())
            .Options;

        using var db = new BlogDbContext(options);
        db.Database.Migrate();
    }

    /// <summary>
    /// 在测试库里准备中文全文检索的扩展与检索配置。
    ///
    /// 为什么必须显式做这一步：全新创建的数据库是「干净」的，
    /// 而 Posts.SearchVector 是**生成列**，表达式里用了 <c>to_tsvector('chinese', ...)</c>。
    /// 没有 chinese 检索配置时，迁移直接失败：
    ///   42704: text search configuration "chinese" does not exist
    /// （实测就是这个报错，而且被 WebApplicationFactory 包装成了看不出原因的错误。）
    ///
    /// 这里执行的语句与 EF 迁移/deploy 初始化脚本保持一致：
    /// 扩展已随自定义镜像提供，检索配置需要按库创建一次。
    /// </summary>
    private void InitializeChineseTextSearch()
    {
        using var conn = new NpgsqlConnection(BuildTestConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE EXTENSION IF NOT EXISTS zhparser;
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_ts_config WHERE cfgname = 'chinese') THEN
                    CREATE TEXT SEARCH CONFIGURATION chinese (PARSER = zhparser);
                    ALTER TEXT SEARCH CONFIGURATION chinese
                        ADD MAPPING FOR n,v,a,i,e,l,j,q WITH simple;
                END IF;
            END
            $$;
            """;
        cmd.ExecuteNonQuery();
    }

    private void CreateTestDatabase()
    {
        using var conn = new NpgsqlConnection(_adminConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        // 库名由本类生成（Guid），不含用户输入；这里仍用参数化形式保持一致性
        cmd.CommandText = $"CREATE DATABASE \"{TestDatabaseName}\"";
        cmd.ExecuteNonQuery();
    }

    /// <summary>删除测试库（由测试夹具在全部测试结束后调用）</summary>
    public void DropTestDatabase()
    {
        // 先断开本进程持有的连接，否则 DROP DATABASE 会因连接占用而失败
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlogDbContext>();
            db.Database.CloseConnection();
        }

        NpgsqlConnection.ClearAllPools();

        using var conn = new NpgsqlConnection(_adminConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{TestDatabaseName}\" WITH (FORCE)";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 从 WebApi 的 appsettings.Development.json 读取连接串，
    /// 避免把主机/密码硬编码在测试里（配置改了测试也跟着走）。
    /// </summary>
    private static string ResolveDevelopmentConnectionString()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Blog.Backend.slnx")))
            dir = dir.Parent;

        if (dir is null)
            throw new InvalidOperationException("未能定位仓库根目录（找不到 Blog.Backend.slnx）");

        var settingsPath = Path.Combine(dir.FullName, "Blog.WebApi", "appsettings.Development.json");
        var config = new ConfigurationBuilder().AddJsonFile(settingsPath).Build();
        return config.GetConnectionString("DefaultConnection")
               ?? throw new InvalidOperationException($"未在 {settingsPath} 中找到 ConnectionStrings:DefaultConnection");
    }
}

/// <summary>
/// 共享夹具：整个测试集合共用一个应用实例（宿主启动一次）。
/// 因为应用启动时会执行迁移，按测试类各起一次会明显变慢。
/// </summary>
public sealed class BlogApiFixture : IDisposable
{
    public BlogApiFactory Factory { get; }

    public BlogApiFixture() => Factory = new BlogApiFactory();

    public HttpClient CreateClient() => Factory.CreateClient();

    public void Dispose()
    {
        try { Factory.DropTestDatabase(); }
        finally { Factory.Dispose(); }
    }
}

/// <summary>把夹具接到 xUnit 的集合上，供多个测试类共享</summary>
[CollectionDefinition(Name)]
public sealed class BlogApiCollection : ICollectionFixture<BlogApiFixture>
{
    public const string Name = "blog-api";
}
