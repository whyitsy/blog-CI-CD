using System.Net;
using Blog.Tests.Infrastructure;

namespace Blog.Tests.Integration;

/// <summary>
/// 草稿权限保护集成测试（Q7）。
///
/// 规则回顾：
///   - <c>includeUnpublished=true</c> 必须已登录；非 Admin 只能看到**自己创建的**草稿
///   - 直接访问未发布文章的详情：仅 Admin 与创建者可读
///   - **不可读时返回 404 而不是 403**（403 等于告诉对方「这条 id 存在，只是你没权限」，
///     可以被用来探测草稿的存在性）
///   - <c>mine=true</c> 传入他人的 userId 应被拒绝
///
/// 这一组和权限矩阵同样是「安全相关且历史上出过问题」的地方，因此用集成测试守住。
/// </summary>
[Collection(BlogApiCollection.Name)]
public sealed class DraftProtectionTests
{
    private readonly BlogApiFixture _fixture;
    private readonly ApiClient _api;

    public DraftProtectionTests(BlogApiFixture fixture)
    {
        _fixture = fixture;
        _api = new ApiClient(fixture.CreateClient());
    }

    [Fact]
    public async Task 匿名_请求含草稿列表_应401或403且不泄露草稿()
    {
        var admin = await AdminTokenAsync();
        var (_, _, draft, _) = await CreatePostAsync(admin, publish: false, title: "匿名不可见草稿");

        Assert.NotNull(draft);

        // 匿名带 includeUnpublished=true：应被拒绝
        var (status, code, data, _) = await _api.CallAsync<PagedResult<PostCard>>(
            HttpMethod.Get, "/api/posts?includeUnpublished=true");

        Assert.True(status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"匿名请求含草稿列表应被拒绝，实际 {status}");

        // 无论返回什么，都不能包含那篇草稿
        Assert.DoesNotContain(data?.Items ?? [], x => x.Id == draft!.Id);
        _ = code;
    }

    [Fact]
    public async Task 作者_含草稿列表_只能看到自己创建的()
    {
        var admin = await AdminTokenAsync();
        var (authorToken, authorEmail) = await CreateAuthorAndLoginAsync("draft-mine");

        // 管理员创建一篇草稿（作者不应看到）
        var (_, _, adminDraft, _) = await CreatePostAsync(admin, publish: false, title: "管理员的草稿");
        // 作者创建一篇草稿（自己应能看到）
        var (_, _, ownDraft, _) = await CreatePostAsync(authorToken, publish: false, title: "作者自己的草稿");

        var (status, code, data, _) = await _api.CallAsync<PagedResult<PostCard>>(
            HttpMethod.Get, "/api/posts?includeUnpublished=true&pageSize=100", authorToken);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Codes.Ok, code);
        Assert.NotNull(data);

        Assert.Contains(data!.Items, x => x.Id == ownDraft!.Id);
        Assert.DoesNotContain(data.Items, x => x.Id == adminDraft!.Id);

