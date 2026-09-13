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

        /// <summary>
        /// 导航栏 Logo 圆点里的文字（1~2 个字符，缺省 "k"）。
        /// 与 <see cref="SiteName"/> **刻意分开**：站点名可能是「kky's blog」这样一句话，
        /// 而圆点里只放得下一个字符，两者共用会互相牵制。
        /// </summary>
        public const string LogoName = "LogoName";

        /// <summary>自定义 Logo 图片地址（本站上传）。为空则退化为「渐变圆点 + LogoName」文字样式</summary>
        public const string SiteLogo = "SiteLogo";

        /// <summary>首屏副文本打字机内容（JSON 数组）</summary>
        public const string HeroSubtitles = "HeroSubtitles";

        /// <summary>建站日期（yyyy-MM-dd），用于 Footer 建站天数计算</summary>
        public const string FoundingDate = "FoundingDate";

        /// <summary>
        /// 首页 Hero 背景图（JSON 数组）。可配置多张，每次进入首屏随机展示一张；
        /// 为空则前端使用内置渐变背景。
        /// <para>Key 名字保持单数是为了不改动已存在的配置行（Value 由裸地址升级为 JSON 数组，
        /// 读取侧对裸地址仍然兼容，见 MediaPath.ParseList）。</para>
        /// </summary>
        public const string HeroBackground = "HeroBackground";
    }
}
