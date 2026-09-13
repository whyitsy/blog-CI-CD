using System.Net;
using Blog.Tests.Infrastructure;

namespace Blog.Tests.Integration;

/// <summary>
/// 「字段长度」这一系列业务校验的**端点级**测试。
///
/// <para><b>为什么需要这一组</b></para>
/// 起因是一个真实缺陷（archive/问题排查记录.md §3）：<c>Posts.Summary</c> 在库里是
/// <c>varchar(120)</c>，但 Application 层**根本没有校验摘要长度**，前端也没有
/// <c>maxlength</c>，于是超出后一路穿到数据库，抛 <c>DbUpdateException</c>，
/// 用户看到的是「服务器内部错误」而不是「摘要太长了」。
///
/// 顺着这条线做全库审计，发现共有 7 处「数据库有约束、Application 层却没有校验」。
/// 本文件把它们逐个钉住。
///
/// <para><b>两条纪律</b></para>
/// <list type="number">
///   <item>每条负向用例都配**正向/边界**用例 —— 只断言"被拒绝"的话，
///         「把所有输入都拒掉」的退化实现也能全绿。</item>
///   <item>断言到**具体字段名**，而不只是 code。4001 是通用参数错误码，
///         标题为空、slug 非法同样是 4001；只断言 code 会因错误的原因变绿。</item>
/// </list>
/// </summary>
[Collection(BlogApiCollection.Name)]
public sealed class FieldLengthValidationTests
{
    private readonly ApiClient _api;
    private string? _adminToken;

    public FieldLengthValidationTests(BlogApiFixture fixture) => _api = new ApiClient(fixture.CreateClient());

    private async Task<string> AdminAsync() =>
        _adminToken ??= await _api.LoginAsync("admin@example.com", "Admin@12345");

    private static string Chars(int n) => new('中', n);

    // ------------------------------------------------------------------ 文章摘要（事故现场）

    [Fact]
    public async Task 摘要_恰好200字放行_201字被拒()
    {
        var token = await AdminAsync();

        // 正向：恰好 200 —— 迁移把列从 varchar(120) 扩到 varchar(200) 就是为了支持这个
        var ok = await CreatePostAsync(token, Chars(200));
        Assert.True(ok.Status == HttpStatusCode.OK && ok.Code == Codes.Ok,
            $"200 字摘要应放行，实际 status={ok.Status} code={ok.Code} message={ok.Message}");

        // 负向：201 —— 必须是「参数校验失败」而不是「服务器内部错误」
        var bad = await CreatePostAsync(token, Chars(201));
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal(Codes.InvalidArgument, bad.Code);
        Assert.Contains("摘要", bad.Message);
        Assert.Contains("200", bad.Message);
    }

    /// <summary>
    /// 回归护栏：以前这里返回的是 500 + 「服务器内部错误」。
    /// 这条用例专门盯着「不能是 5xx」。
    /// </summary>
    [Fact]
    public async Task 摘要超长时不能返回500()
    {
        var token = await AdminAsync();

        var bad = await CreatePostAsync(token, Chars(5000));

        Assert.True((int)bad.Status < 500,
            $"超长摘要属于 4xx 语义，不该是 {bad.Status}");
        Assert.NotEqual(Codes.InternalError, bad.Code);
    }

    /// <summary>留空摘要走自动截取（正文前 50 字），不受 200 上限影响</summary>
    [Fact]
    public async Task 摘要留空仍然放行()
    {
        var token = await AdminAsync();

        var ok = await CreatePostAsync(token, summary: "");
        Assert.True(ok.Status == HttpStatusCode.OK && ok.Code == Codes.Ok,
            $"留空摘要应走自动截取，实际 status={ok.Status} code={ok.Code} message={ok.Message}");
    }

    // ------------------------------------------------------------------ 作者简介

    [Fact]
    public async Task 作者简介_500字放行_501字被拒()
    {
        var token = await AdminAsync();
        var author = await CreateAuthorAsync(token);

        var ok = await UpdateAuthorAsync(token, author, new string('中', 500));
        Assert.True(ok.Status == HttpStatusCode.OK && ok.Code == Codes.Ok,
            $"500 字简介应放行，实际 status={ok.Status} code={ok.Code} message={ok.Message}");

        var bad = await UpdateAuthorAsync(token, author with { Version = ok.Data!.Version }, new string('中', 501));
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal(Codes.InvalidArgument, bad.Code);
        Assert.Contains("简介", bad.Message);
    }

