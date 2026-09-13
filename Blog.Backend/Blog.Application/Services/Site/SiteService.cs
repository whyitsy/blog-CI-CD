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
                // Logo 文字缺省 "k"，与改造前写死的那个字符保持一致
                dict.TryGetValue(SiteConfigKeys.LogoName, out var logoName) && !string.IsNullOrWhiteSpace(logoName)
                    ? logoName.Trim()
                    : "k",
                NullIfEmpty(dict.GetValueOrDefault(SiteConfigKeys.SiteLogo)),
                ParseSubtitles(dict.GetValueOrDefault(SiteConfigKeys.HeroSubtitles)),
                // 读取侧宽容：历史上这里存的是单个裸地址，MediaPath.ParseList 会把它当成单元素列表
                MediaPath.ParseList(dict.GetValueOrDefault(SiteConfigKeys.HeroBackground)),
                DateTimeOffset.TryParse(dict.GetValueOrDefault(SiteConfigKeys.FoundingDate), out var founding)
                    ? founding : null,
                items.ToDictionary(i => i.Key, i => i.Version, StringComparer.OrdinalIgnoreCase));
        }

        private static string? NullIfEmpty(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>
        /// 写入单个配置项：Key 尚未存在时按新增处理（忽略 version），已存在时走乐观锁。
        /// 已存在的项若 version 非法会给出 4001，避免仓储抛参数异常而变成 5000。
        /// </summary>
        public async Task<SiteConfigDto> UpdateConfigAsync(UpdateSiteConfigRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Key))
                throw new BusinessException("配置项 Key 不能为空", ErrorCodes.InvalidArgument);
            FieldLimits.EnsureLength(request.Key, FieldLimits.SiteConfigKey, "配置项 Key");

            var value = NormalizeConfigValue(request.Key, request.Value);

            var config = await _configs.GetByKeyAsync(request.Key, cancellationToken);

            if (config is null)
            {
                config = new SiteConfig(request.Key, value);
                await _configs.AddAsync(config, cancellationToken);
            }
            else
            {
                if (request.Version < 1)
                    throw new BusinessException("缺少合法的版本号，无法进行并发控制", ErrorCodes.InvalidArgument);

                _configs.ApplyOptimisticVersion(config, request.Version);
                config.Update(value);
            }

            await _uow.SaveChangesAsync(cancellationToken);
            await _cache.RemoveAsync(CacheKeys.SiteConfig, cancellationToken);

            return await GetConfigAsync(cancellationToken);
        }

        /// <summary>
        /// 媒体类配置项在写库前必须过 <see cref="MediaPath"/> 白名单——这些值最终都会进
        /// <c>&lt;img src&gt;</c>，与作者头像、文章封面是同一类风险，不能因为「只有管理员能改」
        /// 就放行（管理员账号被盗是常见的攻击路径）。
        /// 非媒体类配置项原样返回。
        /// </summary>
        private static string NormalizeConfigValue(string key, string value)
        {
            if (key.Equals(SiteConfigKeys.SiteLogo, StringComparison.OrdinalIgnoreCase))
                return MediaPath.Validate(value, "站点 Logo");

            if (key.Equals(SiteConfigKeys.HeroBackground, StringComparison.OrdinalIgnoreCase))
                return MediaPath.ValidateJsonList(value, "首屏背景图");

            return value;
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

                // 长度校验：Name/Icon/Url 在库里分别是 varchar(50)/(50)/(500)，
                // 前端这三个输入框**没有 maxlength**，不校验就是又一条「500 而不是 4001」的路
                FieldLimits.EnsureLength(item.Name, FieldLimits.SocialLinkName, "社交链接名称");
                FieldLimits.EnsureLength(item.Icon, FieldLimits.SocialLinkIcon, "社交链接图标");
                FieldLimits.EnsureLength(item.Url, FieldLimits.SocialLinkUrl, "社交链接地址");

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
