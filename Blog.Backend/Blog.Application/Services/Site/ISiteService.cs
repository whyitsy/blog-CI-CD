using Blog.Application.Common;

namespace Blog.Application.Services.Site
{
    public interface ISiteService
    {
        Task<SiteConfigDto> GetConfigAsync(CancellationToken cancellationToken = default);
        Task<SiteConfigDto> UpdateConfigAsync(UpdateSiteConfigRequest request, CancellationToken cancellationToken = default);
        /// <summary>社交链接列表。<paramref name="includeHidden"/> 为 true 时含隐藏项（管理端配置页）</summary>
        Task<List<SocialLinkDto>> GetSocialLinksAsync(bool includeHidden = false, CancellationToken cancellationToken = default);
        Task<List<SocialLinkDto>> SaveSocialLinksAsync(List<UpsertSocialLinkRequest> request, CancellationToken cancellationToken = default);

        /// <summary>删除社交链接（软删除，乐观锁：必须携带当前 Version）</summary>
        Task DeleteSocialLinkAsync(Guid id, int version, CancellationToken cancellationToken = default);
        Task<SiteStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);
    }
}
