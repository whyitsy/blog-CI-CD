using Blog.Application.Services.Site;

namespace Blog.Application.Interfaces
{
    /// <summary>站点配置与统计的只读查询</summary>
    public interface ISiteQueryRepository
    {
        Task<List<SiteConfigItemDto>> GetAllConfigsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// 社交链接查询。<paramref name="includeHidden"/> 为 true 时返回全部（管理端），
        /// 否则仅返回 IsVisible（公开首屏）。
        /// </summary>
        Task<List<SocialLinkDto>> GetSocialLinksAsync(bool includeHidden, CancellationToken cancellationToken = default);

        Task<SiteStatsDto> GetStatsAsync(DateTimeOffset? foundingDate, CancellationToken cancellationToken = default);
    }
}
