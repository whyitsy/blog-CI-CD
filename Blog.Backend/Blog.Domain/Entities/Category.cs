using Blog.Domain.Entities.Base;

namespace Blog.Domain.Entities;

/// <summary>
/// 分类实体
/// </summary>
public class Category : BaseEntity
{
    public string Name { get; private set; } = string.Empty;

    // 导航属性
    public ICollection<Post> Posts { get; set; } = [];

    private Category() { } // EF Core 需要一个无参构造函数

    public Category(string name)
    {
        Name = name;
        CreatedAt = DateTimeOffset.UtcNow;
        IsDeleted = false;
    }

    public void Update(string name)
    {
        Name = name;
    }

}
