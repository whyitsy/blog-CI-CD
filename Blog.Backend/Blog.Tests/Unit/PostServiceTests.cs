using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Interfaces;
using Blog.Application.Services.Post;
using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PostEntity = Blog.Domain.Entities.Post;

namespace Blog.Tests.Unit;

/// <summary>
/// PostService 的**单元测试**：只测「不与数据库/HTTP 绑定的业务规则」。
///
/// 分工说明（重要）：
///   - 权限矩阵、草稿保护的 HTTP 行为 → 集成测试（见 Integration/），因为 [Authorize]
///     与状态码 404/403/409 只有在完整管道里才有意义。
///   - 这里测的是**服务层自己的判定与编排**：非法入参的拒绝、归属校验、
///     乐观锁版本号校验、更新时的字段语义。
/// 两者互补，不重复。
/// </summary>
public sealed class PostServiceTests
{
    // ---------------------------------------------------------------- 非法入参

    [Theory]
    [InlineData("", "正文")]
    [InlineData("   ", "正文")]
    [InlineData(null!, "正文")]
    public async Task 创建_标题为空_应4001(string? title, string content)
    {
        var (service, _) = CreateService(isAuthenticated: true, userId: Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.CreateAsync(new CreatePostRequest(title!, content, null, null, null, null, null, null)));

        Assert.Equal(ErrorCodes.InvalidArgument, ex.Code);
    }

    [Fact]
    public async Task 创建_标题超200字_应4001()
    {
        var (service, _) = CreateService(isAuthenticated: true, userId: Guid.NewGuid());
        var longTitle = new string('标', 201);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.CreateAsync(new CreatePostRequest(longTitle, "正文", null, null, null, null, null, null)));

        Assert.Equal(ErrorCodes.InvalidArgument, ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task 创建_正文为空_应4001(string content)
    {
        var (service, _) = CreateService(isAuthenticated: true, userId: Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.CreateAsync(new CreatePostRequest("标题", content, null, null, null, null, null, null)));

        Assert.Equal(ErrorCodes.InvalidArgument, ex.Code);
    }

    [Fact]
    public async Task 创建_未登录_应4010()
    {
        var (service, _) = CreateService(isAuthenticated: false, userId: null);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.CreateAsync(new CreatePostRequest("标题", "正文", null, null, null, null, null, null)));

        Assert.Equal(ErrorCodes.Unauthorized, ex.Code);
    }

    // ---------------------------------------------------------------- 归属校验（服务层职责）

    [Fact]
    public async Task 更新_非管理员改他人文章_应4030()
    {
        var me = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();
        var post = NewPost(createdByUserId: someoneElse);
        var postId = post.Id;   // Id 是 init-only 且未显式赋值，每次读取生成的 Guid 不同，必须先固定

        var (service, mocks) = CreateService(isAuthenticated: true, userId: me, isAdmin: false);
        mocks.Posts.Setup(r => r.GetByIdAsync(postId, It.IsAny<CancellationToken>())).ReturnsAsync(post);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.UpdateAsync(postId, new UpdatePostRequest("新标题", "新正文", null, null, null, null, null, 1)));