    // ------------------------------------------------------------------ 专栏简介

    [Fact]
    public async Task 专栏简介_500字放行_501字被拒()
    {
        var token = await AdminAsync();

        var ok = await CreateCollectionAsync(token, new string('中', 500));
        Assert.True(ok.Status == HttpStatusCode.OK && ok.Code == Codes.Ok,
            $"500 字专栏简介应放行，实际 status={ok.Status} code={ok.Code} message={ok.Message}");

        var bad = await CreateCollectionAsync(token, new string('中', 501));
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal(Codes.InvalidArgument, bad.Code);
        Assert.Contains("简介", bad.Message);
    }

    // ------------------------------------------------------------------ 社交链接

    [Fact]
    public async Task 社交链接名称_50字放行_51字被拒()
    {
        var token = await AdminAsync();

        var ok = await SaveSocialLinkAsync(token, new string('中', 50));
        Assert.True(ok.Status == HttpStatusCode.OK && ok.Code == Codes.Ok,
            $"50 字名称应放行，实际 status={ok.Status} code={ok.Code} message={ok.Message}");

        var bad = await SaveSocialLinkAsync(token, new string('中', 51));
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal(Codes.InvalidArgument, bad.Code);
        Assert.Contains("社交链接名称", bad.Message);
    }

    // ------------------------------------------------------------------ 辅助

    private Task<(HttpStatusCode Status, int Code, PostDetail? Data, string Message)> CreatePostAsync(
        string token, string summary) =>
        _api.CallAsync<PostDetail>(HttpMethod.Post, "/api/posts", token, new
        {
            title = $"长度校验-{Guid.NewGuid():N}",
            content = "正文内容，保证不是被「内容不能为空」拦下的。",
            summary,
            coverImage = "",
            categoryId = (Guid?)null,
            tagIds = Array.Empty<Guid>(),
            collectionIds = Array.Empty<Guid>(),
            authorId = (Guid?)null,
            publish = false,
        });

    private async Task<AuthorDetail> CreateAuthorAsync(string token)
    {
        var (status, code, data, message) = await _api.CallAsync<AuthorDetail>(
            HttpMethod.Post, "/api/authors", token,
            new { name = "长度校验作者", email = $"len-{Guid.NewGuid():N}@example.com", bio = "", avatar = "" });

        Assert.True(status == HttpStatusCode.OK && code == Codes.Ok && data is not null,
            $"创建测试作者失败 status={status} code={code} message={message}");
        return data!;
    }

    private Task<(HttpStatusCode Status, int Code, AuthorDetail? Data, string Message)> UpdateAuthorAsync(
        string token, AuthorDetail author, string bio) =>
        _api.CallAsync<AuthorDetail>(HttpMethod.Put, $"/api/authors/{author.Id}", token, new
        {
            name = author.Name,
            email = author.Email,
            bio,
            avatar = author.Avatar,
            version = author.Version,
        });

    private Task<(HttpStatusCode Status, int Code, CollectionDetail? Data, string Message)> CreateCollectionAsync(
        string token, string description) =>
        _api.CallAsync<CollectionDetail>(HttpMethod.Post, "/api/collections", token, new
        {
            title = "长度校验专栏",
            slug = $"len-{Guid.NewGuid():N}",
            description,
            coverImage = "",
            sortOrder = 0,
            isPublished = false,
        });

    private Task<(HttpStatusCode Status, int Code, List<SocialLinkDetail>? Data, string Message)> SaveSocialLinkAsync(
        string token, string name) =>
        _api.CallAsync<List<SocialLinkDetail>>(HttpMethod.Put, "/api/site/social-links", token, new[]
        {
            new
            {
                id = (Guid?)null,
                name,
                icon = "rss",
                url = "https://example.com",
                sortOrder = 0,
                isVisible = true,
                version = (int?)null,
            },
        });
}

/// <summary>
/// 作者信息（只声明本测试用得到的字段）。
/// <c>AuthorDetail</c> 定义在 <c>MediaUrlValidationTests.cs</c>（同一命名空间，直接复用）——
/// 两个文件同处 <c>Blog.Tests.Integration</c>，重复定义会直接编译失败。
/// </summary>
public sealed record CollectionDetail(Guid Id, string Title, string Slug, string? Description, int Version);

/// <summary>社交链接（只声明本测试用得到的字段）</summary>
public sealed record SocialLinkDetail(Guid Id, string Name, string Icon, string Url);
