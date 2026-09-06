using Blog.Application.Services.Site;

namespace Blog.Application.Interfaces
{
    /// <summary>站点配置与统计的只读查询</summary>
    public interface ISiteQueryRepository
    {
        Task<List<SiteConfigItemDto>> GetAllConfigsAsync(CancellationToken cancellationToken = default);

        Task<List<SocialLinkDto>> GetVisibleSocialLinksAsync(CancellationToken cancellationToken = default);

        Task<SiteStatsDto> GetStatsAsync(DateTimeOffset? foundingDate, CancellationToken cancellationToken = default);
    }
}
