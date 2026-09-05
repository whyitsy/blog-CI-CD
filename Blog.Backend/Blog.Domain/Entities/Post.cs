using Blog.Domain.Entities.Base;

namespace Blog.Domain.Entities;


public class Post : BaseEntity
{
    private const int SummaryLength = 50;

    public DateTimeOffset? UpdatedAt { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Content { get; private set; } = string.Empty;
    public string CoverImage { get; private set; } = string.Empty;
    public Guid AuthorId { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public bool IsPublished => PublishedAt.HasValue;
    public int ViewCount { get; private set; }

    /// <summary>内容前 50 字摘要，写入时计算，避免列表页全量拉取 Content</summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>文章字数（字符数），写入时计算，用于站点统计</summary>
    public int WordCount { get; private set; }

    public Guid? CategoryId { get; set; } // 可空：配置的删除关系为 SetNull
    // 导航属性
    public Category? Category { get; set; }
    public ICollection<Tag> Tags { get; set; } = [];
    public Author? Author { get; set; }

    private Post() { } // EF Core 需要一个无参构造函数

    public Post(string title, string content, Guid authorId, Guid? categoryId, string coverImage, bool publishNow = false)
    {
        Title = title;
        Content = content;
        AuthorId = authorId;
        CategoryId = categoryId;
        CoverImage = coverImage;
        CreatedAt = DateTimeOffset.UtcNow;
        RefreshDerivedFields();
        if (publishNow) PublishedAt = DateTimeOffset.UtcNow;
    }

    public void Update(string title, string content, Guid? categoryId, string coverImage)
    {
        Title = title;
        Content = content;
        CategoryId = categoryId;
        CoverImage = coverImage;
        UpdatedAt = DateTimeOffset.UtcNow;
        RefreshDerivedFields();
    }

    /// <summary>根据当前内容刷新摘要与字数</summary>
    private void RefreshDerivedFields()
    {
        WordCount = Content?.Length ?? 0;
        Summary = Content is null || Content.Length <= SummaryLength
            ? Content ?? string.Empty
            : Content[..SummaryLength];
    }

    public void Publish()
    {
        // 幂等处理：重复发布直接保持原发布时间，避免覆盖首次发布时间（影响归档排序）
        PublishedAt ??= DateTimeOffset.UtcNow;
    }

    public void Unpublish()
    {
        PublishedAt = null;
    }

    public void IncrementViewCount()
    {
        ViewCount++;
    }
}
