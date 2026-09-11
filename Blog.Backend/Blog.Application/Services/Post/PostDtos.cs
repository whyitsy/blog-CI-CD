namespace Blog.Application.Services.Post
{
    public record TagBriefDto(Guid Id, string Name);

    /// <summary>文章列表卡片数据（首页 / 标签 / 分类 / 搜索结果共用）</summary>
    public record PostCardDto(
        Guid Id,
        string Title,
        string Summary,
        string CoverImage,
        Guid? CategoryId,
        string? CategoryName,
        List<TagBriefDto> Tags,
        DateTimeOffset? PublishedAt,
        int ViewCount
    );

    /// <summary>文章详情（Version 为乐观锁版本号，写操作需回传）</summary>
    public record PostDetailDto(
        Guid Id,
        string Title,
        string Content,
        string Summary,
        string CoverImage,
        Guid? CategoryId,
        string? CategoryName,
        List<TagBriefDto> Tags,
        List<CollectionBriefDto> Collections,
        Guid AuthorId,
        string? AuthorName,
        string? AuthorAvatar,
        Guid? CreatedByUserId,
        DateTimeOffset? PublishedAt,
        DateTimeOffset? UpdatedAt,
        int ViewCount,
        int WordCount,
        int Version
    );

    /// <summary>专栏简要信息（用于文章详情/列表展示归属）</summary>
    public record CollectionBriefDto(Guid Id, string Title, string Slug);

    /// <summary>
    /// 创建文章。
    /// Summary：留空则自动取正文前 50 字，填写则以填写内容为准（见 docs/10-决策记录.md §2.2 / T4）。
    /// CollectionIds：所属专栏（一篇文章可属于多个专栏，T2），可选。
    /// AuthorId：署名作者，仅管理员可指定；作者身份登录时忽略（强制为自己）。
    /// </summary>
    public record CreatePostRequest(
        string Title,
        string Content,
        string? Summary,
        string? CoverImage,
        Guid? CategoryId,
        List<Guid>? TagIds,
        List<Guid>? CollectionIds,
        Guid? AuthorId,
        bool Publish = true
    );

    /// <summary>更新文章：Version 为乐观锁版本号，并发冲突时返回 409</summary>
    public record UpdatePostRequest(
        string Title,
        string Content,
        string? Summary,
        string? CoverImage,
        Guid? CategoryId,
        List<Guid>? TagIds,
        List<Guid>? CollectionIds,
        int Version
    );

    public record ArchiveItemDto(Guid Id, string Title, DateTimeOffset PublishedAt);

    public record ArchiveGroupDto(int Year, int Month, List<ArchiveItemDto> Items);

    /// <summary>文章列表查询参数</summary>
    public record PostQueryRequest
    {
        public int Page { get; init; } = 1;
        public int PageSize { get; init; } = 12;
        public Guid? CategoryId { get; init; }
        public Guid? TagId { get; init; }
        public Guid? CollectionId { get; init; }
        public Guid? AuthorId { get; init; }
        public string? Keyword { get; init; }

        /// <summary>true 时返回全部（含草稿），管理后台使用；默认 false（仅已发布）</summary>
        public bool IncludeUnpublished { get; init; } = false;

        /// <summary>
        /// 仅返回「我创建的」文章（作者工作区用）。
        /// 由服务层根据当前登录用户填充后强制过滤；为 null 表示不过滤。
        /// </summary>
        public Guid? OwnedByUserId { get; init; }
    }
}
