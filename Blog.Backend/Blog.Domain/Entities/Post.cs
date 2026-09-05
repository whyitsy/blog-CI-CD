using Blog.Domain.Entities.Base;

namespace Blog.Domain.Entities;


public class Post : BaseEntity
{
    public DateTimeOffset? UpdatedAt { get; private set; }
    public string Title { get; private set; }
    public string Content { get; private set; } 
    public string CoverImage { get; private set; }
    public Guid AuthorId { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }
    public bool IsPublished => PublishedAt.HasValue;
    public int ViewCount { get; private set; }
    
    public Guid CategoryId { get; set; }
    // 导航属性
    public Category? Category { get; set; } // 可空是因为配置的删除关系为setNull
    public ICollection<Tag> Tags { get; set; } = [];
    public Author? Author { get; set; }

    private Post() { } // EF Core 需要一个无参构造函数

    public Post(string title, string content, Guid authorId, Guid categoryId, string coverImage)
    {
        Title = title;
        Content = content;
        AuthorId = authorId;
        CategoryId = categoryId;
        CoverImage = coverImage;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void Update(string title, string content, Guid categoryId, string coverImage)
    {
        Title = title;
        Content = content;
        CategoryId = categoryId;
        CoverImage = coverImage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Publish()
    {
        // TODO: 是否需要检查是否已经发布过？如果要检查，则是应该抛出异常，还是直接返回Result？
        PublishedAt = DateTimeOffset.UtcNow;
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
