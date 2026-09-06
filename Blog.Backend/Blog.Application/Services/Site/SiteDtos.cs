namespace Blog.Application.Services.Site
{
    /// <summary>站点配置聚合结果（面向前端首屏与 Footer）</summary>
    public record SiteConfigDto(
        string SiteName,
        List<string> HeroSubtitles,
        string? HeroBackground,
        DateTimeOffset? FoundingDate
    );

    /// <summary>Version 为乐观锁版本号</summary>
    public record SiteConfigItemDto(string Key, string Value, int Version);

    public record UpdateSiteConfigRequest(string Key, string Value, int Version);

    public record SocialLinkDto(Guid Id, string Name, string Icon, string Url, int SortOrder, bool IsVisible, int Version);

    /// <summary>新增时 Id/Version 传 null；更新时必须携带当前 Version 做并发控制</summary>
    public record UpsertSocialLinkRequest(Guid? Id, string Name, string Icon, string Url, int SortOrder, bool IsVisible, int? Version);

    /// <summary>Footer 展示的站点统计</summary>
    public record SiteStatsDto(
        int SiteDays,
        int TotalPosts,
        long TotalWords,
        int TotalViews,
        int TagCount,
        int CategoryCount
    );
}
