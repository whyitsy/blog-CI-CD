using Blog.Domain.Entities.Base;

namespace Blog.Domain.Entities
{
    /// <summary>
    /// 站点配置实体：Key-Value 形式存储站点级配置
    /// 约定的 Key 见 <see cref="SiteConfigKeys"/>
    /// </summary>
    public class SiteConfig : BaseEntity
    {
        public string Key { get; private set; } = string.Empty;

        /// <summary>配置值（简单字符串或 JSON），由应用层按 Key 解释</summary>
        public string Value { get; private set; } = string.Empty;

        public string? Description { get; private set; }

        private SiteConfig() { }

        public SiteConfig(string key, string value, string? description = null)
        {
            Key = key;
            Value = value;
            Description = description;
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public void Update(string value)
        {
            Value = value;
        }
    }

    /// <summary>约定使用的站点配置 Key</summary>
    public static class SiteConfigKeys
    {
        /// <summary>站点名称</summary>
        public const string SiteName = "SiteName";

        /// <summary>首屏副文本打字机内容（JSON 数组）</summary>
        public const string HeroSubtitles = "HeroSubtitles";

        /// <summary>建站日期（yyyy-MM-dd），用于 Footer 建站天数计算</summary>
        public const string FoundingDate = "FoundingDate";

        /// <summary>首页 Hero 背景图 URL（可选）</summary>
        public const string HeroBackground = "HeroBackground";
    }
}
