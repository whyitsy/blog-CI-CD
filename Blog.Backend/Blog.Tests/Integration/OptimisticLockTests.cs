using System.Net;
using Blog.Tests.Infrastructure;

namespace Blog.Tests.Integration;

/// <summary>
/// 乐观锁集成测试。
///
/// 本项目所有可写实体都带 <c>int Version</c>，更新时必须回传当前版本；
/// 不匹配时后端生成 <c>UPDATE ... WHERE Id=@id AND Version=@expected</c>，
/// 0 行受影响 → DbUpdateConcurrencyException → **409 / code 4090**。
///
/// 这是贯穿全栈的约定（前端详情页必须保存 version 并原样回传），
/// 出错会影响所有编辑功能，因此必须有测试守住「旧版本号被拒绝」这一行为。
/// 同时验证边界：缺失/非法版本号应返回 4001（参数错误），而不是 409。
/// </summary>
[Collection(BlogApiCollection.Name)]
public sealed class OptimisticLockTests
{
    private readonly ApiClient _api;

    public OptimisticLockTests(BlogApiFixture fixture) => _api = new ApiClient(fixture.CreateClient());

    [Fact]
    public async Task 用过期版本更新文章_应409()
    {
        var token = await _api.LoginAsync("admin@example.com", "Admin@12345");

        // 1) 建一篇并拿到初始 version
        var created = await CreatePostAsync(token, "乐观锁-过期版本");
        var v1 = created.Version;
        Assert.True(v1 >= 1, $"新建文章的 version 应 >= 1，实际 {v1}");

        // 2) 用正确的版本更新一次 → 成功，版本自增
        var firstUpdate = await UpdateAsync(token, created.Id, v1, "乐观锁-第一次更新");
        Assert.Equal(HttpStatusCode.OK, firstUpdate.Status);
        Assert.Equal(Codes.Ok, firstUpdate.Code);
        var v2 = firstUpdate.Data!.Version;
        Assert.Equal(v1 + 1, v2);

        // 3) 再用**已经过期**的 v1 更新 → 必须 409
        var stale = await UpdateAsync(token, created.Id, v1, "乐观锁-用过期的 v1");
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal(Codes.ConcurrencyConflict, stale.Code);
    }

    [Fact]
    public async Task 缺失或非法版本号_应4001而不是409()
    {
        var token = await _api.LoginAsync("admin@example.com", "Admin@12345");
        var created = await CreatePostAsync(token, "乐观锁-非法版本");

        // 0 / 负数版本号属于**参数错误**，应与「并发冲突」区分开
        foreach (var badVersion in new[] { 0, -1 })
        {
            var res = await UpdateAsync(token, created.Id, badVersion, "乐观锁-非法版本");
            Assert.Equal(HttpStatusCode.BadRequest, res.Status);
            Assert.Equal(Codes.InvalidArgument, res.Code);
        }
    }

    [Fact]
    public async Task 发布与下架_也需要版本号()
    {
        var token = await _api.LoginAsync("admin@example.com", "Admin@12345");
        var created = await CreatePostAsync(token, "乐观锁-发布");

        // 用正确版本下架 → 成功
        var unpublish = await _api.CallAsync<PostDetail>(
            HttpMethod.Post, $"/api/posts/{created.Id}/publish?version={created.Version}&publish=false", token);
        Assert.Equal(HttpStatusCode.OK, unpublish.Status);
        Assert.Null(unpublish.Data!.PublishedAt);

        // 用过期版本再操作 → 409
        var stale = await _api.CallAsync<PostDetail>(
            HttpMethod.Post, $"/api/posts/{created.Id}/publish?version={created.Version}&publish=true", token);
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal(Codes.ConcurrencyConflict, stale.Code);
    }

    [Fact]
    public async Task 删除_也需要版本号_过期版本应409()
    {
        var token = await _api.LoginAsync("admin@example.com", "Admin@12345");
        var created = await CreatePostAsync(token, "乐观锁-删除");

        // 先更新一次，让 created.Version 过期
        var updated = await UpdateAsync(token, created.Id, created.Version, "乐观锁-删除-先更新");
        Assert.Equal(HttpStatusCode.OK, updated.Status);

        var stale = await _api.CallAsync<object>(
            HttpMethod.Delete, $"/api/posts/{created.Id}?version={created.Version}", token);
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal(Codes.ConcurrencyConflict, stale.Code);

        // 用最新版本删除 → 成功
        var ok = await _api.CallAsync<object>(
            HttpMethod.Delete, $"/api/posts/{created.Id}?version={updated.Data!.Version}", token);
        Assert.Equal(HttpStatusCode.OK, ok.Status);
    }

    [Fact]
    public async Task 站点配置_不存在时视为新增_可接受version0()
    {
        var token = await _api.LoginAsync("admin@example.com", "Admin@12345");

        // 统一的写入语义：Key 不存在 = 新增（忽略 version）/ 已存在 = 乐观锁（version 必须 >= 1）
        // 注意 PUT /api/site/config 返回的是**整个聚合 DTO**（SiteConfigDto），不是单个配置项。
        var key = "HeroBackground";   // 种子数据里**没有**这个 Key，因此首次写入走「新增」

        var created = await _api.CallAsync<object>(
            HttpMethod.Put, "/api/site/config", token,
            new { key, value = "/api/files/test-bg.webp", version = 0 });

        Assert.Equal(HttpStatusCode.OK, created.Status);
        Assert.Equal(Codes.Ok, created.Code);

        // 已存在后再用 0 更新 → 4001（缺少合法版本号）
        var badVersion = await _api.CallAsync<object>(
            HttpMethod.Put, "/api/site/config", token,
            new { key, value = "/api/files/test-bg2.webp", version = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, badVersion.Status);
        Assert.Equal(Codes.InvalidArgument, badVersion.Code);
    }

    // ---------------------------------------------------------------- 辅助

    private async Task<PostDetail> CreatePostAsync(string token, string title)
    {
        var (status, code, data, message) = await _api.CallAsync<PostDetail>(
            HttpMethod.Post, "/api/posts", token, new
            {
                title,
                content = $"正文：{title}",
                summary = (string?)null,
                coverImage = "",
                categoryId = (Guid?)null,
                tagIds = Array.Empty<Guid>(),
                collectionIds = Array.Empty<Guid>(),
                authorId = (Guid?)null,
                publish = true,
            });

        Assert.True(status == HttpStatusCode.OK && code == Codes.Ok,
            $"创建文章失败 status={status} code={code} message={message}");
        return data!;
    }

    private Task<(HttpStatusCode Status, int Code, PostDetail? Data, string Message)> UpdateAsync(
        string token, Guid id, int version, string title) =>
        _api.CallAsync<PostDetail>(HttpMethod.Put, $"/api/posts/{id}", token, new
        {
            title,
            content = $"正文：{title}",
            summary = (string?)null,
            coverImage = "",
            categoryId = (Guid?)null,
            tagIds = Array.Empty<Guid>(),
            collectionIds = Array.Empty<Guid>(),
            version,
        });
}