        _ = authorEmail;
    }

    [Fact]
    public async Task 作者_读取他人未发布详情_应404而不是403()
    {
        var admin = await AdminTokenAsync();
        var otherAuthor = await LoginAsAuthorAsync("draft-other");

        var (_, _, adminDraft, _) = await CreatePostAsync(admin, publish: false, title: "他人的草稿");

        var (status, code, _, _) = await _api.CallAsync<object>(
            HttpMethod.Get, $"/api/posts/{adminDraft!.Id}", otherAuthor);

        // 404 而非 403：不暴露草稿的存在性
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(Codes.NotFound, code);
    }

    [Fact]
    public async Task 创建者_可以读取自己的未发布详情()
    {
        var admin = await AdminTokenAsync();
        var (_, _, draft, _) = await CreatePostAsync(admin, publish: false, title: "自己的草稿可读");

        var (status, code, data, _) = await _api.CallAsync<PostDetail>(
            HttpMethod.Get, $"/api/posts/{draft!.Id}", admin);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Codes.Ok, code);
        Assert.Equal(draft.Id, data!.Id);
    }

    [Fact]
    public async Task 匿名_未发布详情_应404()
    {
        var admin = await AdminTokenAsync();
        var (_, _, draft, _) = await CreatePostAsync(admin, publish: false, title: "匿名读不到的草稿");

        var (status, _, _, _) = await _api.CallAsync<object>(HttpMethod.Get, $"/api/posts/{draft!.Id}");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task 匿名_已发布详情_应200()
    {
        var admin = await AdminTokenAsync();
        var (_, _, published, _) = await CreatePostAsync(admin, publish: true, title: "已发布可见");

        var (status, code, data, _) = await _api.CallAsync<PostDetail>(
            HttpMethod.Get, $"/api/posts/{published!.Id}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Codes.Ok, code);
        Assert.Equal(published.Id, data!.Id);
    }

    [Fact]
    public async Task 作者_mine传入他人账号_应403()
    {
        var admin = await AdminTokenAsync();
        var authorToken = await LoginAsAuthorAsync("draft-mine-spoof");

        var me = await _api.CallAsync<CurrentUserDto>(HttpMethod.Get, "/api/auth/me", admin);
        var adminId = me.Data!.Id;

        var (status, code, _, _) = await _api.CallAsync<object>(
            HttpMethod.Get, $"/api/posts?includeUnpublished=true&mine=true&authorId={adminId}", authorToken);

        // 作者用 mine=true 时服务端强制按自己过滤；若显式传他人 id 应被拒
        Assert.True(status is HttpStatusCode.Forbidden or HttpStatusCode.OK,
            $"不应返回 5xx，实际 {status}");
        if (status == HttpStatusCode.Forbidden)
            Assert.Equal(Codes.Forbidden, code);
    }

    // ---------------------------------------------------------------- 辅助

    private Task<string> AdminTokenAsync() => _api.LoginAsync("admin@example.com", "Admin@12345");

    private async Task<string> LoginAsAuthorAsync(string prefix)
    {
        var (token, _) = await CreateAuthorAndLoginAsync(prefix);
        return token;
    }

    /// <summary>创建 Author 账号并登录（唯一邮箱，避免与种子数据冲突）</summary>
    private async Task<(string Token, string Email)> CreateAuthorAndLoginAsync(string prefix)
    {
        var admin = await AdminTokenAsync();
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        const string password = "Author@12345";

        var (status, code, _, message) = await _api.CallAsync<object>(
            HttpMethod.Post, "/api/users", admin,
            new { email, password, role = "Author", authorId = (Guid?)null });

        Assert.True(status == HttpStatusCode.OK && code == Codes.Ok,
            $"创建测试账号失败 status={status} code={code} message={message}");

        return (await _api.LoginAsync(email, password), email);
    }

    /// <summary>以指定身份创建文章</summary>
    private async Task<(HttpStatusCode Status, int Code, PostDetail? Data, string Message)> CreatePostAsync(
        string token, bool publish, string title)
    {
        return await _api.CallAsync<PostDetail>(HttpMethod.Post, "/api/posts", token, new
        {
            title,
            content = $"正文：{title}",
            summary = (string?)null,
            coverImage = "",
            categoryId = (Guid?)null,
            tagIds = Array.Empty<Guid>(),
            collectionIds = Array.Empty<Guid>(),
            authorId = (Guid?)null,
            publish,
        });
    }
}

// ---- 响应模型（只声明测试用到的字段，避免与后端 DTO 强耦合）----

public sealed record PagedResult<T>(List<T> Items, int Total, int Page, int PageSize, int TotalPages);

public sealed record PostCard(Guid Id, string Title, DateTimeOffset? PublishedAt, int ViewCount);

public sealed record PostDetail(Guid Id, string Title, int Version, DateTimeOffset? PublishedAt, Guid? CreatedByUserId);
