using Blog.Domain.Entities.Base;

namespace Blog.Domain.Entities
{
    /// <summary>
    /// 专栏：把多篇文章组织成一个系列。
    /// 与 <see cref="Category"/> 的区别：分类是「广度」上的单值归类，专栏是「深度」上的系列组织，
    /// 且一篇文章可属于多个专栏（多对多，见 docs/02-架构与数据模型.md §5.6 / T2）。
    /// </summary>
    public class Collection : BaseEntity
    {
        public string Title { get; private set; } = string.Empty;

        /// <summary>URL 友好标识（唯一），用于 /collections/{slug}</summary>
        public string Slug { get; private set; } = string.Empty;

        public string Description { get; private set; } = string.Empty;

        public string CoverImage { get; private set; } = string.Empty;

        /// <summary>专栏之间的展示排序，越小越靠前</summary>
        public int SortOrder { get; private set; }

        /// <summary>是否对外可见（草稿状态的专栏隐藏）</summary>
        public bool IsPublished { get; private set; }

        // 导航属性：多对多连接
        public ICollection<PostCollection> PostLinks { get; set; } = [];

        private Collection() { } // EF Core 需要无参构造函数

        public Collection(string title, string slug, string description, string coverImage,
            int sortOrder = 0, bool isPublished = true)
        {
            Title = title;
            Slug = slug;
            Description = description;
            CoverImage = coverImage;
            SortOrder = sortOrder;
            IsPublished = isPublished;
            CreatedAt = DateTimeOffset.UtcNow;
            IsDeleted = false;
        }

        public void Update(string title, string slug, string description, string coverImage,
            int sortOrder, bool isPublished)
        {
            Title = title;
            Slug = slug;
            Description = description;
            CoverImage = coverImage;
            SortOrder = sortOrder;
            IsPublished = isPublished;
        }
    }

    /// <summary>
    /// 文章↔专栏 的连接实体。
    /// 相比 EF 隐式多对多，这里用显式连接实体是为了能带 <see cref="SortOrder"/>
    /// （专栏内文章需要人为排序，而不是按发布时间）。
    /// </summary>
    public class PostCollection
    {
        public Guid PostId { get; set; }
        public Post? Post { get; set; }

        public Guid CollectionId { get; set; }
        public Collection? Collection { get; set; }

        /// <summary>该文章在此专栏内的排序，越小越靠前</summary>
        public int SortOrder { get; set; }
    }
}
