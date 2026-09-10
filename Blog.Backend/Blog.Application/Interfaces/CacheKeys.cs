using Blog.Application.Services.Post;
using Blog.Application.Services.Site;

namespace Blog.Application.Interfaces
{
    /// <summary>缓存 key 规范：blog:{module}:{query}</summary>
    public static class CacheKeys
    {
        private const string Prefix = "blog:";

        public const string PostsPrefix = Prefix + "posts:";
        public const string SitePrefix = Prefix + "site:";

        public static string PostList(PostQueryRequest query) =>
            $"{PostsPrefix}list:p{query.Page}s{query.PageSize}c{query.CategoryId}t{query.TagId}k{Hash(query.Keyword)}u{(query.IncludeUnpublished ? 1 : 0)}";

        public static string PostDetail(Guid id) => $"{PostsPrefix}detail:{id}";

        public const string PostArchives = PostsPrefix + "archives";

        public const string Categories = Prefix + "categories";
        public const string Tags = Prefix + "tags";

        public const string SiteConfig = SitePrefix + "config";
        public const string SiteSocialLinks = SitePrefix + "social";
        /// <summary>管理端配置页使用：含隐藏项，与公开首屏的缓存分离</summary>
        public const string SiteSocialLinksAll = SitePrefix + "social:all";
        public const string SiteStats = SitePrefix + "stats";

        /// <summary>关键词做摘要，避免超长或特殊字符污染 key</summary>
        private static string Hash(string? keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return "-";

            var bytes = System.Text.Encoding.UTF8.GetBytes(keyword.Trim());
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..16];
        }
    }
}