        Assert.Equal(ErrorCodes.Forbidden, ex.Code);
    }

    [Fact]
    public async Task 更新_管理员可改他人文章()
    {
        var post = NewPost(createdByUserId: Guid.NewGuid());
        var postId = post.Id;

        var (service, mocks) = CreateService(isAuthenticated: true, userId: Guid.NewGuid(), isAdmin: true);
        mocks.Posts.Setup(r => r.GetByIdAsync(postId, It.IsAny<CancellationToken>())).ReturnsAsync(post);

        await service.UpdateAsync(postId, new UpdatePostRequest("新标题", "新正文", null, null, null, null, null, 1));

        // 走到这里即说明没有被归属校验拦下（持久化由 mock 承接）
        mocks.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------- 乐观锁版本号（服务层校验）

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task 更新_版本号非法_应4001(int version)
    {
        var post = NewPost(createdByUserId: null);
        var postId = post.Id;

        var (service, mocks) = CreateService(isAuthenticated: true, userId: Guid.NewGuid(), isAdmin: true);
        mocks.Posts.Setup(r => r.GetByIdAsync(postId, It.IsAny<CancellationToken>())).ReturnsAsync(post);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.UpdateAsync(postId, new UpdatePostRequest("新标题", "新正文", null, null, null, null, null, version)));

        Assert.Equal(ErrorCodes.InvalidArgument, ex.Code);
    }

    [Fact]
    public async Task 更新_文章不存在_应4040()
    {
        var (service, mocks) = CreateService(isAuthenticated: true, userId: Guid.NewGuid(), isAdmin: true);
        mocks.Posts.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((PostEntity?)null);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.UpdateAsync(Guid.NewGuid(), new UpdatePostRequest("t", "c", null, null, null, null, null, 1)));

        Assert.Equal(ErrorCodes.NotFound, ex.Code);
    }

    [Fact]
    public async Task 更新_标签id不合法_应4001()
    {
        var post = NewPost(createdByUserId: null);
        var postId = post.Id;

        var (service, mocks) = CreateService(isAuthenticated: true, userId: Guid.NewGuid(), isAdmin: true);
        mocks.Posts.Setup(r => r.GetByIdAsync(postId, It.IsAny<CancellationToken>())).ReturnsAsync(post);
        // 请求 2 个标签，仓储只返回 1 个 → 说明有不存在的标签
        mocks.Tags.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync([new Tag("存在的标签")]);

        var ex = await Assert.ThrowsAsync<BusinessException>(
            () => service.UpdateAsync(postId, new UpdatePostRequest("t", "c", null, null, null,
                [Guid.NewGuid(), Guid.NewGuid()], null, 1)));

        Assert.Equal(ErrorCodes.InvalidArgument, ex.Code);
    }

    // ---------------------------------------------------------------- 更新时的字段语义

    [Fact]
    public async Task 更新_摘要留空_自动按正文重新截取()
    {
        var post = NewPost(createdByUserId: null);
        var postId = post.Id;

        var (service, mocks) = CreateService(isAuthenticated: true, userId: Guid.NewGuid(), isAdmin: true);
        mocks.Posts.Setup(r => r.GetByIdAsync(postId, It.IsAny<CancellationToken>())).ReturnsAsync(post);

        var content = new string('正', 80);
        await service.UpdateAsync(postId, new UpdatePostRequest("标题", content, null, null, null, null, null, 1));

        Assert.Equal(content[..50], post.Summary);
    }

    [Fact]
    public async Task 更新_作者显式填写摘要_以填写内容为准()
    {
        var post = NewPost(createdByUserId: null);
        var postId = post.Id;

        var (service, mocks) = CreateService(isAuthenticated: true, userId: Guid.NewGuid(), isAdmin: true);
        mocks.Posts.Setup(r => r.GetByIdAsync(postId, It.IsAny<CancellationToken>())).ReturnsAsync(post);

        await service.UpdateAsync(postId,
            new UpdatePostRequest("标题", new string('正', 80), "我自己的摘要", null, null, null, null, 1));

        Assert.Equal("我自己的摘要", post.Summary);
    }

    // ---------------------------------------------------------------- 发布幂等

    [Fact]
    public void 重复发布_不覆盖首次发布时间()
    {
        var post = NewPost(createdByUserId: null);
        post.Publish();
        var first = post.PublishedAt;
        Assert.NotNull(first);

        post.Publish();
        Assert.Equal(first, post.PublishedAt);
    }

    // ---------------------------------------------------------------- 测试脚手架

    private sealed record Mocks(
        Mock<IPostQueryRepository> PostQuery,
        Mock<IPostRepository> Posts,
        Mock<ITagRepository> Tags,
        Mock<ICategoryRepository> Categories,
        Mock<IAuthorRepository> Authors,
        Mock<ICollectionRepository> Collections,
        Mock<IUnitOfWork> Uow,
        Mock<ICacheService> Cache);

    /// <summary>
    /// 构造被测服务。所有协作者都用 Moq；
    /// 缓存用直通实现（GetOrCreate 直接调用 factory），避免把缓存行为混进本组断言。
    /// </summary>
    private static (PostService Service, Mocks Mocks) CreateService(
        bool isAuthenticated, Guid? userId, bool isAdmin = false)
    {
        var mocks = new Mocks(
            new Mock<IPostQueryRepository>(),
            new Mock<IPostRepository>(),
            new Mock<ITagRepository>(),
            new Mock<ICategoryRepository>(),
            new Mock<IAuthorRepository>(),
            new Mock<ICollectionRepository>(),
            new Mock<IUnitOfWork>(),
            new Mock<ICacheService>());

        // 缓存直通：不缓存、直接回源，让断言只关注业务逻辑
        mocks.Cache
            .Setup(c => c.GetOrCreateAsync(
                It.IsAny<string>(),
                It.IsAny<Func<CancellationToken, Task<PostDetailDto?>>>(),
                It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, Task<PostDetailDto?>> f, TimeSpan _, bool _, CancellationToken ct) => f(ct));

        // 默认：按 id 找不到资源（各测试按需覆盖）
        mocks.Tags.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync([]);
        mocks.Authors.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        mocks.Collections.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                         .ReturnsAsync([]);

        // 写操作成功后服务会**回读**详情（LoadDetailOrThrowAsync）。
        // 不 stub 会返回 null 并被当作「文章不存在」而抛 404，掩盖真正要断言的行为。
        mocks.PostQuery
            .Setup(q => q.GetDetailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new PostDetailDto(
                id, "回读标题", "回读正文", "回读摘要", "", null, null, [], [], Guid.Empty, null, null,
                null, null, null, 0, 0, 1));

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(u => u.IsAuthenticated).Returns(isAuthenticated);
        currentUser.SetupGet(u => u.UserId).Returns(userId);
        currentUser.SetupGet(u => u.Role).Returns(isAdmin ? UserRole.Admin : UserRole.Author);
        currentUser.SetupGet(u => u.IsAdmin).Returns(isAdmin);

        var service = new PostService(
            mocks.PostQuery.Object, mocks.Posts.Object, mocks.Tags.Object, mocks.Categories.Object,
            mocks.Authors.Object, mocks.Collections.Object, mocks.Uow.Object, mocks.Cache.Object,
            currentUser.Object);

        return (service, mocks);
    }

    /// <summary>构造一篇文章（AuthorId 可空，按实体公开构造函数创建）</summary>
    private static PostEntity NewPost(Guid? createdByUserId) =>
        new("初始标题", "初始正文", summary: null, authorId: null, categoryId: null,
            coverImage: "", createdByUserId: createdByUserId, publishNow: false);
}
