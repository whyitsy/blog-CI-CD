using Blog.Application.Common;

namespace Blog.Application.Services.Site
{
    public interface ISiteService
    {
        Task<SiteConfigDto> GetConfigAsync(CancellationToken cancellationToken = default);
        Task<SiteConfigDto> UpdateConfigAsync(UpdateSiteConfigRequest request, CancellationToken cancellationToken = default);
        Task<List<SocialLinkDto>> GetSocialLinksAsync(CancellationToken cancellationToken = default);
        Task<List<SocialLinkDto>> SaveSocialLinksAsync(List<UpsertSocialLinkRequest> request, CancellationToken cancellationToken = default);
        Task<SiteStatsDto> GetStatsAsync(CancellationToken cancellationToken = default);
    }
}
