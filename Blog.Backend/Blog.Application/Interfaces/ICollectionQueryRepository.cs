using Blog.Application.Services.Collection;

namespace Blog.Application.Interfaces
{
    /// <summary>专栏的只读查询（投影到 DTO，避免把实体带进应用层）</summary>
    public interface ICollectionQueryRepository
    {
        /// <summary>
        /// 专栏列表。
        /// <paramref name="includeUnpublished"/> 为 false 时只返回 IsPublished 的专栏（前台）；
        /// true 时返回全部（管理端）。PostCount 只统计**已发布**文章。
        /// </summary>
        Task<List<CollectionDto>> GetAllAsync(bool includeUnpublished, CancellationToken cancellationToken = default);

        /// <summary>按 slug 取详情（含专栏内已发布文章，按专栏内 SortOrder）</summary>
        Task<CollectionDetailDto?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

        /// <summary>按 id 取详情（管理端用，含未发布文章）</summary>
        Task<CollectionDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
