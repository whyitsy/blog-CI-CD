using System.Net;
using System.Net.Http.Json;
using Blog.Tests.Infrastructure;

namespace Blog.Tests.Integration;

/// <summary>
/// 权限矩阵集成测试。
///
/// 为什么优先写这一组：本项目的鉴权是**逐个端点用 [Authorize] 属性声明**的，
/// 历史上一度漏掉过若干写接口（分类/标签/站点/上传），匿名即可调用，
/// 而前端隐藏按钮把问题完全掩盖了（见 docs/09-已知限制与技术债.md §5.1）。
/// 属性式鉴权漏一个就是完全敞开，因此必须有测试逐个端点守住。
///
/// 覆盖三类断言：
///   1. 匿名调用写接口 → 401（且带统一响应体 code 4010）
///   2. 匿名读公开接口 → 200（确保鉴权没有误伤读）
///   3. 角色边界：Author 不能碰 AdminOnly 接口 → 403
/// </summary>
[Collection(BlogApiCollection.Name)]
public sealed class AuthorizationMatrixTests
{
    private readonly BlogApiFixture _fixture;
    private readonly ApiClient _api;

    public AuthorizationMatrixTests(BlogApiFixture fixture)
    {
        _fixture = fixture;
        _api = new ApiClient(fixture.CreateClient());
    }

    /// <summary>所有写接口（方法 + 路径）。这些**必须**要求认证。</summary>
    public static TheoryData<string, string> WriteEndpoints() => new()
    {
        { "POST",   "/api/categories" },
        { "PUT",    "/api/categories/00000000-0000-0000-0000-0000000000a1" },
        { "DELETE", "/api/categories/00000000-0000-0000-0000-0000000000a1?version=1" },
        { "POST",   "/api/tags" },
        { "PUT",    "/api/tags/00000000-0000-0000-0000-0000000000a2" },
        { "DELETE", "/api/tags/00000000-0000-0000-0000-0000000000a2?version=1" },
        { "PUT",    "/api/site/config" },
        { "PUT",    "/api/site/social-links" },
        { "DELETE", "/api/site/social-links/00000000-0000-0000-0000-0000000000a3?version=1" },
        { "POST",   "/api/collections" },
        { "PUT",    "/api/collections/00000000-0000-0000-0000-0000000000a4" },
        { "DELETE", "/api/collections/00000000-0000-0000-0000-0000000000a4?version=1" },
        { "PUT",    "/api/collections/00000000-0000-0000-0000-0000000000a4/posts" },
        { "POST",   "/api/posts" },
        { "PUT",    "/api/posts/00000000-0000-0000-0000-0000000000a5" },
        { "POST",   "/api/posts/00000000-0000-0000-0000-0000000000a5/publish?version=1" },
        { "DELETE", "/api/posts/00000000-0000-0000-0000-0000000000a5?version=1" },
        { "POST",   "/api/authors" },
        { "PUT",    "/api/authors/00000000-0000-0000-0000-0000000000a6" },
        { "DELETE", "/api/authors/00000000-0000-0000-0000-0000000000a6?version=1" },
        { "POST",   "/api/users" },
        { "GET",    "/api/users" },
    };

    [Theory]
    [MemberData(nameof(WriteEndpoints))]
    public async Task 写接口_匿名调用一律401(string method, string path)
    {
        var (status, code, _, _) = await _api.CallAsync<object>(new HttpMethod(method), path);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        // 断言统一响应体，而不只是状态码：前端依赖 code 做分支
        Assert.Equal(Codes.Unauthorized, code);
    }

    /// <summary>公开读接口。鉴权不能误伤读，否则站点对访客不可用。</summary>
    public static TheoryData<string> PublicReadEndpoints() => new()
    {
        "/api/posts",
        "/api/posts/archives",
        "/api/posts/search?keyword=x",
        "/api/categories",
        "/api/tags",
        "/api/collections",
        "/api/site/config",
        "/api/site/social-links",
        "/api/site/stats",
        "/api/authors",
    };

    [Theory]
    [MemberData(nameof(PublicReadEndpoints))]
    public async Task 公开读接口_匿名访问200(string path)
    {
        var (status, code, _, _) = await _api.CallAsync<object>(HttpMethod.Get, path);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Codes.Ok, code);
    }

    [Fact]
    public async Task Author角色_调用AdminOnly接口_应403()
    {
        var token = await LoginAsNewAuthorAsync("matrix-author");

        // 分类/标签/站点/专栏/账号管理都是 AdminOnly
        var adminOnly = new[]
        {
            (HttpMethod.Post, "/api/categories"),
            (HttpMethod.Post, "/api/tags"),
            (HttpMethod.Get, "/api/users"),
        };

        foreach (var (method, path) in adminOnly)
        {
            var (status, code, _, _) = await _api.CallAsync<object>(method, path, token);
            Assert.True(status == HttpStatusCode.Forbidden,
                $"{method} {path} 期望 403，实际 {status}");
            Assert.Equal(Codes.Forbidden, code);
        }
    }

    [Fact]
    public async Task 管理员_可以调用AdminOnly接口()
    {
        var token = await _api.LoginAsync("admin@example.com", "Admin@12345");

        var (status, code, _, _) = await _api.CallAsync<object>(HttpMethod.Get, "/api/users", token);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Codes.Ok, code);
    }

    /// <summary>
    /// 文件上传要求 ContentWriter（读保持公开）。
    /// 上传用 multipart 单独验证，因为它的请求体形态与其他写接口不同。
    /// </summary>
    [Fact]
    public async Task 文件上传_匿名401_作者200_读取公开()
    {
        // 读取必须公开（文章封面/头像要给匿名访客看）
        var read = await _api.CallAsync<object>(HttpMethod.Get, "/api/files/does-not-exist.png");
        Assert.Equal(HttpStatusCode.NotFound, read.Status);

        // 匿名上传 → 401
        var anonUpload = await UploadAsync(null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonUpload.StatusCode);

        // 作者上传 → 不应是 401/403
        var authorToken = await LoginAsNewAuthorAsync("uploader");
        var authorUpload = await UploadAsync(authorToken);
        Assert.True(authorUpload.StatusCode != HttpStatusCode.Unauthorized
                    && authorUpload.StatusCode != HttpStatusCode.Forbidden,
            $"作者上传应被放行，实际 {authorUpload.StatusCode}");
    }

    private async Task<HttpResponseMessage> UploadAsync(string? token)
    {
        using var content = new MultipartFormDataContent();
        var bytes = new ByteArrayContent([1, 2, 3, 4]);
        bytes.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(bytes, "file", "probe.png");

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/files/upload") { Content = content };
        if (token is not null)
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        return await _fixture.CreateClient().SendAsync(req);
    }

    /// <summary>创建一个唯一的 Author 账号并登录（避免与种子数据或其他测试冲突）</summary>
    private async Task<string> LoginAsNewAuthorAsync(string prefix)
    {
        var admin = await _api.LoginAsync("admin@example.com", "Admin@12345");
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        const string password = "Author@12345";

        var (status, code, _, message) = await _api.CallAsync<object>(
            HttpMethod.Post, "/api/users", admin,
            new { email, password, role = "Author", authorId = (Guid?)null });

        Assert.True(status == HttpStatusCode.OK && code == Codes.Ok,
            $"创建测试账号失败 status={status} code={code} message={message}");

        return await _api.LoginAsync(email, password);
    }
}
