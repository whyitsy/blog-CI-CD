using Blog.Domain.Entities.Base;

namespace Blog.Domain.Entities
{
    public class Tag : BaseEntity
    {
       public string Name { get; private set; } = string.Empty;

        // 导航属性
        public ICollection<Post> Posts { get; set; } = [];

        private Tag() { } // EF Core 需要一个无参构造函数

        public Tag(string name)
        {
            CreatedAt = DateTimeOffset.UtcNow;
            IsDeleted = false;
            Name = name;
        }

        public void Update(string name)
        {
            Name = name;
        }
    }
}
