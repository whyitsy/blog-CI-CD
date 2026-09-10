namespace Blog.Application.Services.Collection
{
    /// <summary>专栏列表项（含文章数，Version 为乐观锁版本号）</summary>
    public record CollectionDto(
        Guid Id,
        string Title,
        string Slug,
        string Description,
        string CoverImage,
        int SortOrder,
        bool IsPublished,
        int PostCount,
        int Version
    );

    /// <summary>
    /// 专栏详情：在列表项基础上带出该专栏下的文章（只含已发布，按专栏内排序）。
    /// </summary>
    public record CollectionDetailDto(
        Guid Id,
        string Title,
        string Slug,
        string Description,
        string CoverImage,
        int SortOrder,
        bool IsPublished,
        int PostCount,
        int Version,
        List<CollectionPostItemDto> Posts
    );

    /// <summary>专栏内的文章条目（按 PostCollection.SortOrder 排序）</summary>
    public record CollectionPostItemDto(
        Guid Id,
        string Title,
        string Summary,
        string CoverImage,
        DateTimeOffset? PublishedAt,
        int ViewCount,
        int SortOrder
    );

    public record CreateCollectionRequest(
        string Title,
        string Slug,
        string? Description,
        string? CoverImage,
        int SortOrder,
        bool IsPublished
    );

    /// <summary>更新专栏：Version 为乐观锁版本号，并发冲突返回 409</summary>
    public record UpdateCollectionRequest(
        string Title,
        string Slug,
        string? Description,
        string? CoverImage,
        int SortOrder,
        bool IsPublished,
        int Version
    );

    /// <summary>设置专栏内文章及其顺序（整体覆盖该专栏的文章集合）</summary>
    public record SetCollectionPostsRequest(
        List<Guid> PostIds,
        int Version
    );
}
