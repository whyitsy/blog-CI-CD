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
        Guid AuthorId,
        string? AuthorName,
        string? AuthorAvatar,
        DateTimeOffset? PublishedAt,
        DateTimeOffset? UpdatedAt,
        int ViewCount,
        int WordCount,
        int Version
    );

    public record CreatePostRequest(
        string Title,
        string Content,
        string? CoverImage,
        Guid? CategoryId,
        List<Guid>? TagIds,
        bool Publish = true
    );

    /// <summary>更新文章：Version 为乐观锁版本号，并发冲突时返回 409</summary>
    public record UpdatePostRequest(
        string Title,
        string Content,
        string? CoverImage,
        Guid? CategoryId,
        List<Guid>? TagIds,
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
        public string? Keyword { get; init; }
        /// <summary>true 时返回全部（含草稿），管理后台使用；默认 false（仅已发布）</summary>
        public bool IncludeUnpublished { get; init; } = false;
    }
}
