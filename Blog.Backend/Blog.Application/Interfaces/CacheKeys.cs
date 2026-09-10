using Blog.Application.Services.Post;
using Blog.Application.Services.Site;

namespace Blog.Application.Interfaces
{
    /// <summary>
    /// 缓存 key 规范：blog:{模块}:{操作}:v{版本}:{固定维度}:{可选hash}
    /// 详见 docs/backend.md §4.2 与 docs/tech.md §4.4。
    ///
    /// 约定：
    ///   - 固定前缀 blog:，便于前缀失效，也避免与其他应用共用 Redis 时冲突
    ///   - 模块 + 操作 定位业务语义（不单独再放「实体」段，避免 posts:post: 这类冗余）
    ///   - v{版本} 用于「缓存结构不兼容变更」时整体失效：改版本号即可，
    ///     旧 key 无人访问、靠 TTL 自然消失，无需 SCAN 清理
    ///   - 固定维度用短键名，缺省值用 - 占位，保持段数固定便于排查
    ///   - 不可枚举的长文本（如关键词）做 SHA256 取前 16 位
    /// </summary>
    public static class CacheKeys
    {
        private const string Prefix = "blog:";

        /// <summary>缓存结构版本。DTO 结构发生不兼容变更时 +1</summary>
        private const int Version = 1;

        public const string PostsPrefix = Prefix + "posts:";
        public const string SitePrefix = Prefix + "site:";
        public const string TaxonomyPrefix = Prefix + "taxonomy:";
        public const string StatsPrefix = Prefix + "stats:";
        public const string AuthPrefix = Prefix + "auth:";

        // ---------------------------------------------------------------- posts

        public static string PostList(PostQueryRequest query) =>
            $"{PostsPrefix}list:v{Version}" +
            $":p{query.Page}s{query.PageSize}" +
            $":c{Dash(query.CategoryId)}t{Dash(query.TagId)}" +
            $":col{Dash(query.CollectionId)}a{Dash(query.AuthorId)}o{Dash(query.OwnedByUserId)}" +
            $":k{Hash(query.Keyword)}" +
            $":u{(query.IncludeUnpublished ? 1 : 0)}";

        public static string PostDetail(Guid id) => $"{PostsPrefix}detail:v{Version}:{id}";

        public const string PostArchives = PostsPrefix + "archives:v1:-";

        // ---------------------------------------------------------------- taxonomy

        public static string Categories => $"{TaxonomyPrefix}categories:v{Version}:-";

        public static string Tags => $"{TaxonomyPrefix}tags:v{Version}:-";

        public static string Collections => $"{TaxonomyPrefix}collections:v{Version}:-";

        public static string CollectionDetail(string slug) => $"{TaxonomyPrefix}collection:v{Version}:{slug}";

        // ---------------------------------------------------------------- site

        public static string SiteConfig => $"{SitePrefix}config:v{Version}:-";

        /// <summary>公开首屏：仅可见社交链接</summary>
        public static string SiteSocialLinks => $"{SitePrefix}social:v{Version}:visible";

        /// <summary>管理端配置页：含隐藏项，与公开缓存分离</summary>
        public static string SiteSocialLinksAll => $"{SitePrefix}social:v{Version}:all";

        public static string SiteStats => $"{StatsPrefix}summary:v{Version}:-";

        // ---------------------------------------------------------------- auth

        /// <summary>账号信息缓存（TokenVersion 校验路径用）。key 为账号 id</summary>
        public static string UserById(Guid userId) => $"{AuthPrefix}user:v{Version}:{userId}";

        // ---------------------------------------------------------------- helpers

        /// <summary>缺省维度用 - 占位，保持 key 段数固定</summary>
        private static string Dash(Guid? value) => value?.ToString() ?? "-";

        /// <summary>关键词做摘要，避免超长或特殊字符污染 key</summary>
        private static string Hash(string? keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return "-";

            var bytes = System.Text.Encoding.UTF8.GetBytes(keyword.Trim());
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
        }
    }
}
