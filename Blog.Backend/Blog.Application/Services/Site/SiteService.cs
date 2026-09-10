using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Interfaces;
using Blog.Domain.Entities;
using Blog.Domain.IRepository;
using System.Text.Json;

namespace Blog.Application.Services.Site
{
    public class SiteService : ISiteService
    {
        private static readonly TimeSpan ConfigTtl = TimeSpan.FromHours(1);
        private static readonly TimeSpan StatsTtl = TimeSpan.FromMinutes(10);

        private readonly ISiteConfigRepository _configs;
        private readonly ISocialLinkRepository _socialLinks;
        private readonly ISiteQueryRepository _siteQuery;
        private readonly IUnitOfWork _uow;
        private readonly ICacheService _cache;

        public SiteService(
            ISiteConfigRepository configs,
            ISocialLinkRepository socialLinks,
            ISiteQueryRepository siteQuery,
            IUnitOfWork uow,
            ICacheService cache)
        {
            _configs = configs;
            _socialLinks = socialLinks;
            _siteQuery = siteQuery;
            _uow = uow;
            _cache = cache;
        }

        public async Task<SiteConfigDto> GetConfigAsync(CancellationToken cancellationToken = default)
        {
            var items = await _cache.GetOrCreateAsync(
                CacheKeys.SiteConfig,
                ct => _siteQuery.GetAllConfigsAsync(ct),
                ConfigTtl,
                cacheNull: true,
                cancellationToken) ?? [];

            var dict = items.ToDictionary(i => i.Key, i => i.Value, StringComparer.OrdinalIgnoreCase);

            return new SiteConfigDto(
                dict.TryGetValue(SiteConfigKeys.SiteName, out var name) ? name : "My Blog",
                ParseSubtitles(dict.GetValueOrDefault(SiteConfigKeys.HeroSubtitles)),
                dict.GetValueOrDefault(SiteConfigKeys.HeroBackground),
                DateTimeOffset.TryParse(dict.GetValueOrDefault(SiteConfigKeys.FoundingDate), out var founding)
                    ? founding : null,
                items.ToDictionary(i => i.Key, i => i.Version, StringComparer.OrdinalIgnoreCase));
        }

        public async Task<SiteConfigDto> UpdateConfigAsync(UpdateSiteConfigRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Key))
                throw new BusinessException("配置项 Key 不能为空", ErrorCodes.InvalidArgument);

            var config = await _configs.GetByKeyAsync(request.Key, cancellationToken);

            if (config is null)
            {
                config = new SiteConfig(request.Key, request.Value);
                await _configs.AddAsync(config, cancellationToken);
            }
            else
            {
                _configs.ApplyOptimisticVersion(config, request.Version);
                config.Update(request.Value);
            }

            await _uow.SaveChangesAsync(cancellationToken);
            await _cache.RemoveAsync(CacheKeys.SiteConfig, cancellationToken);

            return await GetConfigAsync(cancellationToken);
        }

        public async Task<List<SocialLinkDto>> GetSocialLinksAsync(bool includeHidden = false, CancellationToken cancellationToken = default)
        {
            var cacheKey = includeHidden ? CacheKeys.SiteSocialLinksAll : CacheKeys.SiteSocialLinks;

            var links = await _cache.GetOrCreateAsync(
                cacheKey,
                ct => _siteQuery.GetSocialLinksAsync(includeHidden, ct),
                ConfigTtl,
                cacheNull: true,
                cancellationToken);

            return links ?? [];
        }

        public async Task<List<SocialLinkDto>> SaveSocialLinksAsync(List<UpsertSocialLinkRequest> request, CancellationToken cancellationToken = default)
        {
            var ordered = request
                .Select((item, index) => (item, index))
                .OrderBy(x => x.item.SortOrder).ThenBy(x => x.index)
                .Select(x => x.item)
                .ToList();

            foreach (var item in ordered)
            {
                if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Url))
                    throw new BusinessException("社交链接的名称与地址不能为空", ErrorCodes.InvalidArgument);

                SocialLink link;
                if (item.Id.HasValue)
                {
                    link = await _socialLinks.GetByIdAsync(item.Id.Value, cancellationToken)
                        ?? throw new BusinessException("社交链接不存在", ErrorCodes.NotFound);

                    _socialLinks.ApplyOptimisticVersion(link, item.Version
                        ?? throw new BusinessException("更新社交链接时必须携带 version 进行并发控制", ErrorCodes.InvalidArgument));

                    link.Update(item.Name, item.Icon, item.Url, item.SortOrder, item.IsVisible);
                }
                else
                {
                    link = new SocialLink(item.Name, item.Icon, item.Url, item.SortOrder);
                    await _socialLinks.AddAsync(link, cancellationToken);
                }
            }

            await _uow.SaveChangesAsync(cancellationToken);
            await _cache.RemoveAsync(CacheKeys.SiteSocialLinks, cancellationToken);
            await _cache.RemoveAsync(CacheKeys.SiteSocialLinksAll, cancellationToken);

            return await GetSocialLinksAsync(includeHidden: true, cancellationToken);
        }

        public async Task DeleteSocialLinkAsync(Guid id, int version, CancellationToken cancellationToken = default)
        {
            if (version < 1)
                throw new BusinessException("缺少合法的版本号，无法进行并发控制", ErrorCodes.InvalidArgument);

            var link = await _socialLinks.GetByIdAsync(id, cancellationToken)
                ?? throw new BusinessException("社交链接不存在", ErrorCodes.NotFound);

            _socialLinks.ApplyOptimisticVersion(link, version);
            _socialLinks.Remove(link);
            await _uow.SaveChangesAsync(cancellationToken);

            await _cache.RemoveAsync(CacheKeys.SiteSocialLinks, cancellationToken);
            await _cache.RemoveAsync(CacheKeys.SiteSocialLinksAll, cancellationToken);
        }

        public async Task<SiteStatsDto> GetStatsAsync(CancellationToken cancellationToken = default)
        {
            var config = await GetConfigAsync(cancellationToken);
            var configuredFoundingDate = config.FoundingDate;

            var stats = await _cache.GetOrCreateAsync(
                CacheKeys.SiteStats,
                ct => _siteQuery.GetStatsAsync(configuredFoundingDate, ct),
                StatsTtl,
                cacheNull: true,
                cancellationToken);

            return stats ?? new SiteStatsDto(0, 0, 0, 0, 0, 0);
        }

        private static List<string> ParseSubtitles(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return [];

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }
}
