using Blog.Domain.Entities.Base;

namespace Blog.Domain.Entities
{
    /// <summary>
    /// 社交链接实体：首屏底部的 Github、Bilibili 等图标
    /// </summary>
    public class SocialLink : BaseEntity
    {
        public string Name { get; private set; } = string.Empty;

        /// <summary>图标标识（前端图标 key，如 github / bilibili）</summary>
        public string Icon { get; private set; } = string.Empty;

        public string Url { get; private set; } = string.Empty;

        /// <summary>排序序号，越小越靠前</summary>
        public int SortOrder { get; private set; }

        public bool IsVisible { get; private set; }

        // 导航属性（保留扩展空间）
        private SocialLink() { }

        public SocialLink(string name, string icon, string url, int sortOrder = 0, bool isVisible = true)
        {
            Name = name;
            Icon = icon;
            Url = url;
            SortOrder = sortOrder;
            IsVisible = isVisible;
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public void Update(string name, string icon, string url, int sortOrder, bool isVisible)
        {
            Name = name;
            Icon = icon;
            Url = url;
            SortOrder = sortOrder;
            IsVisible = isVisible;
        }
    }
}
