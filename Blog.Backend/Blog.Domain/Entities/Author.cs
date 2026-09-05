using Blog.Domain.Entities.Base;

namespace Blog.Domain.Entities
{
    public class Author: BaseEntity
    {
        public string Name { get; private set; }
        public string Email { get; private set; }
        public string Avatar { get; private set; }
        public string Bio { get; private set; }

        // 导航属性
        public ICollection<Post> Posts { get; set; } = [];

        private Author() { } // EF Core 需要一个无参构造函数

        public Author(string name, string email, string avatar, string bio)
        {
            Name = name;
            Email = email;
            Avatar = avatar;
            Bio = bio;
            CreatedAt = DateTimeOffset.UtcNow;
            IsDeleted = false;
        }

        public void Update(string name, string email, string avatar, string bio)
        {
            Name = name;
            Email = email;
            Avatar = avatar;
            Bio = bio;
        }
    }
}
